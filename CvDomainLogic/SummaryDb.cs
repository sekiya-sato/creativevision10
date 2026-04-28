using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

public class SummaryDb {
	ExDatabase _db;
	ILogger<SummaryDb> _logger;
	public SummaryDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<SummaryDb>();
	}
	public async IAsyncEnumerable<StreamStepProgress> SummaryAllAsyncStream(SummaryDateParameter param) {
		_logger.LogInformation("処理開始");
		var start = DateTime.Now;

		// ToDo: 最終的に実行させる処理を整理
		var steps = new (string Name, Func<SummaryDateParameter, int> Action)[] {
			/*
			*/
			("Summary : Tran00Uriage", CalcSummaryStock<Tran00Uriage>),
			("Summary : Tran01Tenuri", CalcSummaryStock<Tran01Tenuri>),
			("Summary : Tran03Shiire", CalcSummaryStock<Tran03Shiire>),
			("Summary : Tran05Ido", CalcSummaryStock<Tran05Ido>),
			("Summary : Tran10IdoOut", CalcSummaryStock<Tran10IdoOut>),
			("Summary : Tran11IdoIn", CalcSummaryStock<Tran11IdoIn>)
		};
		//("Summary : Tran60Tana", CalcSummaryStock<Tran60Tana>),

		for (var index = 0; index < steps.Length; index++) {
			var (name, action) = steps[index];
			var startProgress = index * 100 / steps.Length;

			// ステップ開始通知
			yield return new StreamStepProgress(name, 0, startProgress, false, false);

			// 処理実行
			int count = 0;
			string? errorMsg = null;
			bool isError = false;
			try {
				count = action(param);
			}
			catch (Exception ex) {
				_logger.LogError(ex, $"処理エラー: {name}");
				isError = true;
				errorMsg = ex.Message;
			}

			var endProgress = (int)Math.Round((index + 1) * 100d / steps.Length, MidpointRounding.AwayFromZero);

			// ステップ完了通知
			yield return new StreamStepProgress(name, count, endProgress, false, isError, errorMsg);
		}

		var elapsed = DateTime.Now - start;
		_logger.LogInformation($"処理終了 {elapsed.TotalSeconds:0.0}s");

		yield return new StreamStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s");
	}

	private int CalcSummaryStock<T>(SummaryDateParameter param) where T : ITranDetail {
		var cnt = 0;
		var tableName = typeof(T).Name;
		var calcFlg = TranCalcBase.GetCalcSoko(tableName);
		var sql = CreateSummaryStockSql(tableName, "Id_Soko", calcFlg, Common.GetVdate());
		var sql2 = $"SELECT changes() AS updated_count";
		if (calcFlg.Item1 != 0 || calcFlg.Item2 != 0 || calcFlg.Item3 != 0 || calcFlg.Item4 != 0) {
			_db.BeginTransaction();
			var ret = _db.Execute(sql, param.DateYymmFrom, param.DateYymmTo + "99");
			cnt += _db.FirstOrDefault<int>(sql2);
			_db.CompleteTransaction();
		}
		if (typeof(ITranIdo).IsAssignableFrom(typeof(T))) {
			var calcFlg2 = TranCalcBase.GetCalcIdosaki(tableName);
			if (calcFlg2.Item1 != 0 || calcFlg2.Item2 != 0 || calcFlg2.Item3 != 0 || calcFlg2.Item4 != 0) {
				sql = CreateSummaryStockSql(tableName, "Id_Ido", calcFlg2, Common.GetVdate());
				_db.BeginTransaction();
				var ret = _db.Execute(sql, param.DateYymmFrom, param.DateYymmTo + "99");
				cnt += _db.FirstOrDefault<int>(sql2);
				_db.CompleteTransaction();
			}
		}
		return cnt;
	}
	/*
	[ObservableProperty]
	int inQty;
	/// <summary>
	/// 出庫数
	/// </summary>
	[ObservableProperty]
	int outQty;
	/// <summary>
	/// 移動中(入庫予定)
	/// </summary>
	[ObservableProperty]
	int transitQty;
	/// <summary>
	/// 調整数
	/// </summary>
	[ObservableProperty]
	int adjustQty;
	/// <summary>
	/// 棚卸日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string stocktakeDdate = "19010101";
	/// <summary>
	/// 棚卸数
	/// </summary>
	[ObservableProperty]
	int actualQty;
}	 
	 */
	private string CreateSummaryStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlg, long vdate) => $@"
