using System.Globalization;
using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// ポイント失効。基準日時点で台帳合計が正の顧客について、最終購入日から失効月数(MasterPointBase.ExpireMonths)経過、
/// または退会済みなら残高を一括で失効させ、台帳(TranPointEvent, EventType=Expire)に記録して残高(SummaryPoint・会員ポイント)を更新する。
/// <para>
/// EventKey は顧客×基準日で一意のため、同じ基準日の再実行で二重に失効しない。失効後に売上が訂正されても失効は作り直さない（追記のみ）。
/// </para>
/// </summary>
public sealed class PointExpireDb(ExDatabase db) {
	private readonly ILogger<PointExpireDb> _logger = new NLogExtender<PointExpireDb>();
	private const string ProcessNameExpire = "ポイント失効";
	private const long ExpectedDurationExpireSeconds = 600; // 顧客単位の集計のみのため10分

	/// <summary>指定基準日(yyyyMMdd)で失効を実行する</summary>
	/// <returns>追記した失効件数</returns>
	public int Expire(string baseDay) {
		if (!DateTime.TryParseExact(baseDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) {
			throw new ArgumentException($"基準日は実在する日付(yyyyMMdd)で指定してください: {baseDay}");
		}
		var count = 0;
		db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		try {
			// 付与計算と同じく、基準日を期間に含む有効版のうちコード順の先頭を適用する
			var pointBase = db.FetchDialect<MasterPointBase>("SELECT * FROM MasterPointBase WHERE IsEnabled=1 AND DayFrom<=@0 AND DayTo>=@0 ORDER BY Code, Version", baseDay).FirstOrDefault();
			var limitDay = pointBase is { ExpireMonths: > 0 } ? day.AddMonths(-pointBase.ExpireMonths).ToString("yyyyMMdd", CultureInfo.InvariantCulture) : null;
			// 失効額は基準日時点の残高と現在残高の小さい方。後日の手動実行でも、基準日より後の付与(再来店・未来日付)は失効させない
			var asOf = db.FetchDialect<TranPointEvent>(
				"SELECT Id_Customer, SUM(PointDelta) AS PointDelta FROM TranPointEvent WHERE Id_Customer>0 AND DenDay<=@0 GROUP BY Id_Customer HAVING SUM(PointDelta)>0", baseDay)
				.ToDictionary(x => x.Id_Customer, x => x.PointDelta);
			var balances = db.FetchDialect<TranPointEvent>(
				"SELECT Id_Customer, SUM(PointDelta) AS PointDelta FROM TranPointEvent WHERE Id_Customer>0 GROUP BY Id_Customer HAVING SUM(PointDelta)>0")
				.Where(x => asOf.ContainsKey(x.Id_Customer)).ToList();
			foreach (var row in balances) {
				row.PointDelta = Math.Min(row.PointDelta, asOf[row.Id_Customer]);
			}
			if (balances.Count == 0) {
				db.CompleteTransaction();
				return 0;
			}
			var accounts = db.FetchDialect<MasterEndCustomerAccount>(
				"SELECT Id_Customer, IsWithdrawalFlag, WithdrawnDate, LastVisitDate FROM MasterEndCustomerAccount WHERE Id_Customer IN (SELECT Id_Customer FROM TranPointEvent GROUP BY Id_Customer HAVING SUM(PointDelta)>0)")
				.GroupBy(x => x.Id_Customer).ToDictionary(x => x.Key, x => x.First());
			// 最終購入日は売上区分の伝票日の最大（基準日以前）。移行顧客は会員情報の最終来店日も見て遅い方を使う
			var lastPurchase = db.FetchDialect<Tran01Tenuri>(
				"SELECT Id_Customer, MAX(DenDay) AS DenDay FROM Tran01Tenuri WHERE Id_Customer>0 AND Kubun IN (@0,@1,@2) AND DenDay<=@3 GROUP BY Id_Customer",
				(int)EnumUri01.Uriage, (int)EnumUri01.UriSale, (int)EnumUri01.UriShahan, baseDay)
				.ToDictionary(x => x.Id_Customer, x => x.DenDay);
			var vdate = Common.GetVdate();
			var deltas = new Dictionary<long, long>();
			foreach (var row in balances) {
				var account = accounts.GetValueOrDefault(row.Id_Customer);
				var withdrawn = account is { IsWithdrawalFlag: 1 } && IsDate(account.WithdrawnDate) && string.CompareOrdinal(account.WithdrawnDate, baseDay) <= 0;
				var last = Later(lastPurchase.GetValueOrDefault(row.Id_Customer), IsDate(account?.LastVisitDate) && string.CompareOrdinal(account!.LastVisitDate, baseDay) <= 0 ? account.LastVisitDate : null);
				var elapsed = limitDay != null && last != null && string.CompareOrdinal(last, limitDay) <= 0;
				if (!withdrawn && !elapsed) {
					continue;
				}
				var eventKey = $"EXPIRE:{row.Id_Customer}:{baseDay}";
				if (db.FetchDialect<long>("SELECT COUNT(*) FROM TranPointEvent WHERE EventKey=@0", eventKey).FirstOrDefault() > 0) {
					continue;
				}
				db.Insert(new TranPointEvent {
					EventKey = eventKey,
					DenDay = baseDay,
					Id_Customer = row.Id_Customer,
					EventType = (int)EnumPointEventType.Expire,
					PointDelta = -row.PointDelta,
					Id_PointBase = withdrawn ? 0 : pointBase!.Id,
					Jcalc = Common.SerializeObject(new {
						Rule = withdrawn ? "Withdrawn" : "Elapsed",
						BaseDay = baseDay,
						Balance = row.PointDelta,
						LastPurchase = last,
						pointBase?.ExpireMonths,
						LimitDay = limitDay,
						WithdrawnDate = withdrawn ? account!.WithdrawnDate : null,
					}),
					Memo = withdrawn ? "退会の為失効" : $"最終購入日から{pointBase!.ExpireMonths}か月経過の為失効",
					Vdc = vdate,
					Vdu = vdate,
				});
				deltas[row.Id_Customer] = -row.PointDelta;
				count++;
			}
			new PointCalcDb(db).ApplyBalance(deltas);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		_logger.LogInformation("ポイント失効: 基準日={BaseDay}, 件数={Count}", baseDay, count);
		return count;
	}

	/// <summary>ポイント失効（手動・自動実行）。マニュアル排他を取得して実行する</summary>
	public IAsyncEnumerable<StreamStepProgress> ExpireAsyncStream(PointExpireParameter param, int isAutoExec = (int)EmSysHistType.ManualExec) {
		(string Name, Func<PointExpireParameter, int> Action)[] steps = [
			($"Point : Expire {param.BaseDay}", p => Expire(p.BaseDay)),
		];
		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了",
			new ManualLockDb(db),
			ProcessNameExpire,
			ExpectedDurationExpireSeconds,
			isAutoExec);
	}

	private static bool IsDate(string? value) => value?.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

	private static string? Later(string? a, string? b) => a == null ? b : b == null ? a : string.CompareOrdinal(a, b) >= 0 ? a : b;
}
