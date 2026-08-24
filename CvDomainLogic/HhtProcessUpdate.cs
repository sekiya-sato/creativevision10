using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// HHTデータ更新（<see cref="TranVulcanHht"/> → Tran系各テーブル）。
/// <para>
/// 仕様は `Doc/spec/2026-08-24_HHTデータ更新詳細設計.md` を参照する。
/// 画面(HHTデータ更新)から <c>Msg058_HhtDataUpdate</c> で呼ばれる。
/// </para>
/// <para>
/// 伝票の生成と在庫集計をサーバ側で完結させるのは <see cref="StocktakeDb.FixStocktake"/> と同じ判断で、
/// バッチ処理であり1件ずつgRPCを往復しないためである。
/// 在庫集計は <see cref="SummaryDb"/> を直接呼ぶ（<c>WriteEffectRunner</c> は CvServer 層のため参照できない）。
/// </para>
/// </summary>
public partial class HhtProcess {
	/// <summary>VULCAN区分。ファイルレイアウトの 1桁(1-9,A-C) を取込時に数値化した値</summary>
	private const int TypeUriage = 1;
	private const int TypeHenpin = 2;
	private const int TypeNyuko = 3;
	private const int TypeShukko = 4;
	private const int TypeShiire = 5;
	private const int TypeShiireHenpin = 6;
	private const int TypeTanaoroshi = 7;
	private const int TypeHachu = 8;
	private const int TypeOroshi = 9;
	private const int TypeOroshiHenpin = 10;
	private const int TypeIdo = 11;
	private const int TypeKyakusu = 12;

	/// <summary>販売区分。0=プロパー / 1=セール / 2=社販 / 9=未使用。入庫・出庫では 0=買取 / 1=委託</summary>
	private const int HanProper = 0;
	private const int HanSale = 1;
	private const int HanShahan = 2;

	/// <summary>JANとして照合する最小桁数。マスタ側の Jan1 にサイズCD("24"等)の誤登録が混在するため桁数で弾く</summary>
	private const int JanMinLength = 8;

	/// <summary>IN句へ一度に並べるキーの上限</summary>
	private const int InClauseChunkSize = 1000;

	/// <summary><see cref="TranVulcanHht.ErrorMsg"/> の桁数上限</summary>
	private const int ErrorMsgMaxLength = 1000;

	/// <summary>
	/// HHTデータ更新をストリーミングで実行する。
	/// <para>
	/// 進捗は1ステップぶんしか返さない（<see cref="StocktakeDb.FixAsyncStream"/> と同じ構成）。
	/// 成功・エラー・重複の内訳は画面側が完了後に <see cref="TranVulcanHht"/> を数え直して表示する。
	/// </para>
	/// </summary>
	public IAsyncEnumerable<StreamStepProgress> UpdateVulcan2TranAsyncStream(HhtUpdateParameter param) =>
		StreamStepProgressRunner.Run(
			[($"HHTデータ更新 : Tran系への展開 {DescribeTarget(param)}", RunUpdateInTransaction)],
			param, _logger, "HHTデータ更新を開始", "HHTデータ更新エラー: {StepName}", "HHTデータ更新を終了");

	private static string DescribeTarget(HhtUpdateParameter param) {
		if (param.TargetIds is { Length: > 0 }) {
			return $"(指定 {param.TargetIds.Length}件)";
		}
		var from = string.IsNullOrEmpty(param.DateFrom) ? "" : param.DateFrom;
		var to = string.IsNullOrEmpty(param.DateTo) ? "" : param.DateTo;
		return from.Length == 0 && to.Length == 0 ? "(全期間)" : $"({from}～{to})";
	}

