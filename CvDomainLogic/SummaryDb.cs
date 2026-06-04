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
	public IAsyncEnumerable<StreamStepProgress> SummaryAllAsyncStream(SummaryDateParameter param) {
		(string Name, Func<SummaryDateParameter, int> Action)[] steps = [
			/*
			*/
			("Summary : Tran00Uriage", CalcSummaryStock<Tran00Uriage>),
			("Summary : Tran01Tenuri", CalcSummaryStock<Tran01Tenuri>),
			("Summary : Tran03Shiire", CalcSummaryStock<Tran03Shiire>),
			("Summary : Tran05Ido", CalcSummaryStock<Tran05Ido>),
			("Summary : Tran10IdoOut", CalcSummaryStock<Tran10IdoOut>),
			("Summary : Tran11IdoIn", CalcSummaryStock<Tran11IdoIn>)
		];
		//("Summary : Tran60Tana", CalcSummaryStock<Tran60Tana>),

		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了");
	}
	/// <summary>
	/// 年月指定でTranテーブルからSummaryStockを更新する(レコード CUD) SummaryRealStockは後でCalcSummaryRealStock()で一括更新する必要がある
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="param"></param>
	/// <returns></returns>
	private int CalcSummaryStock<T>(SummaryDateParameter param) where T : ITranDetail {
		var cnt = 0;
		var tableName = typeof(T).Name;
		var calcFlg = TranCalcBase.GetCalcSoko(tableName);
		var sql = CreateSummaryStockSql(tableName, "Id_Soko", calcFlg, Common.GetVdate(), "t.DenDay BETWEEN @0 AND @1");
		var period = $"{param.DateYymmFrom}-{param.DateYymmTo}";
		if (calcFlg.Item1 != 0 || calcFlg.Item2 != 0 || calcFlg.Item3 != 0 || calcFlg.Item4 != 0) {
			cnt += ExecuteInTransaction(sql, [param.DateYymmFrom, param.DateYymmTo + "99"], "CalcSummaryStock", $"{tableName}:Id_Soko", period);
		}
		if (typeof(ITranIdo).IsAssignableFrom(typeof(T))) {
			var calcFlg2 = TranCalcBase.GetCalcIdosaki(tableName);
			if (calcFlg2.Item1 != 0 || calcFlg2.Item2 != 0 || calcFlg2.Item3 != 0 || calcFlg2.Item4 != 0) {
				sql = CreateSummaryStockSql(tableName, "Id_Ido", calcFlg2, Common.GetVdate(), "t.DenDay BETWEEN @0 AND @1");
				cnt += ExecuteInTransaction(sql, [param.DateYymmFrom, param.DateYymmTo + "99"], "CalcSummaryStock", $"{tableName}:Id_Ido", period);
			}
		}
		return cnt;
	}
	/// <summary>
	/// Id指定でTranテーブルからSummaryStockおよびSummaryRealStockを更新する(レコード CUD)
	/// </summary>
	/// <param name="id"></param>
	/// <param name="invertFlg">在庫計算のフラグを反転させるかどうか</param>
	/// <returns></returns>
	public int CalcTran2SummaryStock(string tablename, string idSoko, long id, bool invertFlg) {
		var cnt = 0;
		var calcFlg = TranCalcBase.GetCalcSoko(tablename, invertFlg);
		var sql = CreateRealStockSql(tablename, idSoko, calcFlg, Common.GetVdate(), "t.Id=@0");
		var sql2 = $"SELECT changes() AS updated_count";
		if (calcFlg.Item1 != 0) {
			var ret = _db.Execute(sql, id);
			if (ret < 0)
				_logger.LogWarning("CalcTran2SummaryStock:SummaryRealStock {TableName} Id={Id} updated {Count} records", tablename, id, ret);
			cnt += _db.FirstOrDefault<int>(sql2);
			if (calcFlg.Item1 != 0 || calcFlg.Item2 != 0 || calcFlg.Item3 != 0 || calcFlg.Item4 != 0) {
				sql = CreateSummaryStockSql(tablename, idSoko, calcFlg, Common.GetVdate(), "t.Id=@0");
				ret = _db.Execute(sql, id);
				if (ret < 0)
					_logger.LogWarning("CalcTran2SummaryStock:SummaryStock {TableName} Id={Id} updated {Count} records", tablename, id, ret);
				cnt += _db.FirstOrDefault<int>(sql2);
			}
		}
		return cnt;
	}
	private int ExecuteInTransaction(string sql, object[] args, string operationName, string targetName, string period) {
		try {
			_db.BeginTransaction();
			_ = _db.Execute(sql, args);
			var updatedCount = _db.FirstOrDefault<int>("SELECT changes() AS updated_count");
			_db.CompleteTransaction();
			return updatedCount;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "{OperationName} でトランザクション例外 Target={TargetName} Period={Period}", operationName, targetName, period);
			_db.AbortTransaction();
			throw;
		}
	}
	private string CreateSummaryStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlg, long vdate, string whereClause) => $@"
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
FROM {tableName} AS t
     CROSS JOIN json_each(t.Jmeisai) AS j
