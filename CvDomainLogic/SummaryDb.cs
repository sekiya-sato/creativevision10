using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CvDomainLogic;

/// <summary>
/// 引当数(<see cref="SummaryRealStock.ReserveQty"/>)の集計キー。
/// <para>
/// <see cref="SummaryStock"/> は <see cref="SumMonth"/> まで、<see cref="SummaryRealStock"/> は倉庫+SKU で集計する。
/// </para>
/// </summary>
public readonly record struct ReserveKey(string SumMonth, long Id_Soko, long Id_Shohin, long Id_Col, long Id_Siz) {
	/// <summary>配分行から集計キーを作る。DenDay(yyyyMMdd)の上6桁が SumMonth になる</summary>
	public static ReserveKey From(ITranReserve row) => new(
		row.DenDay.Length >= 6 ? row.DenDay[..6] : row.DenDay,
		row.Id_Soko, row.Id_Shohin, row.Id_Col, row.Id_Siz);
}

public class SummaryDb {
	ExDatabase _db;
	ILogger<SummaryDb> _logger;
	public SummaryDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<SummaryDb>();
	}
	public IAsyncEnumerable<StreamStepProgress> SummaryAllAsyncStream(CalcDateTermParameter param) {
		(string Name, Func<CalcDateTermParameter, int> Action)[] steps = [
			("Summary : SummaryStock", CalcSummaryStockRange)
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
	/// 指定年月範囲のSummaryStockとSummaryRealStockをTranテーブルから再作成する
	/// </summary>
	/// <param name="param"></param>
	/// <returns></returns>
	private int CalcSummaryStockRange(CalcDateTermParameter param) {
		const string tempTableName = "TempSummaryStockRebuildKeys";
		var cnt = 0;
		var period = $"{param.DateYymmFrom}-{param.DateYymmTo}";
		var transactionStarted = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			transactionStarted = true;
			var prepareSql = $@"
CREATE TEMP TABLE IF NOT EXISTS {tempTableName} (
  SumMonth TEXT NOT NULL,
  Id_Soko INTEGER NOT NULL,
  Id_Shohin INTEGER NOT NULL,
  Id_Col INTEGER NOT NULL,
  Id_Siz INTEGER NOT NULL,
  CumulativeSu INTEGER NOT NULL,
  AdjustQty INTEGER NOT NULL,
  StocktakeDdate TEXT,
  ActualQty INTEGER NOT NULL,
  PRIMARY KEY (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)
);
DELETE FROM {tempTableName};
INSERT INTO {tempTableName} (
  SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz,
  CumulativeSu, AdjustQty, StocktakeDdate, ActualQty
)
SELECT
  SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz,
  CumulativeSu, AdjustQty, StocktakeDdate, ActualQty
FROM SummaryStock
WHERE SumMonth BETWEEN @0 AND @1;
DELETE FROM SummaryStock
WHERE SumMonth BETWEEN @0 AND @1;
";
			cnt += ExecuteAndCounts(prepareSql, [param.DateYymmFrom, param.DateYymmTo], "CalcSummaryStockRange(delete)", "SummaryStock", period);
			cnt += CalcSummaryStockTrn<Tran00Uriage>(param);
			cnt += CalcSummaryStockTrn<Tran01Tenuri>(param);
			cnt += CalcSummaryStockTrn<Tran03Shiire>(param);
			cnt += CalcSummaryStockTrn<Tran05Ido>(param);
			cnt += CalcSummaryStockTrn<Tran10IdoOut>(param);
			cnt += CalcSummaryStockTrn<Tran11IdoIn>(param);
			// 在庫調整は Tran61Chosei から再現する。集計テーブルへ直接書かず伝票にしているのは
			// 「通常更新値 = Rebuild値」を保つため(仕様 8.4 F0/F2)
			cnt += CalcSummaryStockTrn<Tran61Chosei>(param);
			// AdjustQty は Tran61Chosei から再計算されるので復元対象から外す。
			// CumulativeSu / StocktakeDdate / ActualQty は伝票から導出できない非Tran列なので復元する
			var restoreSql = $@"
UPDATE SummaryStock
SET (CumulativeSu, StocktakeDdate, ActualQty) = (
  SELECT Old.CumulativeSu, Old.StocktakeDdate, Old.ActualQty
  FROM {tempTableName} AS Old
  WHERE Old.SumMonth = SummaryStock.SumMonth
    AND Old.Id_Soko = SummaryStock.Id_Soko
    AND Old.Id_Shohin = SummaryStock.Id_Shohin
    AND Old.Id_Col = SummaryStock.Id_Col
    AND Old.Id_Siz = SummaryStock.Id_Siz
)
WHERE SumMonth BETWEEN @0 AND @1
  AND EXISTS (
    SELECT 1
    FROM {tempTableName} AS Old
    WHERE Old.SumMonth = SummaryStock.SumMonth
      AND Old.Id_Soko = SummaryStock.Id_Soko
      AND Old.Id_Shohin = SummaryStock.Id_Shohin
      AND Old.Id_Col = SummaryStock.Id_Col
      AND Old.Id_Siz = SummaryStock.Id_Siz
  );
";
			cnt += ExecuteAndCounts(restoreSql, [param.DateYymmFrom, param.DateYymmTo], "CalcSummaryStockRange(restore)", "SummaryStock", period);
			cnt += CalcSummaryRealStockRangeCore(param.DateYymmFrom, param.DateYymmTo, tempTableName);
			// 引当数はDELETE→再INSERTで失われるので、再作成の最後にTranHaibunから引き直す
			cnt += CalcReserveQtyAll();
			_db.CompleteTransaction();
			transactionStarted = false;
			return cnt;
		}
		catch {
			if (transactionStarted) {
				_db.AbortTransaction();
			}
			throw;
		}
		finally {
			try {
				_db.Execute($"DROP TABLE IF EXISTS {tempTableName}");
			}
			catch (Exception ex) {
				_logger.LogWarning(ex, "一時テーブルの削除に失敗しました: {TableName}", tempTableName);
			}
		}
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
		var calcFlag = (idSoko == "Id_Ido") ? TranCalcBase.GetCalcIdosaki(tableName, invertFlag) : TranCalcBase.GetCalcSoko(tableName, invertFlag);
		if (calcFlag.Item1 != 0) {
			var sql = CreateRealStockSql(tableName, idSoko, calcFlag, Common.GetVdate(), "t.Id=@0");
			cnt += ExecuteAndCounts(sql, [id], "CalcTran2SummaryStock", $"{tableName}:{idSoko}", $"Id={id}");
		}
		if (calcFlag.Item1 != 0 || calcFlag.Item2 != 0 || calcFlag.Item3 != 0 || calcFlag.Item4 != 0) {
			var sql = CreateSummaryStockSql(tableName, idSoko, calcFlag, Common.GetVdate(), "t.Id=@0", invertFlag);
			cnt += ExecuteAndCounts(sql, [id], "CalcTran2SummaryStock", $"{tableName}:{idSoko}", $"Id={id}");
		}
		return cnt;
	}
	/// <summary>
	/// 配分(<see cref="TranHaibun"/>)の追加・修正・削除に伴い、対象キーの引当数を <see cref="TranHaibun"/> から引き直す。
	/// <para>
	/// 差分の加減算をせずキー単位で引き直すため、修正時は修正前と修正後のキーを両方渡せば、
	/// 呼び出し順やDB更新との前後関係に関係なく正しい値になる（<c>CalcReserveQtyAll()</c> の結果と必ず一致する）。
	/// 引当数は <see cref="TranHaibun"/> だけが源泉なので、Tran系伝票の在庫計算とは独立している。
	/// 呼び出し元(<c>HandlerClass</c>)が張ったトランザクション内で実行される前提。
	/// </para>
	/// </summary>
	/// <param name="keys">再計算する集計キー。重複は内部で除去する</param>
	public int CalcHaibun2Reserve(params ReserveKey[] keys) => CalcHaibun2Reserve((IEnumerable<ReserveKey>)keys);
	/// <inheritdoc cref="CalcHaibun2Reserve(ReserveKey[])"/>
	public int CalcHaibun2Reserve(IEnumerable<ReserveKey> keys) {
		var cnt = 0;
		var vdate = Common.GetVdate();
		var monthKeys = keys.Distinct().ToList();
		if (monthKeys.Count == 0) {
			return cnt;
		}
		foreach (var key in monthKeys) {
			var sql = CreateReserveMonthSql(vdate,
				"SumMonth = @0 AND Id_Soko = @1 AND Id_Shohin = @2 AND Id_Col = @3 AND Id_Siz = @4 AND ReserveQty <> 0",
				$"{ReserveTargetWhere} AND substr(h.DenDay, 1, 6) = @0 AND h.Id_Soko = @1 AND h.Id_Shohin = @2 AND h.Id_Col = @3 AND h.Id_Siz = @4");
			cnt += ExecuteAndCounts(sql, [key.SumMonth, key.Id_Soko, key.Id_Shohin, key.Id_Col, key.Id_Siz],
				"CalcHaibun2Reserve", "SummaryStock:ReserveQty", key.SumMonth);
		}
		// SummaryRealStockは年月を持たないので、同一の倉庫+SKUを複数月ぶん重複実行しない
		foreach (var key in monthKeys.Select(k => (k.Id_Soko, k.Id_Shohin, k.Id_Col, k.Id_Siz)).Distinct()) {
			var sql = CreateReserveRealSql(vdate,
				"Id_Soko = @0 AND Id_Shohin = @1 AND Id_Col = @2 AND Id_Siz = @3 AND ReserveQty <> 0",
				$"{ReserveTargetWhere} AND h.Id_Soko = @0 AND h.Id_Shohin = @1 AND h.Id_Col = @2 AND h.Id_Siz = @3");
			cnt += ExecuteAndCounts(sql, [key.Id_Soko, key.Id_Shohin, key.Id_Col, key.Id_Siz],
				"CalcHaibun2Reserve", "SummaryRealStock:ReserveQty", "");
		}
		return cnt;
	}
	/// <summary>
	/// <see cref="SummaryStock"/> と <see cref="SummaryRealStock"/> の引当数を <see cref="TranHaibun"/> から全面的に作り直す。
	/// <para>
	/// Rebuild経路は「DELETE → 再INSERT」で引当数を失うため、再作成の最後に必ず呼ぶ。
	/// 引当数は年月範囲に依存しない(<see cref="TranHaibun"/> だけが源泉)ので、範囲指定ではなく常に全件を対象にする。
	/// </para>
	/// </summary>
	public int CalcReserveQtyAll() {
		var vdate = Common.GetVdate();
		var cnt = ExecuteAndCounts(CreateReserveMonthSql(vdate, "ReserveQty <> 0", ReserveTargetWhere),
			[], "CalcReserveQtyAll", "SummaryStock:ReserveQty", "");
		cnt += ExecuteAndCounts(CreateReserveRealSql(vdate, "ReserveQty <> 0", ReserveTargetWhere),
			[], "CalcReserveQtyAll", "SummaryRealStock:ReserveQty", "");
		return cnt;
	}
	/// <summary>
	/// 引当対象行の共通条件。<see cref="TranHaibun.EndFlag"/>=0（未入庫）かつ
	/// <see cref="TranHaibun.Kubun"/> が <see cref="EnumHaibun.Hatsukai"/>(0) 以外であること。
	/// <para>
	/// 初回配分は入荷前に入荷予定を店舗へ振り分けるものであり、現物在庫を押さえないため引当対象外とする。
	/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2.2 を参照する。
	/// </para>
	/// </summary>
	public static readonly string ReserveTargetWhere = $"h.EndFlag = 0 AND h.Kubun <> {(int)EnumHaibun.Hatsukai}";
	/// <summary>
	/// 引当数として積む数量の式。未確定(<see cref="TranHaibun.KakuteiDay"/> が空)は指示数
	/// <see cref="TranHaibun.Su"/>、確定済みは確定数 <see cref="TranHaibun.JitsuSu"/> を積む。
	/// <para>
	/// 確定時に倉庫から <see cref="TranHaibun.JitsuSu"/> / <see cref="TranHaibun.ShortSu"/> が返り
	/// <c>Su = JitsuSu + ShortSu</c> が成立する。欠品分は出荷されないため、確定と同時に引当から外れる。
	/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2.2c を参照する。
	/// </para>
	/// </summary>
	public const string ReserveQtySumExpr = "SUM(CASE WHEN ifnull(h.KakuteiDay, '') = '' THEN h.Su ELSE h.JitsuSu END)";
	/// <summary>
	/// SummaryStock(月次)の引当数を TranHaibun から引き直すSQL。「対象を0クリア」→「集計値を反映」の2文。
	/// 引当が0になったキーに0行を作らないよう HAVING で除外し、在庫実績が無いキーはINSERTで新規作成する。
	/// </summary>
	private static string CreateReserveMonthSql(long vdate, string clearWhere, string haibunWhere) => $@"
UPDATE SummaryStock
SET ReserveQty = 0, Vdu = {vdate}
WHERE {clearWhere};

INSERT INTO SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu, ReserveQty)
SELECT
  substr(h.DenDay, 1, 6) AS SumMonth,
  h.Id_Soko,
  h.Id_Shohin,
  h.Id_Col,
  h.Id_Siz,
  0 AS Su,
  {vdate} AS Vdc,
  {vdate} AS Vdu,
  {ReserveQtySumExpr} AS ReserveQty