INSERT INTO SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu, InQty, OutQty, TransitQty)
SELECT
  substr(t.DenDay, 1, 6) AS SumMonth,
  t.{idSoko} AS Id_Soko,
  json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
  json_extract(j.value, '$.Id_Col')    AS Id_Col,
  json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlg.Item1})   AS Su,
  {vdate} vdc,
  {vdate} vdu,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlg.Item2})   AS InQty,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlg.Item3})   AS OutQty,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlg.Item4})   AS TransitQty
FROM {tableName} AS t,
     json_each(t.Jmeisai) AS j
WHERE t.DenDay BETWEEN @0 AND @1
GROUP BY
  SumMonth,
  t.{idSoko},
  Id_Shohin,
  Id_Col,
  Id_Siz
ON CONFLICT(SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz) DO UPDATE
SET Su = Su + excluded.Su, vdu = {vdate},
    InQty = InQty + excluded.InQty,
    OutQty = OutQty + excluded.OutQty,
    TransitQty = TransitQty + excluded.TransitQty
;
";

	public int CalcSummaryRealStock(string DateYyyymm) {
		// DateTime.Now.ToDtStrDate2().Substring(0, 6)
		var cnt = 0;
		var deleteSql = "DELETE FROM SummaryRealStock";
		var sql = @$"
Insert Into SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su)
SELECT
  Id_Soko,
  Id_Shohin,
  Id_Col,
  Id_Siz,
  SUM(Su) AS TotalSu
FROM SummaryStock
WHERE SumMonth <= @0
GROUP BY
  Id_Soko,
  Id_Shohin,
  Id_Col,
  Id_Siz;
";
		var sql2 = $"SELECT changes() AS updated_count";
		_db.BeginTransaction();
		var ret = _db.Execute(deleteSql);
		ret = _db.Execute(sql, DateYyyymm);
		cnt += _db.FirstOrDefault<int>(sql2);
		_db.CompleteTransaction();
		return cnt;
	}
	public int CalcSummaryStockCumulative(string DateYyyymm) {
		var cnt = 0;
		/// 当月までの累計数量を更新 SummaryStock のCumulativeSuを更新
		var sql = @$"
WITH MonthlySum AS (
  SELECT 
    Id_Soko, 
    Id_Shohin, 
    Id_Col, 
    Id_Siz, 
    SumMonth,
    SUM(Su) OVER (
      PARTITION BY Id_Soko, Id_Shohin, Id_Col, Id_Siz 
      ORDER BY SumMonth
    -- 前月までの合計を計算（現在行を含まない）
    -- ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
    ) as CalcCumulative
  FROM SummaryStock
  WHERE SumMonth <= @0
)
UPDATE SummaryStock
SET CumulativeSu = (
  SELECT IFNULL(CalcCumulative, 0)
  FROM MonthlySum
  WHERE MonthlySum.Id_Soko   = SummaryStock.Id_Soko
    AND MonthlySum.Id_Shohin = SummaryStock.Id_Shohin
    AND MonthlySum.Id_Col    = SummaryStock.Id_Col
    AND MonthlySum.Id_Siz    = SummaryStock.Id_Siz
    AND MonthlySum.SumMonth  = SummaryStock.SumMonth
)
WHERE SumMonth <= @0;
";
		var sql2 = $"SELECT changes() AS updated_count";
		_db.BeginTransaction();
		var ret = _db.Execute(sql, DateYyyymm);
		cnt += _db.FirstOrDefault<int>(sql2);
		_db.CompleteTransaction();
		return cnt;
	}
}
