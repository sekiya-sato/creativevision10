using System;
using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="CostUpdateDb"/>（原価4項目 詳細設計 Step 4）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="SummaryDbTests"/>に合わせる。
/// </summary>
[TestClass]
public class CostUpdateDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostUpdateDbTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
		_db.KeepConnectionAlive = true;
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	private void CreateCoreTables() {
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		// CreateTableはKeyDmlの一意索引を作らないため、ON CONFLICTが参照する一意キーを明示的に作る
		// (SummaryDbTests.PrepareStockTablesと同じ作法)
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
	}

	private long InsertShohin(string code, string name, int tankaGenka = 0, int isZaiko = 1) {
		var shohin = new MasterShohin { Code = code, Name = name, TankaGenka = tankaGenka, IsZaiko = isZaiko, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertShain(string code, string name) {
		var shain = new MasterShain { Code = code, Name = name, Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		return shain.Id;
	}

	private static TranGenka NewGenka(long idShohin, string sumMonth, string effectiveDay, int costMethod, int changeKind, int afterCost, long vdu, string batchId = "b") => new() {
		BatchId = batchId,
		SumMonth = sumMonth,
		EffectiveDay = effectiveDay,
		CostMethod = costMethod,
		ChangeKind = changeKind,
		Id_Shohin = idShohin,
		BeforeCost = 0,
		AfterCost = afterCost,
		Vdc = vdu,
		Vdu = vdu,
	};

	// ------------------------------------------------------------------
	// ResolveCostAsOf
	// ------------------------------------------------------------------

	[TestMethod]
	public void ResolveCostAsOf_ReturnsLatestNotAfterAsOfDay_FutureHistoryIgnored() {
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		Db.Insert(NewGenka(idShohin, "202607", "20260720", 2, 0, 1000, 10));
		Db.Insert(NewGenka(idShohin, "202608", "20260820", 2, 0, 1100, 20));
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, 0, 1200, 30)); // 未来日、asOfDayより後

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ResolveCostAsOf(idShohin, "20260825", EnumCostMethod.TotalAverage);

		Assert.AreEqual(1100, result);
	}

	[TestMethod]
	public void ResolveCostAsOf_SameSumMonth_RevalRowWinsOverMonthlyRow_EvenWithOlderVdu() {
		// 設計書§13 U-19: ChangeKind DESC を Vdu DESC より前に置くため、
		// Vduが古くても評価替え行(ChangeKind=1)が優先される。
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		// EffectiveDayを同一にして、ChangeKindの優先順位だけを検証する(EffectiveDayが異なると
		// そちらが優先されてしまい、ChangeKind DESCの効果を検証できないため)
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, (int)EnumCostChangeKind.Monthly, 1000, vdu: 100));
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, (int)EnumCostChangeKind.Reval, 700, vdu: 10)); // Vduは古い

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ResolveCostAsOf(idShohin, "20260930", EnumCostMethod.TotalAverage);

		Assert.AreEqual(700, result);
	}

	[TestMethod]
	public void ResolveCostAsOf_NoHistoryForSelectedMethod_FallsBackToBaselineRow() {
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		Db.Insert(NewGenka(idShohin, "190101", "19010101", (int)EnumCostMethod.Fixed, (int)EnumCostChangeKind.Monthly, 500, vdu: 1));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ResolveCostAsOf(idShohin, "20260930", EnumCostMethod.TotalAverage);

		Assert.AreEqual(500, result);
	}

	[TestMethod]
	public void ResolveCostAsOf_NoHistoryAtAll_ReturnsZero() {
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ResolveCostAsOf(idShohin, "20260930", EnumCostMethod.TotalAverage);

		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public void ResolveCostAsOf_ExcludeRevalSumMonth_ExcludesSameMonthRevalRow() {
		// 設計書§16.5: 評価替えの対象抽出時は同月の評価替え行を除外して解決する(冪等性の土台)。
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, (int)EnumCostChangeKind.Monthly, 1000, vdu: 100));
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, (int)EnumCostChangeKind.Reval, 700, vdu: 200));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ResolveCostAsOf(idShohin, "20260930", EnumCostMethod.TotalAverage, excludeRevalSumMonth: "202609");

		Assert.AreEqual(1000, result);
	}

	[TestMethod]
	public void ResolveCostAsOf_MultipleProducts_MatchesSingleProductResults() {
		CreateCoreTables();
		var id1 = InsertShohin("S001", "商品1");
		var id2 = InsertShohin("S002", "商品2");
		Db.Insert(NewGenka(id1, "202608", "20260820", 2, 0, 1100, 20));
		Db.Insert(NewGenka(id2, "202608", "20260820", 2, 0, 2200, 20));

		var costUpdateDb = new CostUpdateDb(Db);
		var single1 = costUpdateDb.ResolveCostAsOf(id1, "20260825", EnumCostMethod.TotalAverage);
		var single2 = costUpdateDb.ResolveCostAsOf(id2, "20260825", EnumCostMethod.TotalAverage);
		var multi = costUpdateDb.ResolveCostAsOf([id1, id2], "20260825", EnumCostMethod.TotalAverage);

		Assert.AreEqual(single1, multi[id1]);
		Assert.AreEqual(single2, multi[id2]);
	}

	// ------------------------------------------------------------------
	// RefreshCurrentProductCost
	// ------------------------------------------------------------------

	[TestMethod]
	public void RefreshCurrentProductCost_UpdatesHistoryProduct_LeavesNoHistoryProductUnchanged() {
		CreateCoreTables();
		var withHistory = InsertShohin("S001", "商品1", tankaGenka: 0);
		var withoutHistory = InsertShohin("S002", "商品2", tankaGenka: 999);
		Db.Insert(NewGenka(withHistory, "202608", "20260820", 2, 0, 1234, 20));

		var costUpdateDb = new CostUpdateDb(Db);
		var updated = costUpdateDb.RefreshCurrentProductCost([withHistory, withoutHistory], EnumCostMethod.TotalAverage);

		Assert.AreEqual(1, updated);
		Assert.AreEqual(1234, Db.Single<MasterShohin>("WHERE Id=@0", withHistory).TankaGenka);
		Assert.AreEqual(999, Db.Single<MasterShohin>("WHERE Id=@0", withoutHistory).TankaGenka);
	}

	[TestMethod]
	public void RefreshCurrentProductCost_ReRunningPastMonth_DoesNotRevertNewerEffectiveDay() {
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		Db.Insert(NewGenka(idShohin, "202609", "20260920", 2, 0, 1500, 30));

		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.RefreshCurrentProductCost([idShohin], EnumCostMethod.TotalAverage);
		Assert.AreEqual(1500, Db.Single<MasterShohin>("WHERE Id=@0", idShohin).TankaGenka);

		// 過去月(202608)の履歴をあとから追加して再実行しても、202609のほうがEffectiveDayが新しいため戻らない
		Db.Insert(NewGenka(idShohin, "202608", "20260820", 2, 0, 1000, 40));
		costUpdateDb.RefreshCurrentProductCost([idShohin], EnumCostMethod.TotalAverage);

		Assert.AreEqual(1500, Db.Single<MasterShohin>("WHERE Id=@0", idShohin).TankaGenka);
	}

	// ------------------------------------------------------------------
	// EnsureBaselineCostRows
	// ------------------------------------------------------------------

	[TestMethod]
	public void EnsureBaselineCostRows_CreatesOnlyForProductsWithoutHistory_IdempotentOnSecondCall() {
		CreateCoreTables();
		var withHistory = InsertShohin("S001", "商品1", tankaGenka: 500);
		var withoutHistory = InsertShohin("S002", "商品2", tankaGenka: 800);
		var idShain = InsertShain("E001", "社員1");
		Db.Insert(NewGenka(withHistory, "202608", "20260820", 2, 0, 1234, 20));

		var costUpdateDb = new CostUpdateDb(Db);
		var created = costUpdateDb.EnsureBaselineCostRows([withHistory, withoutHistory], "batch-1", idShain);

		Assert.AreEqual(1, created);
		var baselineRows = Db.Fetch<TranGenka>("WHERE Id_Shohin=@0 AND SumMonth='190101'", withoutHistory);
		Assert.AreEqual(1, baselineRows.Count);
		Assert.AreEqual(800, baselineRows[0].AfterCost);
		Assert.AreEqual(0, Db.Fetch<TranGenka>("WHERE Id_Shohin=@0 AND SumMonth='190101'", withHistory).Count);

		var createdAgain = costUpdateDb.EnsureBaselineCostRows([withHistory, withoutHistory], "batch-2", idShain);
		Assert.AreEqual(0, createdAgain);
		Assert.AreEqual(1, Db.Fetch<TranGenka>("WHERE Id_Shohin=@0", withoutHistory).Count);
	}

	// ------------------------------------------------------------------
	// UpsertGenkaRows
	// ------------------------------------------------------------------

	[TestMethod]
	public void UpsertGenkaRows_SameUniqueKeyTwice_ReplacesInPlace_DifferentChangeKindAddsRow() {
		CreateCoreTables();
		var idShohin = InsertShohin("S001", "商品1");
		var costUpdateDb = new CostUpdateDb(Db);

		costUpdateDb.UpsertGenkaRows([NewGenka(idShohin, "202609", "20260920", 2, 0, 1000, 10, "batch-1")]);
		costUpdateDb.UpsertGenkaRows([NewGenka(idShohin, "202609", "20260920", 2, 0, 1200, 20, "batch-2")]);

		var monthlyRows = Db.Fetch<TranGenka>("WHERE Id_Shohin=@0 AND SumMonth='202609' AND ChangeKind=0", idShohin);
		Assert.AreEqual(1, monthlyRows.Count);
		Assert.AreEqual(1200, monthlyRows[0].AfterCost);
		Assert.AreEqual("batch-2", monthlyRows[0].BatchId);

		costUpdateDb.UpsertGenkaRows([NewGenka(idShohin, "202609", "20260920", 2, (int)EnumCostChangeKind.Reval, 900, 30, "batch-3")]);
		var allRows = Db.Fetch<TranGenka>("WHERE Id_Shohin=@0 AND SumMonth='202609'", idShohin);
		Assert.AreEqual(2, allRows.Count);
	}

	// ------------------------------------------------------------------
	// FetchCostMonthStatus (ProcessKind=3)
	// ------------------------------------------------------------------

	private void CreateStatusTables() {
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(Tran01Tenuri), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
	}

	private long InsertShiire(long idShohin, string denDay, long vdu, int isStock = 1, int isPay = 1, int kubun = 10) {
		var header = new Tran03Shiire {
			DenDay = denDay,
			IsStock = isStock,
			IsPay = isPay,
			Kubun = kubun,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = idShohin, Su = 1 }],
			Vdc = vdu,
			Vdu = vdu,
		};
		Db.Insert(header);
		return header.Id;
	}

	[TestMethod]
	public void FetchCostMonthStatus_NoHistory_ReturnsNotRun() {
		CreateCoreTables();
		CreateStatusTables();
		Db.Insert(new MasterSysman { ShimeBi = 20, CostMethod = (int)EnumCostMethod.TotalAverage, Vdc = 1, Vdu = 1 });

		var costUpdateDb = new CostUpdateDb(Db);
		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.CostUpdate);

		Assert.AreEqual(EnumCostProcessStatus.NotRun, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_UnchangedSinceLastRun_ReturnsCompleted() {
		CreateCoreTables();
		CreateStatusTables();
		Db.Insert(new MasterSysman { ShimeBi = 20, CostMethod = (int)EnumCostMethod.TotalAverage, Vdc = 1, Vdu = 1 });
		var idShohin = InsertShohin("S001", "商品1");
		InsertShiire(idShohin, "20260901", vdu: 100);
		Db.Insert(NewGenka(idShohin, "202609", "20260920", (int)EnumCostMethod.TotalAverage, 0, 1000, vdu: 200, batchId: "batch-1"));

		var costUpdateDb = new CostUpdateDb(Db);
		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.CostUpdate);

		Assert.AreEqual(EnumCostProcessStatus.Completed, status.Status);
		Assert.AreEqual(200, status.LastRunAt);
		Assert.AreEqual("batch-1", status.BatchId);
		Assert.AreEqual(EnumCostMethod.TotalAverage, status.CostMethod);
	}

	[TestMethod]
	public void FetchCostMonthStatus_CostMethodChanged_ReturnsRerunRequired() {
		CreateCoreTables();
		CreateStatusTables();
		Db.Insert(new MasterSysman { ShimeBi = 20, CostMethod = (int)EnumCostMethod.TotalAverage, Vdc = 1, Vdu = 1 });
		var idShohin = InsertShohin("S001", "商品1");
		InsertShiire(idShohin, "20260901", vdu: 100);
		Db.Insert(NewGenka(idShohin, "202609", "20260920", (int)EnumCostMethod.TotalAverage, 0, 1000, vdu: 200));

		// 原価方式を変更する(方式変更のみでは既存原価は変わらないが、状態は再実行要になる)
		Db.Execute($"UPDATE {nameof(MasterSysman)} SET CostMethod=@0", (int)EnumCostMethod.LastPurchase);

		var costUpdateDb = new CostUpdateDb(Db);
		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.CostUpdate);

		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_PurchaseUpdatedAfterLastRun_ReturnsRerunRequired() {
		CreateCoreTables();
		CreateStatusTables();
		Db.Insert(new MasterSysman { ShimeBi = 20, CostMethod = (int)EnumCostMethod.TotalAverage, Vdc = 1, Vdu = 1 });
		var idShohin = InsertShohin("S001", "商品1");
		var shiireId = InsertShiire(idShohin, "20260901", vdu: 100);
		Db.Insert(NewGenka(idShohin, "202609", "20260920", (int)EnumCostMethod.TotalAverage, 0, 1000, vdu: 200));

		// 対象期間の仕入をLastRunAt(200)より後に更新する
		Db.Execute($"UPDATE {nameof(Tran03Shiire)} SET Vdu=@0 WHERE Id=@1", 300, shiireId);

		var costUpdateDb = new CostUpdateDb(Db);
		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.CostUpdate);

		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	// ------------------------------------------------------------------
	// ResolvePeriod
	// ------------------------------------------------------------------

	[TestMethod]
	public void ResolvePeriod_ClosingDay20_202609_Resolves20260821To20260920() {
		CreateCoreTables();
		Db.Insert(new MasterSysman { ShimeBi = 20, CostMethod = 0, Vdc = 1, Vdu = 1 });

		var costUpdateDb = new CostUpdateDb(Db);
		var period = costUpdateDb.ResolvePeriod("202609");

		Assert.AreEqual("20260821", period.DayFrom);
		Assert.AreEqual("20260920", period.DayTo);
	}
}