FROM {nameof(TranHaibun)} AS h
WHERE {haibunWhere}
GROUP BY
  SumMonth,
  h.Id_Soko,
  h.Id_Shohin,
  h.Id_Col,
  h.Id_Siz
HAVING {ReserveQtySumExpr} <> 0
ON CONFLICT(SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz) DO UPDATE
SET ReserveQty = excluded.ReserveQty, Vdu = {vdate}
;
";
	/// <summary>
	/// SummaryRealStock(現在庫)の引当数を TranHaibun から引き直すSQL。
	/// SummaryStockの月次合計ではなく TranHaibun から直接引くのは、SummaryRealStockの再作成が
	/// 「SumMonth &lt;= 対象年月」で打ち切るため、未来日付の配分指示が引当から漏れるのを避けるため。
	/// </summary>
	private static string CreateReserveRealSql(long vdate, string clearWhere, string haibunWhere) => $@"
UPDATE SummaryRealStock
SET ReserveQty = 0, Vdu = {vdate}
WHERE {clearWhere};

INSERT INTO SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu, ReserveQty)
SELECT
  h.Id_Soko,
  h.Id_Shohin,
  h.Id_Col,
  h.Id_Siz,
  0 AS Su,
  {vdate} AS Vdc,
  {vdate} AS Vdu,
  {ReserveQtySumExpr} AS ReserveQty
