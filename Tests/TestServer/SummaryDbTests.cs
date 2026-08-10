using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class SummaryDbTests {
	private ExDatabaseSqlite? _db;

	[TestInitialize]
	public void Initialize() {
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
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
}
