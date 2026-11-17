using System;
using System.Linq;
using System.Threading.Tasks;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 全社分析（<see cref="CorporateAnalysisDb"/>）の集計を、在庫再集計で作った SummaryStock / SummaryRealStock と
/// 手計算の期待値で確かめる。期首(n)=期末(n−1)、推移の合計=内訳の合計、日単位と月単位の在庫の一致を見る。
/// </summary>
[TestClass]
public class CorporateAnalysisDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private ExDatabaseSqlite Db => _db ?? throw new InvalidOperationException("DB未初期化");
	private long _s1;
	private long _s2;
	private long _brand1;
	private long _brand2;

	// 商品: P1(Id=10, ブランド1, 原価100) / P2(Id=20, ブランド2, 原価50)
	[TestInitialize]
	public async Task Initialize() {
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = $"CorporateAnalysisDbTests-{Guid.NewGuid():N}",
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		foreach (var t in new[] {
			typeof(SysSequence), typeof(SysHistAutoexec), typeof(MasterSysman), typeof(MasterTokui), typeof(MasterShohin), typeof(MasterMeisho),
			typeof(SummaryStock), typeof(SummaryRealStock), typeof(TranHaibun),
			typeof(Tran00Uriage), typeof(Tran01Tenuri), typeof(Tran03Shiire), typeof(Tran05Ido), typeof(Tran10IdoOut), typeof(Tran11IdoIn),
			typeof(Tran12Jyuchu), typeof(Tran13Hachu), typeof(Tran60Tana), typeof(Tran61Chosei),
		}) {
			Db.CreateTable(t, true, false);
		}
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Insert(new MasterSysman { ShimeBi = 99, FiscalStartDate = "20250401" });
		var s1 = new MasterTokui { Code = "W1", Name = "倉庫1", TenType = 0 };
		Db.Insert(s1);
		_s1 = s1.Id;
		var s2 = new MasterTokui { Code = "W2", Name = "店舗2", TenType = 6 };
		Db.Insert(s2);
		_s2 = s2.Id;
		var b1 = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "B1", Name = "ブランド1" };
		Db.Insert(b1);
		_brand1 = b1.Id;
		var b2 = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "B2", Name = "ブランド2" };
		Db.Insert(b2);
		_brand2 = b2.Id;
		InsertShohin(10, "P1", _brand1, 100);
		InsertShohin(20, "P2", _brand2, 50);

		Db.Insert(Tran<Tran03Shiire>("20260110", _s1, 10, 10, 1000));
		Db.Insert(Tran<Tran01Tenuri>("20260205", _s1, 10, 3, 900));
		var ret = Tran<Tran00Uriage>("20260220", _s1, 10, 1, 300);
		ret.Kubun = 20; // 返品: 数量・金額は正値、CalcFlag=-1
		Db.Insert(ret);
		var ido = Tran<Tran05Ido>("20260225", _s1, 10, 2, 0);
		ido.Id_Ido = _s2;
		Db.Insert(ido);
		Db.Insert(Tran<Tran03Shiire>("20260303", _s1, 20, 4, 400));
		Db.Insert(Tran<Tran13Hachu>("20260315", _s1, 20, 5, 500));
		Db.Insert(Tran<Tran12Jyuchu>("20260315", _s1, 10, 2, 600));
		Db.Insert(Tran<Tran01Tenuri>("20260402", _s2, 20, 1, 80)); // 期間後の動き（現在庫から差し引かれる側）

		await foreach (var progress in new SummaryDb(Db).SummaryAllAsyncStream(new CalcDateTermParameter("202601", "202604"))) {
			Assert.IsFalse(progress.IsError, $"{progress.StepName}: {progress.ErrorMessage}");
		}
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	private void InsertShohin(long id, string code, long brand, int genka) {
		var sh = new MasterShohin { Code = code, Name = code, Id_Brand = brand, TankaGenka = genka, IsZaiko = 1 };
		Db.Insert(sh);
		Db.Execute("UPDATE MasterShohin SET Id = @0 WHERE Id = @1", id, sh.Id);
	}

	private static T Tran<T>(string day, long soko, long shohin, int su, int kingaku) where T : TranAllHeader, new() {
		var t = new T {
			DenDay = day,
			Id_Soko = soko,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = shohin, Id_Col = 1, Id_Siz = 1, Su = su, Kingaku = kingaku }],
		};
		return t;
	}

	private CorporateAnalysisResult Run(CorporateAnalysisUnit unit, string from, string to, bool byBucket,
		CorporateAnalysisAxis axis = CorporateAnalysisAxis.None, params CorporateAnalysisFilter[] filters) =>
		new CorporateAnalysisDb(Db).Analyze(new CorporateAnalysisParameter {
			Unit = unit, From = from, To = to, ByBucket = byBucket, GroupAxis = axis, Filters = [.. filters],
		});

	[TestMethod]
	public void Month_Trend_StockFlowsAndProfit() {
		var r = Run(CorporateAnalysisUnit.Month, "202601", "202603", true);
		Assert.HasCount(3, r.Rows);
		var jan = r.Rows[0];
		var feb = r.Rows[1];
		var mar = r.Rows[2];
		Assert.AreEqual(0, jan.OpeningStockQty);
		Assert.AreEqual(10, jan.ClosingStockQty);
		Assert.AreEqual(1000, jan.ClosingStockAmount);
		Assert.AreEqual(1000, jan.ShiireAmount);
		// 2月: 店舗売上3、返品1、倉庫間移動は全社で相殺
		Assert.AreEqual(10, feb.OpeningStockQty);
		Assert.AreEqual(8, feb.ClosingStockQty);
		Assert.AreEqual(2, feb.SalesQty);
		Assert.AreEqual(600, feb.SalesAmount);
		Assert.AreEqual(200, feb.SalesCost);
		Assert.AreEqual(400, feb.GrossProfit);
		Assert.AreEqual(8, mar.OpeningStockQty);
		Assert.AreEqual(12, mar.ClosingStockQty);
		Assert.AreEqual(1000, mar.ClosingStockAmount);
		Assert.AreEqual(500, mar.HachuAmount);
		Assert.AreEqual(5, mar.HachuQty);
		Assert.AreEqual(600, mar.JuchuAmount);
		Assert.AreEqual(400, mar.ShiireAmount);
	}

	[TestMethod]
	public void Day_StockMatchesMonthlyAndCrossesTransfers() {
		var r = Run(CorporateAnalysisUnit.Day, "20260219", "20260226", true);
		Assert.HasCount(8, r.Buckets);
		var d19 = r.Rows.Single(x => x.BucketKey == "20260219");
		var d20 = r.Rows.Single(x => x.BucketKey == "20260220");
		var d26 = r.Rows.Single(x => x.BucketKey == "20260226");
		Assert.AreEqual(7, d19.OpeningStockQty);
		Assert.AreEqual(7, d19.ClosingStockQty);
		Assert.AreEqual(8, d20.ClosingStockQty);
		Assert.AreEqual(-300, d20.SalesAmount);
		Assert.AreEqual(8, d26.ClosingStockQty);

		var whole = Run(CorporateAnalysisUnit.Day, "20260201", "20260228", false).Rows.Single();
		Assert.AreEqual(10, whole.OpeningStockQty);
		Assert.AreEqual(8, whole.ClosingStockQty);
		Assert.AreEqual(600, whole.SalesAmount);
	}

	[TestMethod]
	public void Year_UsesFiscalStartMonth() {
		var r = Run(CorporateAnalysisUnit.Year, "2025", "2025", true);
		Assert.AreEqual("202504", r.Buckets.Single().MonthFrom);
		Assert.AreEqual("202603", r.Buckets.Single().MonthTo);
		var row = r.Rows.Single();
		Assert.AreEqual(0, row.OpeningStockQty);
		Assert.AreEqual(12, row.ClosingStockQty);
		Assert.AreEqual(1400, row.ShiireAmount);
	}

	[TestMethod]
	public void BrandBreakdown_SumsToTotal_AndFilterDrillsDown() {
		var total = Run(CorporateAnalysisUnit.Month, "202601", "202603", false).Rows.Single();
		var brands = Run(CorporateAnalysisUnit.Month, "202601", "202603", false, CorporateAnalysisAxis.Brand).Rows;
		Assert.HasCount(2, brands);
		Assert.AreEqual(total.ClosingStockAmount, brands.Sum(x => x.ClosingStockAmount));
		Assert.AreEqual(total.SalesAmount, brands.Sum(x => x.SalesAmount));
		Assert.AreEqual(total.HachuAmount, brands.Sum(x => x.HachuAmount));
		var b1 = brands.Single(x => x.GroupId == _brand1);
		Assert.AreEqual("ブランド1", b1.GroupName);
		Assert.AreEqual(800, b1.ClosingStockAmount);

		var shohin = Run(CorporateAnalysisUnit.Month, "202601", "202603", false, CorporateAnalysisAxis.Shohin,
			new CorporateAnalysisFilter { Axis = CorporateAnalysisAxis.Brand, Id = _brand2 }).Rows;
		var p2 = shohin.Single();
		Assert.AreEqual("P2", p2.GroupCode);
		Assert.AreEqual(4, p2.ClosingStockQty);
		Assert.AreEqual(500, p2.HachuAmount);
	}

	[TestMethod]
	public void ShohinAxisByBucket_IsRejected() {
		Assert.ThrowsExactly<ArgumentException>(() => Run(CorporateAnalysisUnit.Month, "202601", "202603", true, CorporateAnalysisAxis.Shohin));
	}
}