FROM {nameof(TranHaibun)} AS h
WHERE {haibunWhere}
GROUP BY
  h.Id_Soko,
  h.Id_Shohin,
  h.Id_Col,
  h.Id_Siz
HAVING {ReserveQtySumExpr} <> 0
ON CONFLICT(Id_Soko, Id_Shohin, Id_Col, Id_Siz) DO UPDATE
SET ReserveQty = excluded.ReserveQty, Vdu = {vdate}
;
";
	private int ExecuteAndCounts(string sql, object[] args, string operationName, string targetName, string period) {
		var updatedCount = _db.ExecuteDialect(sql, args);
		// var updatedCount = _db.FirstOrDefault<int>("SELECT changes() AS updated_count");
		return updatedCount;
	}
	private string CreateSummaryStockSql(string tableName, string idSoko, Tuple<int, int, int, int> calcFlag, long vdate, string whereClause, bool invertFlag = false) {
		// 調整数は在庫調整伝票(Tran61Chosei)だけが積む。移動先軸(Id_Ido)では調整は発生しない
		var adjustFlag = idSoko == nameof(ITranIdo.Id_Ido) ? 0 : TranCalcBase.GetCalcAdjust(tableName, invertFlag);
		return $@"
INSERT INTO SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu, InQty, OutQty, TransitQty, AdjustQty)
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
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{calcFlag.Item4})   AS TransitQty,
  SUM(json_extract(j.value, '$.Su')*t.CalcFlag*{adjustFlag})       AS AdjustQty
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
    TransitQty = TransitQty + excluded.TransitQty,
    AdjustQty = AdjustQty + excluded.AdjustQty
;
";
	}
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
		// 後続のInsertと1つのコマンドで実行するので、文の区切り(;)が必須
		const string deleteSql = "DELETE FROM SummaryRealStock;";
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
		// 引当数はDELETE→再INSERTで失われるので、再作成の最後にTranHaibunから引き直す
		cnt += CalcReserveQtyAll();
		return cnt;
	}
	/// <summary>
	/// 指定年月範囲に存在する倉庫・商品・色・サイズのSummaryStockを基に、該当するSummaryRealStockを再作成する
	/// </summary>
	/// <param name="DateFromYyyymm">対象開始年月</param>
	/// <param name="DateToYyyymm">対象終了年月</param>
	/// <returns></returns>
	public int CalcSummaryRealStockRange(string DateFromYyyymm, string DateToYyyymm) {
		var transactionStarted = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			transactionStarted = true;
			var cnt = CalcSummaryRealStockRangeCore(DateFromYyyymm, DateToYyyymm);
			// 引当数はDELETE→再INSERTで失われるので、再作成の最後にTranHaibunから引き直す
			cnt += CalcReserveQtyAll();
			_db.CompleteTransaction();
			transactionStarted = false;
			return cnt;
		}
		catch {
			if (transactionStarted) {
				_db.AbortTransaction();
			}
			throw;
		}
	}
	private int CalcSummaryRealStockRangeCore(string DateFromYyyymm, string DateToYyyymm, string? previousTargetKeysTable = null) {
		var vdate = Common.GetVdate();
		var period = $"{DateFromYyyymm}-{DateToYyyymm}";
		var targetKeysSql = previousTargetKeysTable == null
			? @"SELECT DISTINCT Id_Soko, Id_Shohin, Id_Col, Id_Siz
  FROM SummaryStock
  WHERE SumMonth BETWEEN @0 AND @1"
			: $@"SELECT Id_Soko, Id_Shohin, Id_Col, Id_Siz
  FROM {previousTargetKeysTable}
UNION
SELECT DISTINCT Id_Soko, Id_Shohin, Id_Col, Id_Siz
  FROM SummaryStock
  WHERE SumMonth BETWEEN @0 AND @1";
		var sql = @$"
DELETE FROM SummaryRealStock
WHERE EXISTS (
  SELECT 1
  FROM ({targetKeysSql}) AS Target
  WHERE Target.Id_Soko = SummaryRealStock.Id_Soko
    AND Target.Id_Shohin = SummaryRealStock.Id_Shohin
    AND Target.Id_Col = SummaryRealStock.Id_Col
    AND Target.Id_Siz = SummaryRealStock.Id_Siz
);

WITH TargetKeys AS (
  {targetKeysSql}
)
INSERT INTO SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, Vdc, Vdu)
SELECT
  s.Id_Soko,
  s.Id_Shohin,
  s.Id_Col,
  s.Id_Siz,
  SUM(s.Su) AS TotalSu,
  {vdate} AS Vdc,
  {vdate} AS Vdu
