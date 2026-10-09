using System.Data;
using System.Globalization;
using CvAsset;
using CvBase;
using Newtonsoft.Json;

namespace CvDomainLogic;

/// <summary>個人情報を通常ログへ出さず、呼出元で限定公開する移行検査結果。</summary>
public sealed class PointHistoryMigrationReport {
	public int SourceRows { get; internal set; }
	public int IssueCount { get; private set; }
	public List<(long SeqNo, string Code, string Reason)> Issues { get; } = [];
	internal void Add(long seqNo, string code, string reason) {
		IssueCount++;
		if (Issues.Count < 1000) Issues.Add((seqNo, code, reason));
	}
	internal void RequireValid() {
		if (IssueCount != 0) throw new InvalidOperationException($"ポイント移行の事前検査で{IssueCount}件の不整合があります。InspectPointHistoryの例外一覧を確認してください。");
	}
}

public partial class ConvertDb {
	/// <summary>同じ移行元では固定する識別子。複数旧CVを統合する場合は元ごとに指定する。</summary>
	public string PointHistorySourceSystem { get; set; } = "CV";

	private sealed record PointMigrationMaps(Dictionary<string, long> Customers, Dictionary<string, long> Shops,
		Dictionary<string, long> Employees, HashSet<long> Accounts);

	/// <summary>旧履歴・参照・現在残高を読取検査する。Issuesは先頭1000件、IssueCountは全件。</summary>
	public PointHistoryMigrationReport InspectPointHistory(int chunkSize = 10000) => CheckPointHistory(LoadPointMaps(), chunkSize);

	/// <summary>旧確定履歴を追記する。isInitでも削除しない。全件検査後、チャンクごとに確定する。</summary>
	public int CnvTranPointHistory(bool isInit = true, int chunkSize = 10000) {
		_fromDb.BeginTransaction(IsolationLevel.Serializable);
		try {
			var maps = LoadPointMaps();
			CheckPointHistory(maps, chunkSize).RequireValid();
			var count = ImportPointHistory(maps, chunkSize);
			_toDb.BeginTransaction(IsolationLevel.Serializable);
			try {
				new PointCalcDb(_toDb).RebuildBalances();
				_toDb.CompleteTransaction();
			} catch { _toDb.AbortTransaction(); throw; }
			_fromDb.CompleteTransaction();
			return count;
		} catch { _fromDb.AbortTransaction(); throw; }
	}

