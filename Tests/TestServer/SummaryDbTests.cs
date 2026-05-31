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
}