FROM SummaryStock AS s
INNER JOIN TargetKeys AS k
  ON k.Id_Soko = s.Id_Soko
 AND k.Id_Shohin = s.Id_Shohin
 AND k.Id_Col = s.Id_Col
 AND k.Id_Siz = s.Id_Siz
WHERE s.SumMonth <= @1
GROUP BY
  s.Id_Soko,
  s.Id_Shohin,
  s.Id_Col,
  s.Id_Siz;
";
		return ExecuteAndCounts(sql, [DateFromYyyymm, DateToYyyymm], "CalcSummaryRealStockRange", "SummaryRealStock", period);
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
	/// <summary>請求残をストリーミングで再作成する。</summary>
	public IAsyncEnumerable<StreamStepProgress> SummaryUriSeiAsyncStream(BillingParameter param) {
		(string Name, Func<BillingParameter, int> Action)[] steps = [
			("Summary : CalcSummaryUriSei", p => CalcSummaryUriSei(p.BillingYyyymm, p.Shime, p.TorisakiCodeFrom, p.TorisakiCodeTo, p.IsReissue)),
		];

		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了");
	}
	/// <summary>支払残をストリーミングで再作成する。</summary>
	public IAsyncEnumerable<StreamStepProgress> SummaryKaiShiAsyncStream(BillingParameter param) {
		(string Name, Func<BillingParameter, int> Action)[] steps = [
			("Summary : CalcSummaryKaiShi", p => CalcSummaryKaiShi(p.BillingYyyymm, p.Shime, p.TorisakiCodeFrom, p.TorisakiCodeTo)),
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
	/// 掛集計で伝票側に必ず付ける条件。掛計上しない伝票(<c>IsPay = 0</c>)は売掛・買掛へ入れない。
	/// <para>2026-08-16 決定。移行済みデータは売上50,311件・仕入25件とも <c>1</c> である。</para>
	/// </summary>
	const string KakeDenWhere = "t.IsPay = 1";

	/// <summary>
	/// 入金/支払明細(<see cref="TranKinMeisai"/>)を <c>json_each</c> で展開する FROM 断片。
	/// 不正JSONへ <c>json_extract</c> を当てると SQLite が例外を投げるため、空配列へ倒してから展開する。
	/// </summary>
	const string KinMeisaiFrom = "json_each(CASE WHEN json_valid(t.Jmeisai) THEN t.Jmeisai ELSE '[]' END) AS m";

	/// <summary>明細の金額。</summary>
	const string KinMeisaiKingaku = "CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER)";

	/// <summary>
	/// 入金/支払の区分別内訳を作る集計式。<c>KIN</c> 区分マスタの <c>Code</c> で振り分ける。
	/// <para>
	/// <c>01</c>現金 / <c>02</c>振込手数料 / <c>03</c>手形 / <c>04</c>相殺 のいずれにも当たらないものは
	/// 全て「その他」(<c>99</c>指定)へ寄せる。<c>05</c>その他だけでなく、有効JSON内でマスタに無い <c>Id_Kin</c> もここへ落ちるため、
	/// 内訳の合計は必ず <c>TotalIn</c> / <c>TotalOut</c> に一致する。
	/// </para>
	/// </summary>
	static string KinBucket(string code) =>
		code == "99"
			? $"SUM(CASE WHEN IFNULL(k.Code, '') NOT IN ('01','02','03','04') THEN {KinMeisaiKingaku} ELSE 0 END)"
			: $"SUM(CASE WHEN k.Code = '{code}' THEN {KinMeisaiKingaku} ELSE 0 END)";

	/// <summary>
	/// 対象期間より後の月も再計算へ含めるため、実効の終了月を求める。
	/// <para>
	/// 繰越(<c>Balance</c>)は前月から積み上がるので、過去月だけを再作成すると以降の月が古い繰越のまま残る。
	/// 指定 <paramref name="dateToYyyymm"/> より後に集計行または伝票が存在すれば、その最大月まで対象を伸ばす。
	/// 夜間ジョブは前月・当月しか回さないため通常は指定値のままで、画面から任意期間を指定したときだけ伸びる。
	/// </para>
	/// <para>
	/// 伝票側は <c>substr(KakeDay,1,6) &gt; @0</c> と同値の <c>KakeDay &gt; @0 || '99'</c> で書き、
	/// <c>KakeDay</c> のインデックスが効く形にしている。
	/// </para>
	/// </summary>
	string ExtendToMonth(string summaryTable, string denTable, string kinTable, string dateToYyyymm) {
		var sql = @$"
SELECT MAX(m) FROM (
	SELECT MAX(DenMonth) AS m FROM {summaryTable} WHERE DenMonth > @0
	UNION ALL
	SELECT MAX(substr(KakeDay, 1, 6)) FROM {denTable} WHERE KakeDay > @0 || '99'
	UNION ALL
	SELECT MAX(substr(KakeDay, 1, 6)) FROM {kinTable} WHERE KakeDay > @0 || '99'
)";
		var found = _db.FirstOrDefault<string>(sql, dateToYyyymm);
		if (string.IsNullOrEmpty(found) || string.CompareOrdinal(found, dateToYyyymm) <= 0) return dateToYyyymm;
		_logger.LogInformation(
			"{Table} の再計算範囲を繰越の整合のため {To} から {Extended} へ延長しました", summaryTable, dateToYyyymm, found);
		return found;
	}

	/// <summary>
	/// 期首年月日(yyyyMMdd)を <see cref="MasterSysman"/> から取得する。未設定時は "19010101"。
	/// <para>
	/// 期首売掛残・買掛残・請求残・支払残は外部CSV取込(ExternalCsvImportView)で Summary 各テーブルへ
	/// 期首前の年月(DenMonth/DenDay)を持つ行として取り込む。この期首年月より前の集計行は移行時の
	/// 期首残高として凍結し、売掛・買掛・請求・支払の再計算では削除・上書きしない。繰越(Balance/previousBalance)
	/// は凍結行を起点として自然に積み上がる。仕様は D-08(期首残高と移行)を参照する。
	/// </para>
	/// </summary>
	private string GetFiscalStartDate() {
		// MasterSysman 未作成(移行前・一部の単体テスト)では既定 "19010101" を返し、ガードを実質無効化する。
		var tableExists = _db.FirstOrDefault<string>(
			"SELECT name FROM sqlite_master WHERE type='table' AND name='MasterSysman'");
		if (string.IsNullOrEmpty(tableExists)) {
			return "19010101";
		}
		var value = _db.FirstOrDefault<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1");
		return string.IsNullOrWhiteSpace(value) ? "19010101" : value;
	}

	/// <summary>
	/// SummaryUriKakeの年月のデータを集計する(再作成)
	/// <para>
	/// 売上は区分(<see cref="EnumUri00"/>)で 売上 / 返品 / 値引 へ、入金は明細の <c>Id_Kin</c> で
	/// 現金 / 振込手数料 / 手形 / 相殺 / その他 へ振り分ける。売上・返品・値引は税抜 <c>Total</c> の正値内訳とし、
	/// 合計は内訳と税から算出する。区分99(その他売上)は売上へ畳み込む(請求残<see cref="SummaryUriSei"/>では
	/// Sonota列へ分離集計するため、こことは扱いが異なる)。
	/// </para>
	/// </summary>
	/// <param name="DatefromYyyymm"></param>
	/// <param name="DateToYyyymm"></param>
	/// <returns></returns>
	public int CalcSummaryUriKake(string DatefromYyyymm, string DateToYyyymm) {
		var cnt = 0;
		// 期首年月(MasterSysman.FiscalStartDate)以前は期首残高として凍結し再計算しない。
		var fiscalMonth = GetFiscalStartDate()[..6];
		if (string.CompareOrdinal(DateToYyyymm, fiscalMonth) < 0) return cnt;
		if (string.CompareOrdinal(DatefromYyyymm, fiscalMonth) < 0) DatefromYyyymm = fiscalMonth;
		const string deleteSql = "DELETE FROM SummaryUriKake WHERE DenMonth BETWEEN @0 AND @1";
		var vdate = Common.GetVdate();
		var toMonth = ExtendToMonth("SummaryUriKake", "Tran00Uriage", "Tran06Nyukin", DateToYyyymm);
		var sql = @$"
WITH kinmap AS (
	SELECT Id, Code FROM MasterMeisho WHERE Kubun = 'KIN'
),
movements AS (
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Tokui,
		SUM(CASE WHEN t.Kubun BETWEEN 10 AND 19 OR t.Kubun BETWEEN 40 AND 89 OR t.Kubun = 99 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Uriage,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Henpin,
		SUM(CASE WHEN t.Kubun BETWEEN 30 AND 39 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Nebiki,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN IFNULL(t.Tax, 0) * t.CalcFlag ELSE IFNULL(t.Tax, 0) END) AS Tax,
		0 AS Cash, 0 AS Fee, 0 AS Densai, 0 AS Offset, 0 AS Other
	FROM Tran00Uriage AS t
	WHERE {KakeDenWhere} AND substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Tokui
	UNION ALL
	-- 入金は有効JSONの明細だけを正値源とする。不正JSONは KinMeisaiFrom により空明細となる。
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Torisaki AS Id_Tokui,
		0, 0, 0, 0,
		{KinBucket("01")} AS Cash,
		{KinBucket("02")} AS Fee,
		{KinBucket("03")} AS Densai,
		{KinBucket("04")} AS Offset,
		{KinBucket("99")} AS Other
	FROM Tran06Nyukin AS t, {KinMeisaiFrom}
	LEFT JOIN kinmap AS k ON k.Id = json_extract(m.value, '$.Id_Kin')
	WHERE substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Torisaki
),
monthly AS (
	SELECT
		DenMonth,
		Id_Tokui,
		SUM(Uriage) AS Uriage,
		SUM(Henpin) AS Henpin,
		SUM(Nebiki) AS Nebiki,
		SUM(Tax) AS Tax,
		SUM(Cash) AS Cash,
		SUM(Fee) AS Fee,
		SUM(Densai) AS Densai,
		SUM(Offset) AS Offset,
		SUM(Other) AS Other
	FROM movements
	GROUP BY DenMonth, Id_Tokui
),
calculated AS (
	SELECT
		m.*,
		m.Uriage - m.Henpin - m.Nebiki + m.Tax AS TotalSales,
		m.Cash + m.Fee + m.Densai + m.Offset + m.Other AS TotalIn
	FROM monthly AS m
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
	c.Id_Tokui,
	c.DenMonth,
	IFNULL(p.Balance, 0) + SUM(c.TotalIn - c.TotalSales) OVER (
		PARTITION BY c.Id_Tokui
		ORDER BY c.DenMonth
		ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
	) AS Balance,
	c.TotalIn,
	c.TotalSales,
	c.Uriage,
	c.Henpin,
	c.Nebiki,
	c.Tax,
	c.Cash,
	c.Fee,
	c.Densai,
	c.Offset,
	c.Other,
	{vdate} AS Vdc,
	{vdate} AS Vdu
FROM calculated AS c
LEFT JOIN previousBalance AS p ON p.Id_Tokui = c.Id_Tokui;
";
		var period = $"{DatefromYyyymm}-{toMonth}";
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		cnt += ExecuteAndCounts(deleteSql, [DatefromYyyymm, toMonth], "CalcSummaryUriKake(delete)", "SummaryUriKake", period);
		cnt += ExecuteAndCounts(sql, [DatefromYyyymm, toMonth], "CalcSummaryUriKake", "SummaryUriKake", period);
		_db.CompleteTransaction();
		return cnt;
	}
	/// <summary>
	/// 指定締日・請求月・得意先コード範囲の請求残を再作成する。
	/// <para>通常再計算では請求書番号と連番を保持し、対象期間の伝票内訳だけを作り直す。</para>
	/// </summary>
	public int CalcSummaryUriSei(string billingYyyymm, int shime, string tokuiCodeFrom = "", string tokuiCodeTo = "", bool isReissue = false) {
		var (dayFrom, dayTo) = GetClosingPeriod(billingYyyymm, shime);
		// 期首日(MasterSysman.FiscalStartDate)以前は期首残高として凍結し再計算しない。
		// 締期間が期首前に終わるなら何もせず、期首をまたぐ場合は開始日を期首日まで切り上げる。
		var fiscalStart = GetFiscalStartDate();
		if (string.CompareOrdinal(dayTo, fiscalStart) < 0) return 0;
		if (string.CompareOrdinal(dayFrom, fiscalStart) < 0) dayFrom = fiscalStart;
		const string tempTableName = "TempSummaryUriSeiPrevious";
		var vdate = Common.GetVdate();
		var cnt = 0;
		var transactionStarted = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			transactionStarted = true;
			var prepareSql = $@"
CREATE TEMP TABLE IF NOT EXISTS {tempTableName} AS
SELECT Id_Tokui, DenDay, SeikyuNo, Renban
FROM SummaryUriSei
WHERE 1 = 0;
DELETE FROM {tempTableName};
INSERT INTO {tempTableName} (Id_Tokui, DenDay, SeikyuNo, Renban)
SELECT s.Id_Tokui, s.DenDay, s.SeikyuNo, s.Renban
FROM SummaryUriSei AS s
INNER JOIN MasterTokui AS t ON t.Id = s.Id_Tokui
WHERE s.DenDay = @1
  AND t.Shime1 = @2
  AND (@3 = '' OR t.Code >= @3)
  AND (@4 = '' OR t.Code <= @4);
DELETE FROM SummaryUriSei
WHERE DenDay = @1
  AND Id_Tokui IN (
		SELECT Id FROM MasterTokui
		WHERE Shime1 = @2
		  AND (@3 = '' OR Code >= @3)
		  AND (@4 = '' OR Code <= @4)
	);
";
			var sql = $@"
WITH kinmap AS (
	SELECT Id, Code FROM MasterMeisho WHERE Kubun = 'KIN'
),
sales AS (
	SELECT
		t.Id_Tokui,
		SUM(CASE WHEN t.Kubun BETWEEN 10 AND 19 OR t.Kubun BETWEEN 40 AND 89 THEN t.Total ELSE 0 END) AS Uriage,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN t.Total ELSE 0 END) AS Henpin,
		SUM(CASE WHEN t.Kubun BETWEEN 30 AND 39 THEN t.Total ELSE 0 END) AS Nebiki,
		SUM(CASE WHEN t.Kubun = 99 THEN t.Total ELSE 0 END) AS Sonota,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN t.Tax * t.CalcFlag ELSE t.Tax END) AS Tax
	FROM Tran00Uriage AS t
	WHERE {KakeDenWhere} AND t.KakeDay BETWEEN @0 AND @1
	GROUP BY t.Id_Tokui
),
payments AS (
	SELECT
		t.Id_Torisaki AS Id_Tokui,
		{KinBucket("01")} AS Cash,
		{KinBucket("02")} AS Fee,
		{KinBucket("03")} AS Densai,
		{KinBucket("04")} AS Offset,
		{KinBucket("99")} AS Other
	FROM Tran06Nyukin AS t, {KinMeisaiFrom}
	LEFT JOIN kinmap AS k ON k.Id = json_extract(m.value, '$.Id_Kin')
	WHERE t.KakeDay BETWEEN @0 AND @1
	GROUP BY t.Id_Torisaki
),
targets AS (
	SELECT Id, PayMonth, PayDay
	FROM MasterTokui
	WHERE Shime1 = @2
	  AND (@3 = '' OR Code >= @3)
	  AND (@4 = '' OR Code <= @4)
),
previousBalance AS (
	SELECT Id_Tokui, SUM(TotalIn - TotalSales) AS Balance
	FROM SummaryUriSei
	WHERE DayTo < @0
	GROUP BY Id_Tokui
),
calculated AS (
	SELECT
		t.Id AS Id_Tokui,
		IFNULL(s.Uriage, 0) AS Uriage,
		IFNULL(s.Henpin, 0) AS Henpin,
		IFNULL(s.Nebiki, 0) AS Nebiki,
		IFNULL(s.Sonota, 0) AS Sonota,
		IFNULL(s.Tax, 0) AS Tax,
		IFNULL(p.Cash, 0) AS Cash,
		IFNULL(p.Fee, 0) AS Fee,
		IFNULL(p.Densai, 0) AS Densai,
		IFNULL(p.Offset, 0) AS Offset,
		IFNULL(p.Other, 0) AS Other,
		strftime('%Y%m%d', CASE
			WHEN t.PayDay IN (0, 99) THEN date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth + 1), '-1 day')
			ELSE date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth), printf('+%d days', MIN(t.PayDay, CAST(strftime('%d', date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth + 1), '-1 day')) AS INTEGER)) - 1))
		END) AS NyukinYoteiDay
	FROM targets AS t
	LEFT JOIN sales AS s ON s.Id_Tokui = t.Id
	LEFT JOIN payments AS p ON p.Id_Tokui = t.Id
)
INSERT INTO SummaryUriSei (
	Id_Tokui, DenDay, DayFrom, DayTo, SeikyuNo, Renban, NyukinYoteiDay,
	Balance, TotalIn, TotalSales, Uriage, Henpin, Nebiki, Sonota, Tax,
	Cash, Fee, Densai, Offset, Other, Vdc, Vdu
)
SELECT
	c.Id_Tokui,
	@1,
	@0,
	@1,
	CASE WHEN @6 = 0 THEN COALESCE(NULLIF(o.SeikyuNo, ''), printf('%d-%s-%02d', c.Id_Tokui, @1, CASE WHEN IFNULL(o.Renban, 0) > 0 THEN o.Renban ELSE 1 END))
		ELSE printf('%d-%s-%02d', c.Id_Tokui, @1, CASE WHEN IFNULL(o.Renban, 0) > 0 THEN o.Renban + 1 ELSE 1 END) END,
	CASE WHEN IFNULL(o.Renban, 0) > 0 AND @6 <> 0 THEN o.Renban + 1 WHEN IFNULL(o.Renban, 0) > 0 THEN o.Renban ELSE 1 END,
	c.NyukinYoteiDay,
	IFNULL(b.Balance, 0) + (c.Cash + c.Fee + c.Densai + c.Offset + c.Other) - (c.Uriage - c.Henpin - c.Nebiki + c.Sonota + c.Tax),
	c.Cash + c.Fee + c.Densai + c.Offset + c.Other,
	c.Uriage - c.Henpin - c.Nebiki + c.Sonota + c.Tax,
	c.Uriage, c.Henpin, c.Nebiki, c.Sonota, c.Tax,
	c.Cash, c.Fee, c.Densai, c.Offset, c.Other,
	{vdate}, {vdate}