	private int RunUpdateInTransaction(HhtUpdateParameter param) {
		var started = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			started = true;
			var result = UpdateVulcan2Tran(param);
			_db.CompleteTransaction();
			started = false;
			_logger.LogInformation("HHTデータ更新 伝票={Slip} 成功行={Ok} エラー行={Ng} 重複行={Dup} 対象外行={Skip}",
				result.SlipCount, result.SuccessRows, result.ErrorRows, result.DuplicateRows, result.SkippedRows);
			return result.SlipCount;
		}
		catch {
			if (started) {
				_db.AbortTransaction();
			}
			throw;
		}
	}

	/// <summary>
	/// HHTデータ更新の本体。トランザクションは呼び出し元が張る前提でここでは張らない。
	/// </summary>
	public HhtUpdateResult UpdateVulcan2Tran(HhtUpdateParameter param) {
		var where = BuildTargetWhere(param, out var args);

		// 対象行を「処理中(-1)」にして他端末との競合を避けつつ、前回のエラー内容をクリアする。
		// 再変換で前回のエラーが残ると、直したのに直っていないように見えるため必ず消す。
		_db.Execute($"Update {nameof(TranVulcanHht)} set VdCnvDate=-1, ErrorMsg='' {where}", args);
		var rows = _db.Fetch<TranVulcanHht>(
			"where VdCnvDate=-1 order by BackupFileName, HhtNo, Serial, LineNo");
		if (rows.Count == 0) {
			return new HhtUpdateResult(0, 0, 0, 0, 0);
		}

		var cache = LoadMasterCache(rows);
		var result = new HhtUpdateResult(0, 0, 0, 0, 0);
		var vdate = Common.GetVdate();

		// 重複受信は伝票を作らずエラーにする（同じ実棚数・売上が二重計上されるのを防ぐ）。
		// グルーピングより先に除くのが重要。再受信された行はヘッダキーが元の行と完全に一致するため、
		// 残したままだと同じ伝票ランへ吸収され、1件目まで巻き込んでエラーになってしまう
		var duplicates = FindDuplicates(rows);
		foreach (var row in rows.Where(x => duplicates.ContainsKey(x.Id))) {
			StoreError(row, duplicates[row.Id]);
		}
		result = result with { DuplicateRows = duplicates.Count };
		var groups = GroupIntoSlips([.. rows.Where(x => !duplicates.ContainsKey(x.Id))]);
		var shiireRelateIds = new HashSet<long>();
		var uriMonths = new SortedSet<string>();
		var kaiMonths = new SortedSet<string>();

		foreach (var group in groups) {
			// 客数(12)は対応する伝票がないため対象外とする。
			// ToDo: Tran02PosSeisan.KyakuSu へ入れる場合は、POS日次精算との突合ルールを別途決める必要がある（決定 12-B）
			if (group.Type0 == TypeKyakusu) {
				foreach (var row in group.Rows) {
					row.VdCnvDate = vdate;
					row.TargetTableName = string.Empty;
					row.TargetId = 0;
					row.ErrorMsg = string.Empty;
					_db.Update(row);
				}
				result = result with { SkippedRows = result.SkippedRows + group.Rows.Count };
				continue;
			}

			var errors = new List<string>();
			var slip = BuildSlip(group, cache, errors);
			if (slip == null || errors.Count > 0) {
				StoreGroupErrors(group, errors);
				result = result with { ErrorRows = result.ErrorRows + group.Rows.Count };
				continue;
			}

			_db.Insert(slip.Entity);
			var slipId = ((BaseDbClass)slip.Entity).Id;
			ApplyStockEffect(slip.Entity, slip.TableName, slipId);

			if (slip.Entity is Tran03Shiire shiire && shiire.RelateNo1 > 0) {
				shiireRelateIds.Add(shiire.RelateNo1);
			}
			if (slip.Entity is Tran00Uriage uriage) {
				uriMonths.Add(uriage.KakeDay.Length >= 6 ? uriage.KakeDay[..6] : uriage.KakeDay);
			}
			if (slip.Entity is Tran03Shiire shiire2) {
				kaiMonths.Add(shiire2.KakeDay.Length >= 6 ? shiire2.KakeDay[..6] : shiire2.KakeDay);
			}

			foreach (var row in group.Rows) {
				row.VdCnvDate = vdate;
				row.TargetTableName = slip.TableName;
				row.TargetId = slipId;
				row.ErrorMsg = string.Empty;
				_db.Update(row);
			}
			result = result with {
				SlipCount = result.SlipCount + 1,
				SuccessRows = result.SuccessRows + group.Rows.Count
			};
		}

		// 取りこぼし防止。ここに残る -1 は伝票も作られずエラーも書かれていない想定外の行なので未変換へ戻す
		_db.Execute($"Update {nameof(TranVulcanHht)} set VdCnvDate=0 where VdCnvDate=-1");

		// 発注残の自動完了。仕入が RelateNo1 で紐付く発注を再判定する
		if (shiireRelateIds.Count > 0) {
			new CompletionDb(_db).CalcHachuEndFlag(shiireRelateIds);
		}
		// 売掛・買掛は月次一括の引き直しなので、対象年月の範囲でまとめて1回だけ呼ぶ
		var summaryDb = new SummaryDb(_db);
		if (uriMonths.Count > 0) {
			summaryDb.CalcSummaryUriKake(uriMonths.Min!, uriMonths.Max!);
		}
		if (kaiMonths.Count > 0) {
			summaryDb.CalcSummaryKaiKake(kaiMonths.Min!, kaiMonths.Max!);
		}
		return result;
	}

	/// <summary>対象行の抽出条件を組み立てる</summary>
	private static string BuildTargetWhere(HhtUpdateParameter param, out object[] args) {
		var conditions = new List<string> { "VdCnvDate=0" };
		var values = new List<object>();

		if (param.TargetIds is { Length: > 0 }) {
			// Id は long なのでSQLへ直接埋め込む(パラメータでは動的型比較で一致しない)
			conditions.Add($"Id in ({string.Join(",", param.TargetIds.Distinct())})");
		}
		else {
			if (!string.IsNullOrWhiteSpace(param.DateFrom)) {
				conditions.Add($"DenDay >= @{values.Count}");
				values.Add(param.DateFrom);
			}
			if (!string.IsNullOrWhiteSpace(param.DateTo)) {
				conditions.Add($"DenDay <= @{values.Count}");
				values.Add(param.DateTo);
			}
			if (param.Types is { Length: > 0 }) {
				conditions.Add($"Type0 in ({string.Join(",", param.Types.Distinct())})");
			}
			if (!param.RetryError) {
				conditions.Add("ErrorMsg = ''");
			}
		}

		args = [.. values];
		return "where " + string.Join(" and ", conditions);
	}

	/// <summary>
	/// 重複受信を検出する。キーは Type0 + DenDay + Shop + HhtNo + Serial。
	/// <para>
	/// 同一HT・同一日で Serial が重複することは正常運用では起きない。
	/// 既に変換済みの行と重複する場合と、同一バッチ内で重複する場合の両方を対象にする。
	/// </para>
	/// </summary>
	/// <returns>エラーにする行Id → エラー内容</returns>
	private Dictionary<long, string> FindDuplicates(List<TranVulcanHht> rows) {
		var result = new Dictionary<long, string>();
		var seen = new Dictionary<string, TranVulcanHht>();
		foreach (var row in rows) {
			var key = DuplicateKey(row);
			if (seen.TryGetValue(key, out var first)) {
				result[row.Id] = Truncate($"E016 重複受信: {first.BackupFileName} 行{first.LineNo} と同一 (HTNo={row.HhtNo} Serial={row.Serial})");
				continue;
			}
			seen.Add(key, row);
		}

		// 既に変換済み(VdCnvDate>0)の行との重複
		foreach (var chunk in seen.Values.Chunk(InClauseChunkSize)) {
			var serials = string.Join(",", chunk.Select(x => x.Serial).Distinct());
			var converted = _db.Fetch<TranVulcanHht>(
				$"where VdCnvDate > 0 and Serial in ({serials})");
			if (converted.Count == 0) {
				continue;
			}
			var convertedMap = converted
				.GroupBy(DuplicateKey)
				.ToDictionary(g => g.Key, g => g.First());
			foreach (var row in chunk) {
				if (convertedMap.TryGetValue(DuplicateKey(row), out var already)) {
					result[row.Id] = Truncate(
						$"E016 重複受信: 変換済みデータと同一 (先行={already.TargetTableName}#{already.TargetId} {already.BackupFileName} 行{already.LineNo})");
				}
			}
		}
		return result;
	}

	private static string DuplicateKey(TranVulcanHht row) =>
		$"{row.Type0}\t{row.DenDay}\t{row.Shop}\t{row.HhtNo}\t{row.Serial}";

	/// <summary>
	/// 連続ラン方式で伝票単位にまとめる。
	/// <para>
	/// ヘッダキーが変わったところで区切り、非連続で同キーが再出現した場合は別伝票にする。
	/// 売上・返品は DenNo が顧客CDで伝票番号ではないため、キーだけで束ねると同一顧客の別会計が融合する。
	/// HHTのスキャン順(Serial)では1会計が連続ランになる。
	/// </para>
	/// </summary>
	private static List<HhtSlipGroup> GroupIntoSlips(List<TranVulcanHht> rows) {
		var groups = new List<HhtSlipGroup>();
		HhtSlipGroup? current = null;
		var currentKey = string.Empty;

		foreach (var row in rows) {
			// BackupFileName もキーに含める。別ファイルの行が1伝票へ混ざらないようにするため
			var key = $"{row.BackupFileName}\t{row.Type0}\t{row.DenDay}\t{row.Shop}\t{row.HhtNo}\t{row.Tanto}\t{row.HanKubun}\t{row.DenNo}\t{row.ToriSaki}";
			if (current == null || key != currentKey) {
				current = new HhtSlipGroup(row.Type0, []);
				groups.Add(current);
				currentKey = key;
			}
			current.Rows.Add(row);
		}
		return groups;
	}

	/// <summary>エラー内容を格納して未変換(0)へ戻す</summary>
	private void StoreError(TranVulcanHht row, string message) {
		row.VdCnvDate = 0;
		row.TargetTableName = string.Empty;
		row.TargetId = 0;
		row.ErrorMsg = Truncate(message);
		_db.Update(row);
	}

	/// <summary>
	/// 伝票単位のエラーを格納する。原因行には具体的な内容、同一伝票の他行には連鎖(E900)を書く。
	/// </summary>
	private void StoreGroupErrors(HhtSlipGroup group, List<string> errors) {
		// 行を特定できるエラー("行=n")はその行へ、特定できないヘッダのエラーは全行へ書く
		var headerErrors = errors.Where(x => !x.Contains("行=", StringComparison.Ordinal)).ToList();
		var firstLine = group.Rows.Count > 0 ? group.Rows[0].LineNo : 0;

		foreach (var row in group.Rows) {
			var own = errors.Where(x => x.Contains($"行={row.LineNo}", StringComparison.Ordinal)).ToList();
			var messages = new List<string>(headerErrors);
			messages.AddRange(own);
			if (messages.Count == 0) {
				messages.Add($"E900 同一伝票内にエラー行あり (行={firstLine})");
			}
			StoreError(row, string.Join(" / ", messages));
		}
	}

	private static string Truncate(string message) =>
		message.Length <= ErrorMsgMaxLength ? message : message[..ErrorMsgMaxLength];

	/// <summary>
	/// 在庫集計へ反映する。移動系(<see cref="ITranIdo"/>)は倉庫軸と移動先軸の2回ぶん計算する。
	/// 棚卸・受注・発注は <see cref="ITranSoko"/> ではないので在庫を動かさない。
	/// </summary>
	private void ApplyStockEffect(object entity, string tableName, long id) {
		if (entity is not ITranSoko) {
			return;
		}
		var summaryDb = new SummaryDb(_db);
		summaryDb.CalcTran2SummaryStock(tableName, nameof(ITranSoko.Id_Soko), id, invertFlag: false);
		if (entity is ITranIdo) {
			summaryDb.CalcTran2SummaryStock(tableName, nameof(ITranIdo.Id_Ido), id, invertFlag: false);
		}
	}

	/// <summary>HHTデータ更新の実行結果</summary>
	/// <param name="SlipCount">生成した伝票数</param>
	/// <param name="SuccessRows">変換できた行数</param>
	/// <param name="ErrorRows">エラーになった行数</param>
	/// <param name="DuplicateRows">重複受信としてエラーにした行数</param>
	/// <param name="SkippedRows">対象外(客数)として伝票を作らず完了にした行数</param>
	public readonly record struct HhtUpdateResult(int SlipCount, int SuccessRows, int ErrorRows, int DuplicateRows, int SkippedRows);

	/// <summary>伝票1枚ぶんのVULCAN行</summary>
	private sealed record HhtSlipGroup(int Type0, List<TranVulcanHht> Rows);

	/// <summary>生成した伝票と格納先テーブル名</summary>
	private sealed record HhtSlip(string TableName, object Entity);
}
