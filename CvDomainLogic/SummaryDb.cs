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
	public IAsyncEnumerable<StreamStepProgress> SummaryAllAsyncStream(CalcDateTermParameter param) {
		(string Name, Func<CalcDateTermParameter, int> Action)[] steps = [
			/*
			*/
			($"Summary : {nameof(Tran00Uriage)}", CalcSummaryStockTrn<Tran00Uriage>),
			($"Summary : {nameof(Tran01Tenuri)}", CalcSummaryStockTrn<Tran01Tenuri>),
			($"Summary : {nameof(Tran03Shiire)}", CalcSummaryStockTrn<Tran03Shiire>),
			($"Summary : {nameof(Tran05Ido)}", CalcSummaryStockTrn<Tran05Ido>),
			($"Summary : {nameof(Tran10IdoOut)}", CalcSummaryStockTrn<Tran10IdoOut>),
			($"Summary : {nameof(Tran11IdoIn)}", CalcSummaryStockTrn<Tran11IdoIn>)
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
	private int CalcSummaryStockTrn<T>(CalcDateTermParameter param) where T : ITranDetail {
		var cnt = 0;
		var tableName = typeof(T).Name;
		var calcFlag = TranCalcBase.GetCalcSoko(tableName);
		var sql = CreateSummaryStockSql(tableName, "Id_Soko", calcFlag, Common.GetVdate(), "t.DenDay BETWEEN @0 AND @1");
		var period = $"{param.DateYymmFrom}-{param.DateYymmTo}";
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		if (calcFlag.Item1 != 0 || calcFlag.Item2 != 0 || calcFlag.Item3 != 0 || calcFlag.Item4 != 0) {
			cnt += ExecuteAndCounts(sql, [param.DateYymmFrom, param.DateYymmTo + "99"], "CalcSummaryStock", $"{tableName}:Id_Soko", period);
		}
		if (typeof(ITranIdo).IsAssignableFrom(typeof(T))) {
			var calcFlag2 = TranCalcBase.GetCalcIdosaki(tableName);
			if (calcFlag2.Item1 != 0 || calcFlag2.Item2 != 0 || calcFlag2.Item3 != 0 || calcFlag2.Item4 != 0) {
				sql = CreateSummaryStockSql(tableName, "Id_Ido", calcFlag2, Common.GetVdate(), "t.DenDay BETWEEN @0 AND @1");
				cnt += ExecuteAndCounts(sql, [param.DateYymmFrom, param.DateYymmTo + "99"], "CalcSummaryStock", $"{tableName}:Id_Ido", period);
			}
		}
		_db.CompleteTransaction();
		return cnt;
	}
	/// <summary>
	/// Id指定でTranテーブルからSummaryStockおよびSummaryRealStockを更新する(レコード CUD)
	/// </summary>
	/// <param name="id"></param>
	/// <param name="invertFlag">在庫計算のフラグを反転させるかどうか</param>
	/// <returns></returns>
	public int CalcTran2SummaryStock(string tableName, string idSoko, long id, bool invertFlag) {
		var cnt = 0;
		var calcFlag = TranCalcBase.GetCalcSoko(tableName, invertFlag);
		var sql = CreateRealStockSql(tableName, idSoko, calcFlag, Common.GetVdate(), "t.Id=@0");
		if (calcFlag.Item1 != 0) {
			cnt += ExecuteAndCounts(sql, [id], "CalcTran2SummaryStock", $"{tableName}:Id_Soko", $"Id={id}");
			if (calcFlag.Item1 != 0 || calcFlag.Item2 != 0 || calcFlag.Item3 != 0 || calcFlag.Item4 != 0) {
				sql = CreateSummaryStockSql(tableName, idSoko, calcFlag, Common.GetVdate(), "t.Id=@0");
				cnt += ExecuteAndCounts(sql, [id], "CalcTran2SummaryStock", $"{tableName}:Id_Soko", $"Id={id}");
			}
		}
		return cnt;
	}
	private int ExecuteAndCounts(string sql, object[] args, string operationName, string targetName, string period) {
		var updatedCount = _db.Execute(sql, args);
		// var updatedCount = _db.FirstOrDefault<int>("SELECT changes() AS updated_count");
		return updatedCount;
	}
	private string CreateSummaryStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlag, long vdate, string whereClause) => $@"
INSERT INTO SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu, InQty, OutQty, TransitQty)
SELECT
  substr(t.DenDay, 1, 6) AS SumMonth,
  t.{idSoko} AS Id_Soko,
  json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
  json_extract(j.value, '$.Id_Col')    AS Id_Col,
  json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item1})   AS Su,
  {vdate} vdc,
  {vdate} vdu,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item2})   AS InQty,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item3})   AS OutQty,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item4})   AS TransitQty
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
	private string CreateRealStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlag, long vdate, string whereClause) => $@"