FROM calculated AS c
LEFT JOIN previousBalance AS b ON b.Id_Tokui = c.Id_Tokui
LEFT JOIN {tempTableName} AS o ON o.Id_Tokui = c.Id_Tokui AND o.DenDay = @1;
";
			var period = $"{dayFrom}-{dayTo},締日={shime},再発行={isReissue}";
			var parameters = new object[] { dayFrom, dayTo, shime, tokuiCodeFrom, tokuiCodeTo, billingYyyymm, isReissue ? 1 : 0 };
			cnt += ExecuteAndCounts(prepareSql, parameters, "CalcSummaryUriSei(delete)", "SummaryUriSei", period);
			cnt += ExecuteAndCounts(sql, parameters, "CalcSummaryUriSei", "SummaryUriSei", period);
			_db.CompleteTransaction();
			transactionStarted = false;
			return cnt;
		}
		catch {
			if (transactionStarted) {
				_db.AbortTransaction();
			}
			throw;
		}
		finally {
			try {
				_db.Execute($"DROP TABLE IF EXISTS {tempTableName}");
			}
			catch (Exception ex) {
				_logger.LogWarning(ex, "請求残計算用の一時テーブル削除に失敗しました: {TableName}", tempTableName);
			}
		}
	}

	private static (string DayFrom, string DayTo) GetClosingPeriod(string billingYyyymm, int shime) {
		if (!DateTime.TryParseExact(billingYyyymm, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var billingMonth)) {
			throw new ArgumentException("請求月はyyyyMM形式で指定してください。", nameof(billingYyyymm));
		}
		if (shime is < 1 or > 31 && shime != (int)EnumShime.DayLast) {
			throw new ArgumentOutOfRangeException(nameof(shime), "締日は1から31または99で指定してください。");
		}
		var dayTo = GetClosingDay(billingMonth, shime);
		var dayFrom = GetClosingDay(billingMonth.AddMonths(-1), shime).AddDays(1);
		return (dayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture), dayTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	private static DateTime GetClosingDay(DateTime month, int shime) {
		var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
		return new DateTime(month.Year, month.Month, shime == (int)EnumShime.DayLast ? lastDay : Math.Min(shime, lastDay));
	}
	/// <summary>
	/// 指定締日・支払月・仕入先コード範囲の支払残を再作成する。
	/// </summary>
	public int CalcSummaryKaiShi(string paymentYyyymm, int shime, string shiireCodeFrom = "", string shiireCodeTo = "") {
		var (dayFrom, dayTo) = GetClosingPeriod(paymentYyyymm, shime);
		// 期首日(MasterSysman.FiscalStartDate)以前は期首残高として凍結し再計算しない。
		// 締期間が期首前に終わるなら何もせず、期首をまたぐ場合は開始日を期首日まで切り上げる。
		var fiscalStart = GetFiscalStartDate();
		if (string.CompareOrdinal(dayTo, fiscalStart) < 0) return 0;
		if (string.CompareOrdinal(dayFrom, fiscalStart) < 0) dayFrom = fiscalStart;
		var vdate = Common.GetVdate();
		var cnt = 0;
		var transactionStarted = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			transactionStarted = true;
			const string deleteSql = @"
DELETE FROM SummaryKaiShi
WHERE DenDay = @1
  AND Id_Shiire IN (
		SELECT Id FROM MasterShiire
		WHERE Shime1 = @2
		  AND (@3 = '' OR Code >= @3)
		  AND (@4 = '' OR Code <= @4)
	);";
			var sql = $@"
WITH kinmap AS (
	SELECT Id, Code FROM MasterMeisho WHERE Kubun = 'KIN'
),
purchases AS (
	SELECT
		t.Id_Shiire,
		SUM(CASE WHEN t.Kubun BETWEEN 10 AND 19 OR t.Kubun BETWEEN 40 AND 89 THEN t.Total ELSE 0 END) AS Shiire,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN t.Total ELSE 0 END) AS Henpin,
		SUM(CASE WHEN t.Kubun BETWEEN 30 AND 39 THEN t.Total ELSE 0 END) AS Nebiki,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN t.Tax * t.CalcFlag ELSE t.Tax END) AS Tax
	FROM Tran03Shiire AS t
	WHERE {KakeDenWhere} AND t.KakeDay BETWEEN @0 AND @1
	GROUP BY t.Id_Shiire
),
payments AS (
	SELECT
		t.Id_Torisaki AS Id_Shiire,
		{KinBucket("01")} AS Cash,
		{KinBucket("02")} AS Fee,
		{KinBucket("03")} AS Densai,
		{KinBucket("04")} AS Offset,
		{KinBucket("99")} AS Other
	FROM Tran07Shiharai AS t, {KinMeisaiFrom}
	LEFT JOIN kinmap AS k ON k.Id = json_extract(m.value, '$.Id_Kin')
	WHERE t.KakeDay BETWEEN @0 AND @1
	GROUP BY t.Id_Torisaki
),
targets AS (
	SELECT Id, PayMonth, PayDay
	FROM MasterShiire
	WHERE Shime1 = @2
	  AND (@3 = '' OR Code >= @3)
	  AND (@4 = '' OR Code <= @4)
),
previousBalance AS (
	SELECT Id_Shiire, SUM(TotalOut - TotalShiire) AS Balance
	FROM SummaryKaiShi
	WHERE DayTo < @0
	GROUP BY Id_Shiire
),
calculated AS (
	SELECT
		t.Id AS Id_Shiire,
		IFNULL(s.Shiire, 0) AS Shiire,
		IFNULL(s.Henpin, 0) AS Henpin,
		IFNULL(s.Nebiki, 0) AS Nebiki,
		IFNULL(s.Tax, 0) AS Tax,
		IFNULL(p.Cash, 0) AS Cash,
		IFNULL(p.Fee, 0) AS Fee,
		IFNULL(p.Densai, 0) AS Densai,
		IFNULL(p.Offset, 0) AS Offset,
		IFNULL(p.Other, 0) AS Other,
		strftime('%Y%m%d', CASE
			WHEN t.PayDay IN (0, 99) THEN date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth + 1), '-1 day')
			ELSE date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth), printf('+%d days', MIN(t.PayDay, CAST(strftime('%d', date(substr(@5, 1, 4) || '-' || substr(@5, 5, 2) || '-01', printf('+%d months', t.PayMonth + 1), '-1 day')) AS INTEGER)) - 1))
		END) AS ShiharaiYoteiDay
	FROM targets AS t
	LEFT JOIN purchases AS s ON s.Id_Shiire = t.Id
	LEFT JOIN payments AS p ON p.Id_Shiire = t.Id
)
INSERT INTO SummaryKaiShi (
	Id_Shiire, DenDay, DayFrom, DayTo, ShiharaiYoteiDay,
	Balance, TotalOut, TotalShiire, Shiire, Henpin, Nebiki, Tax,
	Cash, Fee, Densai, Offset, Other, Vdc, Vdu
)
SELECT
	c.Id_Shiire,
	@1,
	@0,
	@1,
	c.ShiharaiYoteiDay,
	IFNULL(b.Balance, 0) + (c.Cash + c.Fee + c.Densai + c.Offset + c.Other) - (c.Shiire - c.Henpin - c.Nebiki + c.Tax),
	c.Cash + c.Fee + c.Densai + c.Offset + c.Other,
	c.Shiire - c.Henpin - c.Nebiki + c.Tax,
	c.Shiire, c.Henpin, c.Nebiki, c.Tax,
	c.Cash, c.Fee, c.Densai, c.Offset, c.Other,
	{vdate}, {vdate}