	/// <summary>
	/// 初回切替専用。更新停止・バックアップ済みのコピーDBだけで呼ぶ。
	/// 事前検査→初期化→旧履歴取込→CV10売上再計算→残高照合を一つの取込先トランザクションにする。
	/// </summary>
	public int RebuildPointHistory(int chunkSize = 10000) {
		_fromDb.BeginTransaction(IsolationLevel.Serializable);
		try {
			var maps = LoadPointMaps();
			// 初期化後には既存移行キーがないため、ここでは旧元値・参照・残高を検査する。
			CheckPointHistory(maps, chunkSize, rebuilding: true).RequireValid();
			var calc = new PointCalcDb(_toDb);
			var customerIds = maps.Customers.Values.ToHashSet();
			foreach (var slip in _toDb.FetchDialect<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE OldSeqNo=0 AND (Id_Customer>0 OR UsePoint<>0)")) {
				calc.ValidateTenuriInput(slip);
				if (!DateTime.TryParseExact(slip.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
					|| slip.Id_Customer > 0 && (!customerIds.Contains(slip.Id_Customer) || !maps.Accounts.Contains(slip.Id_Customer)))
					throw new InvalidOperationException("CV10店舗売上の計上日・顧客・会員情報を確認してください。");
			}
			_toDb.BeginTransaction(IsolationLevel.Serializable);
			try {
				_toDb.ExecuteDialect("DELETE FROM TranPointEvent");
				_toDb.ExecuteDialect("UPDATE SummaryPoint SET Point=0");
				_toDb.ExecuteDialect("UPDATE MasterEndCustomerAccount SET Point=0");
				_toDb.ExecuteDialect("UPDATE Tran01Tenuri SET GrantPoint=0");
				var count = ImportPointHistory(maps, chunkSize);
				var months = _toDb.FetchDialect<string>("SELECT DISTINCT substr(DenDay,1,6) FROM Tran01Tenuri WHERE OldSeqNo=0 AND Id_Customer>0 ORDER BY 1");
				foreach (var month in months) {
					if (!DateTime.TryParseExact(month + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
						throw new InvalidOperationException("CV10店舗売上の計上月が不正です。");
					// 月ごとに計算状態を再読込し、回数制限を時系列で確定する。
					count = checked(count + new PointCalcDb(_toDb).Recalc(month, month));
				}
				new PointCalcDb(_toDb).RebuildBalances();
				_toDb.CompleteTransaction();
				_fromDb.CompleteTransaction();
				return count;
			} catch { _toDb.AbortTransaction(); throw; }
		} catch { _fromDb.AbortTransaction(); throw; }
	}

	private PointMigrationMaps LoadPointMaps() {
		if (string.IsNullOrWhiteSpace(PointHistorySourceSystem) || PointHistorySourceSystem.Length > 64
			|| PointHistorySourceSystem.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
			throw new ArgumentException("移行元識別子は64文字以内の英数字・ハイフン・アンダースコア・ピリオドで指定してください。");
		return new(
			_toDb.FetchDialect<MasterEndCustomer>("SELECT Id,Code FROM MasterEndCustomer").ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal),
			_toDb.FetchDialect<MasterTokui>("SELECT Id,Code FROM MasterTokui WHERE TenType=6").ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal),
			_toDb.FetchDialect<MasterShain>("SELECT Id,Code FROM MasterShain").ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal),
			_toDb.FetchDialect<long>("SELECT Id_Customer FROM MasterEndCustomerAccount").ToHashSet());
	}

	private IEnumerable<List<Dictionary<string, object>>> PointHistoryChunks(int chunkSize) {
		if (chunkSize is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(chunkSize));
		long last = long.MinValue;
		while (true) {
			var rows = _fromDb.SkipTake<Dictionary<string, object>>(0, chunkSize,
				"SELECT * FROM HC$TRAN_POINT_RIREKI WHERE SEQ_NO>@0 ORDER BY SEQ_NO", last);
			if (rows.Count == 0) yield break;
			yield return rows;
			last = PointLong(rows[^1], "SEQ_NO");
		}
	}

	private PointHistoryMigrationReport CheckPointHistory(PointMigrationMaps maps, int chunkSize, bool rebuilding = false) {
		var report = new PointHistoryMigrationReport();
		var totals = new Dictionary<string, long>(StringComparer.Ordinal);
		var additions = new Dictionary<long, long>();
		foreach (var rows in PointHistoryChunks(chunkSize)) {
			var valid = new List<TranPointEvent>(rows.Count);
			foreach (var rec in rows) {
				report.SourceRows++;
				var seq = PointLong(rec, "SEQ_NO");
				var code = PointRaw(rec, "顧客CD");
				try {
					var row = CreateLegacyPoint(rec, maps);
					totals[code] = checked(totals.GetValueOrDefault(code) + row.PointDelta);
					valid.Add(row);
				} catch (Exception ex) when (ex is ArgumentException or OverflowException or FormatException) {
					report.Add(seq, code, ex.Message);
				}
			}
			var existing = rebuilding ? new Dictionary<string, TranPointEvent>() : ExistingPointKeys(valid);
			foreach (var row in valid) {
				if (existing.TryGetValue(row.EventKey, out var old)) {
					if (!SameLegacyPoint(old, row)) report.Add(Common.DeserializeObject<LegacyPointHistory>(row.Jcalc)!.SeqNo, "", "既存の移行キーと内容が異なります。");
				} else additions[row.Id_Customer] = checked(additions.GetValueOrDefault(row.Id_Customer) + row.PointDelta);
			}
		}
		var real = _fromDb.Fetch<Dictionary<string, object>>("SELECT 顧客CD,REALポイント FROM HC$POINT_REAL")
			.ToDictionary(x => PointRaw(x, "顧客CD"), x => PointLong(x, "REALポイント"), StringComparer.Ordinal);
		foreach (var code in totals.Keys.Union(real.Keys))
			if (totals.GetValueOrDefault(code) != real.GetValueOrDefault(code)) report.Add(0, code, "旧履歴合計と旧現在残高が一致しません。");
		var current = rebuilding ? new Dictionary<long, long>() : _toDb.FetchDialect<TranPointEvent>(
			"SELECT Id_Customer,SUM(PointDelta) AS PointDelta FROM TranPointEvent GROUP BY Id_Customer").ToDictionary(x => x.Id_Customer, x => x.PointDelta);
		foreach (var id in current.Keys.Union(additions.Keys)) {
			try { _ = checked((int)id); _ = checked((int)checked(current.GetValueOrDefault(id) + additions.GetValueOrDefault(id))); }
			catch (OverflowException) { report.Add(0, "", "顧客IDまたは残高がint範囲を超えています。"); }
		}
		return report;
	}

	private int ImportPointHistory(PointMigrationMaps maps, int chunkSize) {
		var count = 0;
		foreach (var rows in PointHistoryChunks(chunkSize)) {
			var converted = rows.Select(rec => CreateLegacyPoint(rec, maps)).ToList();
			_toDb.BeginTransaction(IsolationLevel.Serializable);
			try {
				var existing = ExistingPointKeys(converted);
				foreach (var row in converted) {
					if (existing.TryGetValue(row.EventKey, out var old) && !SameLegacyPoint(old, row))
						throw new InvalidOperationException("旧ポイント履歴が事前検査後に変更されています。");
				}
				var added = converted.Where(row => !existing.ContainsKey(row.EventKey)).ToList();
				_toDb.InsertBulk(added);
				count = checked(count + added.Count);
				_toDb.CompleteTransaction();
			} catch { _toDb.AbortTransaction(); throw; }
		}
		return count;
	}

	private Dictionary<string, TranPointEvent> ExistingPointKeys(List<TranPointEvent> rows) => rows.Count == 0 ? [] :
		_toDb.FetchDialect<TranPointEvent>("SELECT * FROM TranPointEvent WHERE EventKey IN (@0)", (object)rows.Select(x => x.EventKey).ToArray())
			.ToDictionary(x => x.EventKey, StringComparer.Ordinal);

	private static bool SameLegacyPoint(TranPointEvent old, TranPointEvent row) {
		try { return
		old.EventType == row.EventType && old.PointDelta == row.PointDelta && old.DenDay == row.DenDay
		&& old.Id_Customer == row.Id_Customer && old.Id_Tenpo == row.Id_Tenpo && old.Id_Shain == row.Id_Shain
		&& old.Id_Tenuri == 0 && old.Id_PointBase == 0 && old.Id_PointRank == 0 && old.Id_PointBonus == 0 && old.Id_OriginalEvent == 0
		&& old.SourceVdu == 0 && old.Memo == row.Memo
		&& Common.SerializeObject(Common.DeserializeObject<LegacyPointHistory>(old.Jcalc)!) == row.Jcalc;
		} catch (JsonException) { return false; }
	}

	private TranPointEvent CreateLegacyPoint(Dictionary<string, object> rec, PointMigrationMaps maps) {
		var source = new LegacyPointHistory {
			SourceSystem = PointHistorySourceSystem, SeqNo = PointLong(rec, "SEQ_NO"),
			CreatedAt = PointDecimal(rec, "VDATE_CREATE"), UpdatedAt = PointDecimal(rec, "VDATE_UPDATE"),
			CustomerCode = PointRaw(rec, "顧客CD"), Rank = PointRaw(rec, "ランク"), Day = PointRaw(rec, "ポイント計上日"),
			ShopCode = PointRaw(rec, "店舗CD"), EmployeeCode = PointRaw(rec, "社員CD"), RegisterNo = PointLong(rec, "レジNO"), ReceiptNo = PointLong(rec, "レシートNO"),
			TransactionType = checked((int)PointLong(rec, "取引区分")), OriginType = checked((int)PointLong(rec, "発生区分")),
			GrantPoints = PointLong(rec, "付与ポイント数"), UsePoints = PointLong(rec, "使用ポイント数"), ExpirePoints = PointLong(rec, "失効ポイント数"), Memo = PointRaw(rec, "備考")
		};
		if (source.SeqNo <= 0 || !DateTime.TryParseExact(source.Day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
			throw new ArgumentException("旧SEQ_NOまたはポイント計上日が不正です。");
		if (!maps.Customers.TryGetValue(source.CustomerCode, out var customer) || !maps.Accounts.Contains(customer))
			throw new ArgumentException("顧客または会員情報が未解決です。");
		var shop = ResolvePointReference(maps.Shops, source.ShopCode, "店舗");
		var employee = ResolvePointReference(maps.Employees, source.EmployeeCode, "社員");
		var json = Common.SerializeObject(source);
		if (json.Length > 4000 || source.Memo.Length > 200) throw new ArgumentException("旧履歴JSONまたは備考が保存上限を超えています。");
		var vdate = Common.GetVdate();
		return new TranPointEvent {
			EventKey = $"LEGACY:{PointHistorySourceSystem}:POINT:{source.SeqNo}", EnEventType = EnumPointEventType.LegacyHistory,
			DenDay = source.Day, Id_Customer = customer, Id_Tenpo = shop, Id_Shain = employee,
			PointDelta = checked(source.GrantPoints - source.UsePoints - source.ExpirePoints), Jcalc = json, Memo = source.Memo, Vdc = vdate, Vdu = vdate
		};
	}

	private static long ResolvePointReference(Dictionary<string, long> map, string code, string name) =>
		code is "" or "." ? 0 : map.TryGetValue(code, out var id) ? id : throw new ArgumentException($"{name}参照が未解決です。");
	private static string PointRaw(Dictionary<string, object> row, string key) =>
		row.TryGetValue(key, out var value) && value != null && value != DBNull.Value ? System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
	private static decimal PointDecimal(Dictionary<string, object> row, string key) =>
		decimal.TryParse(PointRaw(row, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException("旧履歴の数値が不正です。");
	private static long PointLong(Dictionary<string, object> row, string key) {
		var value = PointDecimal(row, key);
		if (value != decimal.Truncate(value)) throw new FormatException("旧履歴の整数値に小数が含まれます。");
		return checked((long)value);
	}
}
