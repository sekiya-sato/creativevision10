using System.Linq;
using System.Threading.Tasks;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class SummaryDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"SummaryDbTests-{System.Guid.NewGuid():N}";
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

	[TestMethod]
	public void CalcSummaryStockCumulative_UpdatesRunningTotalsInSqlite() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryStock), true, false);

		db.Insert(new SummaryStock {
			SumMonth = "202601",
			Id_Soko = 1,
			Id_Shohin = 10,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = 10,
			Vdc = 1,
			Vdu = 1,
		});
		db.Insert(new SummaryStock {
			SumMonth = "202602",
			Id_Soko = 1,
			Id_Shohin = 10,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = 5,
			Vdc = 1,
			Vdu = 1,
		});

		var summaryDb = new SummaryDb(db);
		var updated = summaryDb.CalcSummaryStockCumulative("202602");
		var rows = db.Fetch<SummaryStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3 order by SumMonth",
			1,
			10,
			100,
			1000);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(10, rows[0].CumulativeSu);
		Assert.AreEqual(15, rows[1].CumulativeSu);
		Assert.IsTrue(updated >= 2);
	}

	[TestMethod]
	public void CalcSummaryRealStockRange_RebuildsOnlyTargetWarehouseProductColorSize() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryStock), true, false);
		db.CreateTable(typeof(SummaryRealStock), true, false);

		InsertSummaryStock(db, "202601", 1, 10, 100, 1000, 10);
		InsertSummaryStock(db, "202601", 1, 10, 100, 1001, 7);
		InsertSummaryStock(db, "202602", 1, 10, 100, 1000, 5);
		InsertSummaryStock(db, "202601", 2, 20, 200, 2000, 30);

		db.Insert(new SummaryRealStock { Id_Soko = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = 999, Vdc = 1, Vdu = 1 });
		db.Insert(new SummaryRealStock { Id_Soko = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1001, Su = 999, Vdc = 1, Vdu = 1 });
		db.Insert(new SummaryRealStock { Id_Soko = 2, Id_Shohin = 20, Id_Col = 200, Id_Siz = 2000, Su = 777, Vdc = 1, Vdu = 1 });

		var summaryDb = new SummaryDb(db);
		summaryDb.CalcSummaryRealStockRange("202602", "202602");
		var targetRows = db.Fetch<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 order by Id_Siz",
			1,
			10,
			100);
		var unrelated = db.Single<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			2,
			20,
			200,
			2000);

		Assert.AreEqual(2, targetRows.Count);
		Assert.AreEqual(15, targetRows[0].Su);
		Assert.AreEqual(999, targetRows[1].Su);
		Assert.AreEqual(777, unrelated.Su);
	}

	[TestMethod]
	public void CalcTran2SummaryStock_ImmediateTransferAndInvert_RestoresSourceAndDestination() {
		var db = PrepareStockTables();
		db.CreateTable(typeof(Tran05Ido), true, false);
		var tran = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(tran);
		var summaryDb = new SummaryDb(db);

		ApplyImmediate(summaryDb, tran, false);

		AssertRealStock(db, 1, -7);
		AssertRealStock(db, 2, 7);
		AssertSummaryStock(db, "202608", 1, -7, 0, 7, 0);
		AssertSummaryStock(db, "202608", 2, 7, 7, 0, 0);

		ApplyImmediate(summaryDb, tran, true);

		AssertRealStock(db, 1, 0);
		AssertRealStock(db, 2, 0);
		AssertSummaryStock(db, "202608", 1, 0, 0, 0, 0);
		AssertSummaryStock(db, "202608", 2, 0, 0, 0, 0);
	}

	[TestMethod]
	public void CalcTran2SummaryStock_TransitOutAndReceipt_UpdateTransitWithoutPrematureRealStock() {
		var db = PrepareStockTables();
		db.CreateTable(typeof(Tran10IdoOut), true, false);
		db.CreateTable(typeof(Tran11IdoIn), true, false);
		var transitOut = CreateTransfer<Tran10IdoOut>("20260815", 1, 2, 5);
		db.Insert(transitOut);
		var receipt = CreateTransfer<Tran11IdoIn>("20260816", 1, 2, 5);
		db.Insert(receipt);
		var summaryDb = new SummaryDb(db);

		ApplyImmediate(summaryDb, transitOut, false);

		AssertRealStock(db, 1, -5);
		AssertNoRealStock(db, 2);
		AssertSummaryStock(db, "202608", 1, -5, 0, 5, 0);
		AssertSummaryStock(db, "202608", 2, 0, 0, 0, 5);

		ApplyImmediate(summaryDb, receipt, false);

		AssertRealStock(db, 1, -5);
		AssertRealStock(db, 2, 5);
		AssertSummaryStock(db, "202608", 2, 5, 5, 0, 0);

		ApplyImmediate(summaryDb, receipt, true);
		ApplyImmediate(summaryDb, transitOut, true);

		AssertRealStock(db, 1, 0);
		AssertRealStock(db, 2, 0);
		AssertSummaryStock(db, "202608", 1, 0, 0, 0, 0);
		AssertSummaryStock(db, "202608", 2, 0, 0, 0, 0);
	}

	[TestMethod]
	public async Task CalcTran2SummaryStock_PurchaseAndReturn_MatchesRebuild() {
		var db = PrepareAllStockTables();
		var purchase = CreatePurchase("20260810", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		var returned = CreatePurchase("20260811", 1, 2, EnumShiire.Henpin);
		db.Insert(returned);
		var summaryDb = new SummaryDb(db);

		ApplyImmediate(summaryDb, purchase, false);
		ApplyImmediate(summaryDb, returned, false);

		AssertRealStock(db, 1, 8);
		AssertSummaryStock(db, "202608", 1, 8, 8, 0, 0);
		var immediateSnapshot = GetStockSnapshot(db);

		await RunRebuildAsync(summaryDb, "202608", "202608");

		CollectionAssert.AreEqual(immediateSnapshot, GetStockSnapshot(db));
	}

	[TestMethod]
	public void CalcTran2SummaryStock_UpdateTransfer_ReversesOldValuesBeforeApplyingNewValues() {
		var db = PrepareStockTables();
		db.CreateTable(typeof(Tran05Ido), true, false);
		var tran = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(tran);
		var summaryDb = new SummaryDb(db);
		ApplyImmediate(summaryDb, tran, false);

		ApplyImmediate(summaryDb, tran, true);
		tran.Id_Ido = 3;
		tran.Jmeisai![0].Su = 4;
		db.Update(tran);
		ApplyImmediate(summaryDb, tran, false);

		AssertRealStock(db, 1, -4);
		AssertRealStock(db, 2, 0);
		AssertRealStock(db, 3, 4);
		AssertSummaryStock(db, "202608", 1, -4, 0, 4, 0);
		AssertSummaryStock(db, "202608", 2, 0, 0, 0, 0);
		AssertSummaryStock(db, "202608", 3, 4, 4, 0, 0);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_RepeatedRebuild_IsIdempotentAndMatchesImmediateUpdate() {
		var db = PrepareAllStockTables();
		var immediateTransfer = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(immediateTransfer);
		var transitOut = CreateTransfer<Tran10IdoOut>("20260816", 2, 3, 5);
		db.Insert(transitOut);
		var receipt = CreateTransfer<Tran11IdoIn>("20260817", 2, 3, 5);
		db.Insert(receipt);
		var summaryDb = new SummaryDb(db);
		ApplyImmediate(summaryDb, immediateTransfer, false);
		ApplyImmediate(summaryDb, transitOut, false);
		ApplyImmediate(summaryDb, receipt, false);
		var immediateSnapshot = GetStockSnapshot(db);

		await RunRebuildAsync(summaryDb, "202608", "202608");
		var firstRebuildSnapshot = GetStockSnapshot(db);
		await RunRebuildAsync(summaryDb, "202608", "202608");
		var secondRebuildSnapshot = GetStockSnapshot(db);

		CollectionAssert.AreEqual(immediateSnapshot, firstRebuildSnapshot);
		CollectionAssert.AreEqual(firstRebuildSnapshot, secondRebuildSnapshot);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_WhenLastTranDisappears_RemovesObsoleteStockRows() {
		var db = PrepareAllStockTables();
		var tran = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(tran);
		var summaryDb = new SummaryDb(db);
		await RunRebuildAsync(summaryDb, "202608", "202608");
		Assert.AreEqual(2, db.Fetch<SummaryStock>().Count);
		Assert.AreEqual(2, db.Fetch<SummaryRealStock>().Count);

		db.Delete(tran);
		await RunRebuildAsync(summaryDb, "202608", "202608");

		Assert.AreEqual(0, db.Fetch<SummaryStock>().Count);
		Assert.AreEqual(0, db.Fetch<SummaryRealStock>().Count);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_WhenLastTargetMonthTranDisappears_RestoresPriorMonthRealStock() {
		var db = PrepareAllStockTables();
		InsertSummaryStock(db, "202607", 1, 10, 100, 1000, 13);
		db.Insert(new SummaryRealStock { Id_Soko = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = 13, Vdc = 1, Vdu = 1 });
		var tran = CreatePurchase("20260810", 1, 7, EnumShiire.Shiire);
		db.Insert(tran);
		var summaryDb = new SummaryDb(db);

		await RunRebuildAsync(summaryDb, "202608", "202608");
		AssertRealStock(db, 1, 20);
		db.Delete(tran);

		await RunRebuildAsync(summaryDb, "202608", "202608");

		AssertSummaryStock(db, "202607", 1, 13, 0, 0, 0);
		Assert.AreEqual(0, db.Fetch<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).Count);
		AssertRealStock(db, 1, 13);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_WhenRebuildFails_RollsBackMonthlyAndRealStock() {
		var db = PrepareAllStockTables();
		InsertSummaryStock(db, "202608", 1, 10, 100, 1000, 19);
		db.Insert(new SummaryRealStock { Id_Soko = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = 19, Vdc = 1, Vdu = 1 });
		var before = GetStockSnapshot(db);
		db.Execute("DROP TABLE Tran03Shiire");
		var errors = new System.Collections.Generic.List<StreamStepProgress>();

		await foreach (var progress in new SummaryDb(db).SummaryAllAsyncStream(new CalcDateTermParameter("202608", "202608"))) {
			if (progress.IsError) {
				errors.Add(progress);
			}
		}

		Assert.AreEqual(1, errors.Count);
		StringAssert.Contains(errors[0].ErrorMessage, "Tran03Shiire");
		CollectionAssert.AreEqual(before, GetStockSnapshot(db));
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_Rebuild_PreservesOutsidePeriodAndUnrelatedKeys() {
		var db = PrepareAllStockTables();
		InsertSummaryStock(db, "202607", 9, 90, 900, 9000, 13);
		db.Insert(new SummaryRealStock { Id_Soko = 9, Id_Shohin = 90, Id_Col = 900, Id_Siz = 9000, Su = 13, Vdc = 1, Vdu = 1 });
		InsertSummaryStock(db, "202608", 8, 80, 800, 8000, 17);
		db.Insert(new SummaryRealStock { Id_Soko = 8, Id_Shohin = 80, Id_Col = 800, Id_Siz = 8000, Su = 17, Vdc = 1, Vdu = 1 });
		var tran = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(tran);

		await RunRebuildAsync(new SummaryDb(db), "202608", "202608");

		AssertSummaryStock(db, "202607", 9, 13, 0, 0, 0, 90, 900, 9000);
		AssertRealStock(db, 9, 13, 90, 900, 9000);
		Assert.AreEqual(0, db.Fetch<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 8).Count);
		AssertNoRealStock(db, 8, 80, 800, 8000);
		AssertSummaryStock(db, "202608", 1, -7, 0, 7, 0);
		AssertSummaryStock(db, "202608", 2, 7, 7, 0, 0);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_Rebuild_PreservesNonTranColumnsForRegeneratedNaturalKey() {
		var db = PrepareAllStockTables();
		db.Insert(new SummaryStock {
			SumMonth = "202608",
			Id_Soko = 1,
			Id_Shohin = 10,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = 999,
			CumulativeSu = 123,
			AdjustQty = 4,
			StocktakeDdate = "20260809",
			ActualQty = 88,
			Vdc = 1,
			Vdu = 1,
		});
		var tran = CreatePurchase("20260810", 1, 7, EnumShiire.Shiire);
		db.Insert(tran);

		await RunRebuildAsync(new SummaryDb(db), "202608", "202608");

		var rebuilt = db.Single<SummaryStock>(
			"where SumMonth=@0 and Id_Soko=@1 and Id_Shohin=@2 and Id_Col=@3 and Id_Siz=@4",
			"202608",
			1,
			10,
			100,
			1000);
		Assert.AreEqual(7, rebuilt.Su);
		Assert.AreEqual(123, rebuilt.CumulativeSu);
		Assert.AreEqual(4, rebuilt.AdjustQty);
		Assert.AreEqual("20260809", rebuilt.StocktakeDdate);
		Assert.AreEqual(88, rebuilt.ActualQty);
	}

	private static void InsertSummaryStock(ExDatabaseSqlite db, string sumMonth, long idSoko, long idShohin, long idCol, long idSiz, int su) {
		db.Insert(new SummaryStock {
			SumMonth = sumMonth,
			Id_Soko = idSoko,
			Id_Shohin = idShohin,
			Id_Col = idCol,
			Id_Siz = idSiz,
			Su = su,
			Vdc = 1,
			Vdu = 1,
		});
	}

	private ExDatabaseSqlite PrepareStockTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryStock), true, false);
		db.CreateTable(typeof(SummaryRealStock), true, false);
		db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		return db;
	}

	private ExDatabaseSqlite PrepareAllStockTables() {
		var db = PrepareStockTables();
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran01Tenuri), true, false);
		db.CreateTable(typeof(Tran03Shiire), true, false);
		db.CreateTable(typeof(Tran05Ido), true, false);
		db.CreateTable(typeof(Tran10IdoOut), true, false);
		db.CreateTable(typeof(Tran11IdoIn), true, false);
		return db;
	}

	private static T CreateTransfer<T>(string denDay, long idSoko, long idIdo, int su)
		where T : TranAllHeader, ITranIdo, new() => new() {
			DenDay = denDay,
			Id_Soko = idSoko,
			Id_Ido = idIdo,
			Jmeisai = [new Tran99Meisai {
			No = 1,
			Id_Shohin = 10,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = su,
		}],
		};

	private static Tran03Shiire CreatePurchase(string denDay, long idSoko, int su, EnumShiire kubun) {
		var tran = new Tran03Shiire {
			DenDay = denDay,
			KakeDay = denDay,
			Id_Soko = idSoko,
			Jmeisai = [new Tran99Meisai {
				No = 1,
				Id_Shohin = 10,
				Id_Col = 100,
				Id_Siz = 1000,
				Su = su,
			}],
		};
		tran.EnKubun = kubun;
		return tran;
	}

	private static void ApplyImmediate<T>(SummaryDb summaryDb, T tran, bool invertFlag)
		where T : TranAllHeader, ITranIdo {
		summaryDb.CalcTran2SummaryStock(typeof(T).Name, nameof(ITranDetail.Id_Soko), tran.Id, invertFlag);
		summaryDb.CalcTran2SummaryStock(typeof(T).Name, nameof(ITranIdo.Id_Ido), tran.Id, invertFlag);
	}

	private static void ApplyImmediate(SummaryDb summaryDb, Tran03Shiire tran, bool invertFlag) {
		summaryDb.CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranDetail.Id_Soko), tran.Id, invertFlag);
	}

	private static async Task RunRebuildAsync(SummaryDb summaryDb, string dateFrom, string dateTo) {
		await foreach (var progress in summaryDb.SummaryAllAsyncStream(new CalcDateTermParameter(dateFrom, dateTo))) {
			Assert.IsFalse(progress.IsError, $"{progress.StepName}: {progress.ErrorMessage}");
		}
	}

	private static string[] GetStockSnapshot(ExDatabaseSqlite db) {
		var monthly = db.Fetch<SummaryStock>("order by SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.Select(x => $"M:{x.SumMonth}:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.Su}:{x.InQty}:{x.OutQty}:{x.TransitQty}");
		var real = db.Fetch<SummaryRealStock>("order by Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.Select(x => $"R:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.Su}");
		return monthly.Concat(real).ToArray();
	}

	private static void AssertSummaryStock(
		ExDatabaseSqlite db,
		string sumMonth,
		long idSoko,
		int su,
		int inQty,
		int outQty,
		int transitQty,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var row = db.Single<SummaryStock>(
			"where SumMonth=@0 and Id_Soko=@1 and Id_Shohin=@2 and Id_Col=@3 and Id_Siz=@4",
			sumMonth,
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(su, row.Su);
		Assert.AreEqual(inQty, row.InQty);
		Assert.AreEqual(outQty, row.OutQty);
		Assert.AreEqual(transitQty, row.TransitQty);
	}

	private static void AssertRealStock(
		ExDatabaseSqlite db,
		long idSoko,
		int su,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var row = db.Single<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(su, row.Su);
	}

	private static void AssertNoRealStock(
		ExDatabaseSqlite db,
		long idSoko,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var rows = db.Fetch<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(0, rows.Count);
	}
}