INSERT INTO SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu)
SELECT
  t.{idSoko} AS Id_Soko,
  json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
  json_extract(j.value, '$.Id_Col')    AS Id_Col,
  json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item1})   AS Su,
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
		cnt += ExecuteAndCounts($"{deleteSql}\n{sql}", [DateYyyymm], "CalcSummaryRealStock", "SummaryRealStock", DateYyyymm);
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
		cnt += ExecuteAndCounts(sql, [DateYyyymm], "CalcSummaryStockCumulative", "SummaryStock:CumulativeSu", DateYyyymm);
		return cnt;
	}

	public IAsyncEnumerable<StreamStepProgress> SummaryRealAsyncStream(CalcDateParameter param) {
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
	public IAsyncEnumerable<StreamStepProgress> SummaryUriKakeAsyncStream(CalcDateTermParameter param) {
		(string Name, Func<CalcDateTermParameter, int> Action)[] steps = [
			("Summary : CalcSummaryUriKake", p => CalcSummaryUriKake(p.DateYymmFrom, p.DateYymmTo)),
		];

		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了");
	}
	public IAsyncEnumerable<StreamStepProgress> SummaryKaiKakeAsyncStream(CalcDateTermParameter param) {
		(string Name, Func<CalcDateTermParameter, int> Action)[] steps = [
			("Summary : CalcSummaryKaiKake", p => CalcSummaryKaiKake(p.DateYymmFrom, p.DateYymmTo)),
		];

		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了");
	}
	/// <summary>
	/// SummaryKakeの年月のデータを集計する(再作成)
	/// </summary>
	/// <param name="DatefromYyyymm"></param>
	/// <param name="DateToYyyymm"></param>
	/// <returns></returns>
	public int CalcSummaryUriKake(string DatefromYyyymm, string DateToYyyymm) {
		var cnt = 0;
		const string deleteSql = "DELETE FROM SummaryUriKake WHERE DenMonth BETWEEN @0 AND @1";
		var vdate = Common.GetVdate();
		var sql = @$"
WITH movements AS (
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Tokui,
		SUM((CASE WHEN t.Total <> 0 THEN t.Total ELSE t.KingakuTotal + t.Tax END) * t.CalcFlag) AS TotalSales,
		SUM(t.KingakuTotal * t.CalcFlag) AS Uriage,
		SUM(t.Tax * t.CalcFlag) AS Tax,
		0 AS TotalIn
	FROM Tran00Uriage AS t
	WHERE substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Tokui
	UNION ALL
	SELECT
		substr(t.DenDay, 1, 6) AS DenMonth,
		t.Id_Torisaki AS Id_Tokui,
		0 AS TotalSales,
		0 AS Uriage,
		0 AS Tax,
		SUM(t.KingakuTotal) AS TotalIn
	FROM Tran06Nyukin AS t
	WHERE substr(t.DenDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Torisaki
),
monthly AS (
	SELECT
		DenMonth,
		Id_Tokui,
		SUM(TotalSales) AS TotalSales,
		SUM(Uriage) AS Uriage,
		SUM(Tax) AS Tax,
		SUM(TotalIn) AS TotalIn
	FROM movements
	GROUP BY DenMonth, Id_Tokui
),
previousBalance AS (
	SELECT s.Id_Tokui, s.Balance
	FROM SummaryUriKake AS s
	WHERE s.DenMonth = (
		SELECT MAX(p.DenMonth)
		FROM SummaryUriKake AS p
		WHERE p.Id_Tokui = s.Id_Tokui
		  AND p.DenMonth < @0
	)
)
INSERT INTO SummaryUriKake (
	Id_Tokui, DenMonth, Balance, TotalIn, TotalSales, Uriage, Henpin, Nebiki, Tax,
	Cash, Fee, Densai, Offset, Other, Vdc, Vdu
)
SELECT
	m.Id_Tokui,
	m.DenMonth,
	IFNULL(p.Balance, 0) + SUM(m.TotalSales - m.TotalIn) OVER (
		PARTITION BY m.Id_Tokui
		ORDER BY m.DenMonth
		ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
	) AS Balance,
	m.TotalIn,
	m.TotalSales,
	m.Uriage,
	0 AS Henpin,
	0 AS Nebiki,
	m.Tax,
	0 AS Cash,
	0 AS Fee,
	0 AS Densai,
	0 AS Offset,
	0 AS Other,
	{vdate} AS Vdc,
	{vdate} AS Vdu
FROM monthly AS m
LEFT JOIN previousBalance AS p ON p.Id_Tokui = m.Id_Tokui;
";
	var period = $"{DatefromYyyymm}-{DateToYyyymm}";
	_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
	cnt += ExecuteAndCounts(deleteSql, [DatefromYyyymm, DateToYyyymm], "CalcSummaryUriKake(delete)", "SummaryUriKake", period);
	cnt += ExecuteAndCounts(sql, [DatefromYyyymm, DateToYyyymm], "CalcSummaryUriKake", "SummaryUriKake", period);
	_db.CompleteTransaction();
		return cnt;
	}
	/// <summary>
	/// SummaryKaiKakeの年月のデータを集計する(再作成)
	/// </summary>
	/// <param name="DatefromYyyymm"></param>
	/// <param name="DateToYyyymm"></param>
	/// <returns></returns>
	public int CalcSummaryKaiKake(string DatefromYyyymm, string DateToYyyymm) {
		var cnt = 0;
		const string deleteSql = "DELETE FROM SummaryKaiKake WHERE DenMonth BETWEEN @0 AND @1";
		var vdate = Common.GetVdate();
		var sql = @$"
WITH movements AS (
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Shiire,
		SUM((CASE WHEN t.Total <> 0 THEN t.Total ELSE t.KingakuTotal + t.Tax END) * t.CalcFlag) AS TotalShiire,
		SUM(t.KingakuTotal * t.CalcFlag) AS Shiire,
		SUM(t.Tax * t.CalcFlag) AS Tax,
		0 AS TotalOut
	FROM Tran03Shiire AS t
	WHERE substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Shiire
	UNION ALL
	SELECT
		substr(t.DenDay, 1, 6) AS DenMonth,
		t.Id_Torisaki AS Id_Shiire,
		0 AS TotalShiire,
		0 AS Shiire,
		0 AS Tax,
		SUM(t.KingakuTotal) AS TotalOut
	FROM Tran07Shiharai AS t
	WHERE substr(t.DenDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Torisaki
),
monthly AS (
	SELECT
		DenMonth,
		Id_Shiire,
		SUM(TotalShiire) AS TotalShiire,
		SUM(Shiire) AS Shiire,
		SUM(Tax) AS Tax,
		SUM(TotalOut) AS TotalOut
	FROM movements
	GROUP BY DenMonth, Id_Shiire
),
previousBalance AS (
	SELECT s.Id_Shiire, s.Balance
	FROM SummaryKaiKake AS s
	WHERE s.DenMonth = (
		SELECT MAX(p.DenMonth)
		FROM SummaryKaiKake AS p
		WHERE p.Id_Shiire = s.Id_Shiire
		  AND p.DenMonth < @0
	)
)
INSERT INTO SummaryKaiKake (
	Id_Shiire, DenMonth, Balance, TotalOut, TotalShiire, Shiire, Henpin, Nebiki, Tax,
	Cash, Fee, Densai, Offset, Other, Vdc, Vdu
)
SELECT
	m.Id_Shiire,
	m.DenMonth,
	IFNULL(p.Balance, 0) + SUM(m.TotalShiire - m.TotalOut) OVER (
		PARTITION BY m.Id_Shiire
		ORDER BY m.DenMonth
		ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
	) AS Balance,
	m.TotalOut,
	m.TotalShiire,
	m.Shiire,
	0 AS Henpin,
	0 AS Nebiki,
	m.Tax,
	0 AS Cash,
	0 AS Fee,
	0 AS Densai,
	0 AS Offset,
	0 AS Other,
	{vdate} AS Vdc,
	{vdate} AS Vdu
FROM monthly AS m
LEFT JOIN previousBalance AS p ON p.Id_Shiire = m.Id_Shiire;
";
		var period = $"{DatefromYyyymm}-{DateToYyyymm}";
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		cnt += ExecuteAndCounts(deleteSql, [DatefromYyyymm, DateToYyyymm], "CalcSummaryKaiKake(delete)", "SummaryKaiKake", period);
		cnt += ExecuteAndCounts(sql, [DatefromYyyymm, DateToYyyymm], "CalcSummaryKaiKake", "SummaryKaiKake", period);
		_db.CompleteTransaction();
		return cnt;
	}



}