WHERE {whereClause}
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
	private string CreateRealStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlg, long vdate, string whereClause) => $@"
INSERT INTO SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu)
SELECT
  t.{idSoko} AS Id_Soko,
  json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
  json_extract(j.value, '$.Id_Col')    AS Id_Col,
  json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlg.Item1})   AS Su,
  {vdate} vdc,
  {vdate} vdu
FROM {tableName} AS t
     CROSS JOIN json_each(t.Jmeisai) AS j
WHERE {whereClause}
GROUP BY
  t.{idSoko},
  Id_Shohin,
  Id_Col,
  Id_Siz
ON CONFLICT(Id_Soko, Id_Shohin, Id_Col, Id_Siz) DO UPDATE
SET Su = Su + excluded.Su, vdu = {vdate}
;
";
	/// <summary>
	/// SummaryStockの年月までのデータを集計してSummaryRealStockに更新する(再作成)
	/// </summary>
	/// <param name="DateYyyymm"></param>
	/// <returns></returns>
	public int CalcSummaryRealStock(string DateYyyymm) {
		// DateTime.Now.ToDtStrDate2().Substring(0, 6)
		var cnt = 0;
		const string deleteSql = "DELETE FROM SummaryRealStock";
		var vdate = Common.GetVdate();
		var sql = @$"
Insert Into SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu)
SELECT
  Id_Soko,
  Id_Shohin,
  Id_Col,
  Id_Siz,
  SUM(Su) AS TotalSu,
  {vdate} AS Vdc,
  {vdate} AS Vdu
FROM SummaryStock
WHERE SumMonth <= @0
GROUP BY
  Id_Soko,
  Id_Shohin,
  Id_Col,
  Id_Siz;
";
		cnt += ExecuteInTransaction($"{deleteSql}\n{sql}", [DateYyyymm], "CalcSummaryRealStock", "SummaryRealStock", DateYyyymm);
		return cnt;
	}
	/// <summary>
	/// SummaryStockの年月までのデータを集計してSummaryStockのCumulativeSuに更新する(更新)
	/// Pending: SummaryRealStockがあるので、不要と思われる
	/// </summary>
	/// <param name="DateYyyymm"></param>
	/// <returns></returns>
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
		cnt += ExecuteInTransaction(sql, [DateYyyymm], "CalcSummaryStockCumulative", "SummaryStock:CumulativeSu", DateYyyymm);
		return cnt;
	}

	public IAsyncEnumerable<StreamStepProgress> SummaryRealAsyncStream(SummaryRealDateParameter param) {
		(string Name, Func<string, int> Action)[] steps = [
			/*
			*/
			("Summary : CalcSummaryRealStock", CalcSummaryRealStock),
		];

		return StreamStepProgressRunner.Run(
			steps,
			param.DateYymm,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了");
	}



}