FROM calculated AS c
LEFT JOIN previousBalance AS b ON b.Id_Shiire = c.Id_Shiire;
";
			var period = $"{dayFrom}-{dayTo},締日={shime}";
			var parameters = new object[] { dayFrom, dayTo, shime, shiireCodeFrom, shiireCodeTo, paymentYyyymm };
			cnt += ExecuteAndCounts(deleteSql, parameters, "CalcSummaryKaiShi(delete)", "SummaryKaiShi", period);
			cnt += ExecuteAndCounts(sql, parameters, "CalcSummaryKaiShi", "SummaryKaiShi", period);
			_db.CompleteTransaction();
			transactionStarted = false;
			return cnt;
		}
		catch {
			if (transactionStarted) {
				_db.AbortTransaction();
			}
			throw;
		}
	}
	/// <summary>
	/// SummaryKaiKakeの年月のデータを集計する(再作成)
	/// <para>
	/// 仕入は区分(<see cref="EnumShiire"/>)で 仕入 / 返品 / 値引 へ、支払は明細の <c>Id_Kin</c> で
	/// 現金 / 振込手数料 / 手形 / 相殺 / その他 へ振り分ける。仕入・返品・値引は税抜 <c>Total</c> の正値内訳とし、
	/// 合計は内訳と税から算出する。区分99(その他仕入)は仕入へ畳み込む。売掛側(<see cref="CalcSummaryUriKake"/>)と同じ規則である。
	/// </para>
	/// </summary>
	/// <param name="DatefromYyyymm"></param>
	/// <param name="DateToYyyymm"></param>
	/// <returns></returns>
	public int CalcSummaryKaiKake(string DatefromYyyymm, string DateToYyyymm) {
		var cnt = 0;
		// 期首年月(MasterSysman.FiscalStartDate)以前は期首残高として凍結し再計算しない。
		var fiscalMonth = GetFiscalStartDate()[..6];
		if (string.CompareOrdinal(DateToYyyymm, fiscalMonth) < 0) return cnt;
		if (string.CompareOrdinal(DatefromYyyymm, fiscalMonth) < 0) DatefromYyyymm = fiscalMonth;
		const string deleteSql = "DELETE FROM SummaryKaiKake WHERE DenMonth BETWEEN @0 AND @1";
		var vdate = Common.GetVdate();
		var toMonth = ExtendToMonth("SummaryKaiKake", "Tran03Shiire", "Tran07Shiharai", DateToYyyymm);
		var sql = @$"
WITH kinmap AS (
	SELECT Id, Code FROM MasterMeisho WHERE Kubun = 'KIN'
),
movements AS (
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Shiire,
		SUM(CASE WHEN t.Kubun BETWEEN 10 AND 19 OR t.Kubun BETWEEN 40 AND 89 OR t.Kubun = 99 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Shiire,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Henpin,
		SUM(CASE WHEN t.Kubun BETWEEN 30 AND 39 THEN IFNULL(t.Total, 0) ELSE 0 END) AS Nebiki,
		SUM(CASE WHEN t.Kubun BETWEEN 20 AND 29 THEN IFNULL(t.Tax, 0) * t.CalcFlag ELSE IFNULL(t.Tax, 0) END) AS Tax,
		0 AS Cash, 0 AS Fee, 0 AS Densai, 0 AS Offset, 0 AS Other
	FROM Tran03Shiire AS t
	WHERE {KakeDenWhere} AND substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Shiire
	UNION ALL
	-- 支払は有効JSONの明細だけを正値源とする。不正JSONは KinMeisaiFrom により空明細となる。
	SELECT
		substr(t.KakeDay, 1, 6) AS DenMonth,
		t.Id_Torisaki AS Id_Shiire,
		0, 0, 0, 0,
		{KinBucket("01")} AS Cash,
		{KinBucket("02")} AS Fee,
		{KinBucket("03")} AS Densai,
		{KinBucket("04")} AS Offset,
		{KinBucket("99")} AS Other
	FROM Tran07Shiharai AS t, {KinMeisaiFrom}
	LEFT JOIN kinmap AS k ON k.Id = json_extract(m.value, '$.Id_Kin')
	WHERE substr(t.KakeDay, 1, 6) BETWEEN @0 AND @1
	GROUP BY DenMonth, t.Id_Torisaki
),
monthly AS (
	SELECT
		DenMonth,
		Id_Shiire,
		SUM(Shiire) AS Shiire,
		SUM(Henpin) AS Henpin,
		SUM(Nebiki) AS Nebiki,
		SUM(Tax) AS Tax,
		SUM(Cash) AS Cash,
		SUM(Fee) AS Fee,
		SUM(Densai) AS Densai,
		SUM(Offset) AS Offset,
		SUM(Other) AS Other
	FROM movements
	GROUP BY DenMonth, Id_Shiire
),
calculated AS (
	SELECT
		m.*,
		m.Shiire - m.Henpin - m.Nebiki + m.Tax AS TotalShiire,
		m.Cash + m.Fee + m.Densai + m.Offset + m.Other AS TotalOut
	FROM monthly AS m
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
	c.Id_Shiire,
	c.DenMonth,
	IFNULL(p.Balance, 0) + SUM(c.TotalOut - c.TotalShiire) OVER (
		PARTITION BY c.Id_Shiire
		ORDER BY c.DenMonth
		ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
	) AS Balance,
	c.TotalOut,
	c.TotalShiire,
	c.Shiire,
	c.Henpin,
	c.Nebiki,
	c.Tax,
	c.Cash,
	c.Fee,
	c.Densai,
	c.Offset,
	c.Other,
	{vdate} AS Vdc,
	{vdate} AS Vdu
FROM calculated AS c
LEFT JOIN previousBalance AS p ON p.Id_Shiire = c.Id_Shiire;
";
		var period = $"{DatefromYyyymm}-{toMonth}";
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		cnt += ExecuteAndCounts(deleteSql, [DatefromYyyymm, toMonth], "CalcSummaryKaiKake(delete)", "SummaryKaiKake", period);
		cnt += ExecuteAndCounts(sql, [DatefromYyyymm, toMonth], "CalcSummaryKaiKake", "SummaryKaiKake", period);
		_db.CompleteTransaction();
		return cnt;
	}



}


