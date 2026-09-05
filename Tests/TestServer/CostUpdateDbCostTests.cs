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
/// 最終仕入原価更新・総平均原価更新（原価4項目 詳細設計 §5、§6、Step 7）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbSundryTests"/>・<see cref="CostUpdateDbTests"/>に合わせる。
/// </summary>
[TestClass]
public class CostUpdateDbCostTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostUpdateDbCostTests-{Guid.NewGuid():N}";
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

	// ------------------------------------------------------------------
	// テーブル・データ準備
	// ------------------------------------------------------------------

	private void CreateCostTables(int costMethod) {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		Db.CreateTable(typeof(MasterShiire), true, false);
		Db.CreateTable(typeof(MasterMaterial), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		// CreateTableはKeyDmlの一意索引を作らないため、ON CONFLICTが参照する一意キーを明示的に作る
		// (CostUpdateDbTests.CreateCoreTablesと同じ作法)
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
		// 末日締めにして対象月=暦月にする(CostUpdateDbSundryTests等と同じ作法。テストの期間計算を単純にするため)
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = costMethod, Vdc = 1, Vdu = 1 });
	}

	private long InsertShohin(string code, int tankaGenka = 0, int isZaiko = 1) {
		var shohin = new MasterShohin { Code = code, Name = $"商品{code}", TankaGenka = tankaGenka, IsZaiko = isZaiko, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertShain(string code = "E1") {
		var shain = new MasterShain { Code = code, Name = $"社員{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		return shain.Id;
	}

	private long InsertShiire(string code) {
		var shiire = new MasterShiire { Code = code, Name = $"仕入先{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shiire);
		return shiire.Id;
	}

	private long InsertMaterial(string code) {
		var material = new MasterMaterial { Code = code, Name = $"生地付属{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(material);
		return material.Id;
	}

	private long InsertPurchase(string denDay, int kubun, long idShohin, int su, long kingaku, int isStock = 1, int isPay = 1) {
		var meisai = new System.Collections.Generic.List<Tran99Meisai> {
			new() { No = 1, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku },
		};
		var header = new Tran03Shiire { DenDay = denDay, KakeDay = denDay, IsStock = isStock, IsPay = isPay, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private static Tran99Meisai NewLine(int no, long idShohin, int su, long kingaku) => new() {
		No = no, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku,
	};

	private long InsertPurchaseMulti(string denDay, int kubun, params Tran99Meisai[] meisai) {
		var header = new Tran03Shiire { DenDay = denDay, KakeDay = denDay, IsStock = 1, IsPay = 1, Jmeisai = [.. meisai], Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private static Tran99MaterialMeisai NewMaterialLine(int no, long idMaterial, long idShohin, int su, long kingaku) => new() {
		No = no, Id_Material = idMaterial, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku,
	};

	private long InsertMaterialHeader(string denDay, int kubun, long idShiire, params Tran99MaterialMeisai[] meisai) {
		var header = new Tran02Material { DenDay = denDay, KakeDay = denDay, Id_Shiire = idShiire, Jmeisai = [.. meisai], Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private void InsertOpeningStock(string sumMonth, long idShohin, int su) {
		Db.Insert(new SummaryStock { SumMonth = sumMonth, Id_Soko = 1, Id_Shohin = idShohin, Su = su, Vdc = 1, Vdu = 1 });
	}

	private static CostUpdateParameter NewParam(string targetMonth, long idShain = 0, string batchId = "B1") => new() {
		TargetMonth = targetMonth, Id_Shain = idShain, BatchId = batchId,
	};

	private TranGenka? FetchGenka(long idShohin, string sumMonth, int costMethod, int changeKind = 0) =>
		Db.FirstOrDefault<TranGenka>(
			"WHERE Id_Shohin=@0 AND SumMonth=@1 AND CostMethod=@2 AND ChangeKind=@3", idShohin, sumMonth, costMethod, changeKind);

	private int TankaGenkaOf(long idShohin) => Db.FirstOrDefault<MasterShohin>("WHERE Id=@0", idShohin)!.TankaGenka;

	// ------------------------------------------------------------------
	// 最終仕入原価更新(§5)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyLastPurchaseCost_SimpleCase_AfterCostMatchesUnitPrice() {
		// 設計書§11.5 T-04の土台
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000); // 単価100

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(100, FetchGenka(idShohin, "202609", (int)EnumCostMethod.LastPurchase)!.AfterCost);
	}

	[TestMethod]
	public void ComputeLastPurchase_SameDay_DifferentShiireId_LaterIdWins() {
		// 設計書§5.2: DenDay、Tran03Shiire.Idの降順で最終行を決定する
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 1, kingaku: 100); // 先の伝票(小さいId)、単価100
		InsertPurchase("20260910", 10, idShohin, su: 1, kingaku: 200); // 後の伝票(大きいId)、単価200

		var rows = new CostUpdateDb(Db).PreviewLastPurchaseCost(NewParam("202609"));

		Assert.AreEqual(200, rows.Single(r => r.Id_Shohin == idShohin).AfterCost);
	}

	[TestMethod]
	public void ComputeLastPurchase_SameHeader_DifferentLineNo_LargerNoWins() {
		// 設計書§5.2: 同一伝票内ではTran99Meisai.Noの降順で最終行を決定する
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShohin = InsertShohin("A1");
		InsertPurchaseMulti("20260910", 10,
			NewLine(1, idShohin, su: 1, kingaku: 100),
			NewLine(2, idShohin, su: 1, kingaku: 300));

		var rows = new CostUpdateDb(Db).PreviewLastPurchaseCost(NewParam("202609"));

		Assert.AreEqual(300, rows.Single(r => r.Id_Shohin == idShohin).AfterCost);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_RoundsAwayFromZero_NotFloor() {
		// 設計書§5.3: Kingaku=11489, Su=30 → 383(floorの382ではない)。DB経路でも固定する
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 30, kingaku: 11489);

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(383, FetchGenka(idShohin, "202609", (int)EnumCostMethod.LastPurchase)!.AfterCost);
	}

	[TestMethod]
	public void ComputeLastPurchase_ExcludesReturnDiscountOtherNonStockAndNonZaiko() {
		// 設計書§5.1: 仕入返品(20)・値引(30)・その他(99)・消化仕入(IsStock=0)・IsZaiko=0は対象外
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idA = InsertShohin("A1");
		var idB = InsertShohin("B1");
		var idC = InsertShohin("C1");
		var idD = InsertShohin("D1");
		var idE = InsertShohin("E1", isZaiko: 0);
		InsertPurchase("20260910", 20, idA, su: 1, kingaku: 100);
		InsertPurchase("20260910", 30, idB, su: 1, kingaku: 100);
		InsertPurchase("20260910", 99, idC, su: 1, kingaku: 100);
		InsertPurchase("20260910", 10, idD, su: 1, kingaku: 100, isStock: 0);
		InsertPurchase("20260910", 10, idE, su: 1, kingaku: 100);

		var rows = new CostUpdateDb(Db).PreviewLastPurchaseCost(NewParam("202609"));

		Assert.AreEqual(0, rows.Count);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_SundryChargesAreNotAdded() {
		// 設計書§3.6、§13 U-24: 諸掛は最終仕入原価に加算されない
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000); // 単価100
		InsertMaterialHeader("20260915", 10, idShiire, NewMaterialLine(1, idMaterial, idShohin, su: 1, kingaku: 500));

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		var genka = FetchGenka(idShohin, "202609", (int)EnumCostMethod.LastPurchase)!;
		Assert.AreEqual(100, genka.AfterCost);
		Assert.AreEqual(0, genka.SundryAmount);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_NoPurchaseInPeriod_NoRowCreated_TankaGenkaUnchanged() {
		// 設計書§5.4: 対象期間に通常仕入がない商品は更新しない
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 555);

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(0, result.UpdatedCount);
		Assert.AreEqual(0, Db.Fetch<TranGenka>("WHERE Id_Shohin=@0", idShohin).Count);
		Assert.AreEqual(555, TankaGenkaOf(idShohin));
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_DoesNotAutoRecalculateFollowingMonth() {
		// 設計書§5.4: 後続月の最終仕入原価は前月原価へ依存しないため自動再計算しない
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		Db.Insert(new TranGenka {
			BatchId = "prev", SumMonth = "202610", EffectiveDay = "20261010",
			CostMethod = (int)EnumCostMethod.LastPurchase, ChangeKind = 0,
			Id_Shohin = idShohin, BeforeCost = 100, AfterCost = 200, Vdc = 1, Vdu = 1,
		});
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 3000); // 単価300

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(200, FetchGenka(idShohin, "202610", (int)EnumCostMethod.LastPurchase)!.AfterCost);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_RunTwice_IsIdempotent() {
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var costUpdateDb = new CostUpdateDb(Db);
		var first = costUpdateDb.ApplyLastPurchaseCost(NewParam("202609", idShain));
		var second = costUpdateDb.ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsTrue(first.IsSuccess);
		Assert.IsTrue(second.IsSuccess);
		Assert.AreEqual(1, Db.Fetch<TranGenka>(
			"WHERE Id_Shohin=@0 AND CostMethod=@1", idShohin, (int)EnumCostMethod.LastPurchase).Count);
		Assert.AreEqual(100, FetchGenka(idShohin, "202609", (int)EnumCostMethod.LastPurchase)!.AfterCost);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_CostMethodFixed_IsBlocked() {
		CreateCostTables((int)EnumCostMethod.Fixed);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>("WHERE Id_Shohin=@0", idShohin).Count);
	}

	[TestMethod]
	public void ApplyLastPurchaseCost_CostMethodTotalAverage_IsBlocked() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var result = new CostUpdateDb(Db).ApplyLastPurchaseCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>("WHERE Id_Shohin=@0", idShohin).Count);
	}

	[TestMethod]
	public void PreviewLastPurchaseCost_WrongCostMethod_ReturnsMismatchRow() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);

		var rows = new CostUpdateDb(Db).PreviewLastPurchaseCost(NewParam("202609"));

		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual(EnumCostCalcError.CostMethodMismatch, rows[0].Error);
	}

	// ------------------------------------------------------------------
	// 総平均原価更新(§6)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyTotalAverageCost_BasicCase_MatchesSpecExample() {
		// 設計書§11.5 T-01: 前月在庫10個×5,000円、当月仕入14個・68,000円 → 4,916
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 5000);
		InsertOpeningStock("202608", idShohin, su: 10);
		InsertPurchase("20260910", 10, idShohin, su: 14, kingaku: 68000);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(4916, FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!.AfterCost);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_SundryChargesAddToNumerator_ReapplyDoesNotIncrease() {
		// 設計書§11.5 T-02: 商品Aへの諸掛明細が3行(30円・40円・30円) → 分子へ100円加算。再実行しても増えない
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);
		InsertMaterialHeader("20260911", 10, idShiire, NewMaterialLine(1, idMaterial, idShohin, su: 1, kingaku: 30));
		InsertMaterialHeader("20260912", 10, idShiire, NewMaterialLine(1, idMaterial, idShohin, su: 1, kingaku: 40));
		InsertMaterialHeader("20260913", 10, idShiire, NewMaterialLine(1, idMaterial, idShohin, su: 1, kingaku: 30));
		// Denominator=10、Numerator=1000+100=1100 → 110

		var costUpdateDb = new CostUpdateDb(Db);
		var first = costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain));
		Assert.IsTrue(first.IsSuccess);
		var genka1 = FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!;
		Assert.AreEqual(110, genka1.AfterCost);
		Assert.AreEqual(100, genka1.SundryAmount);

		var second = costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain));
		Assert.IsTrue(second.IsSuccess);
		var genka2 = FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!;
		Assert.AreEqual(110, genka2.AfterCost);
		Assert.AreEqual(100, genka2.SundryAmount);
		Assert.AreEqual(1, Db.Fetch<TranGenka>(
			"WHERE Id_Shohin=@0 AND CostMethod=@1", idShohin, (int)EnumCostMethod.TotalAverage).Count);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_NegativeOpeningStock_RollsBackEverything() {
		// 設計書§6.5「OpeningQty<0はエラー」、§2.4-2・§10.2「1件でもエラーがあれば全件ロールバック」
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idGood = InsertShohin("GOOD", tankaGenka: 100);
		var idBad = InsertShohin("BAD", tankaGenka: 100);
		InsertPurchase("20260910", 10, idGood, su: 10, kingaku: 1000);
		InsertPurchase("20260910", 10, idBad, su: 10, kingaku: 1000);
		InsertOpeningStock("202608", idBad, su: -5);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
		Assert.AreEqual(100, TankaGenkaOf(idGood));
	}

	[TestMethod]
	public void ApplyTotalAverageCost_BeforeCostNonPositiveWithOpeningStock_RollsBackEverything() {
		// 設計書§6.5「OpeningQty>0、BeforeCost<=0はエラー」
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idGood = InsertShohin("GOOD", tankaGenka: 100);
		var idBad = InsertShohin("BAD", tankaGenka: 0);
		InsertPurchase("20260910", 10, idGood, su: 10, kingaku: 1000);
		InsertPurchase("20260910", 10, idBad, su: 10, kingaku: 1000);
		InsertOpeningStock("202608", idBad, su: 5);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
		Assert.AreEqual(100, TankaGenkaOf(idGood));
	}

	[TestMethod]
	public void ApplyTotalAverageCost_AfterCostNonPositive_RollsBackEverything() {
		// 設計書§6.5「AfterCost<=0はエラー」
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idGood = InsertShohin("GOOD", tankaGenka: 100);
		var idBad = InsertShohin("BAD", tankaGenka: 100);
		InsertPurchase("20260910", 10, idGood, su: 10, kingaku: 1000);
		InsertPurchase("20260910", 10, idBad, su: 100, kingaku: 50); // floor(50/100)=0

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
		Assert.AreEqual(100, TankaGenkaOf(idGood));
	}

	[TestMethod]
	public void ApplyTotalAverageCost_ExcludesDiscountOtherAndNonStock() {
		// 設計書§6.1: Kubun=30/99、消化仕入(IsStock=0)は分母・分子に入らない
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);
		InsertPurchase("20260911", 30, idShohin, su: 999, kingaku: 999999);
		InsertPurchase("20260912", 99, idShohin, su: 999, kingaku: 999999);
		InsertPurchase("20260913", 10, idShohin, su: 5, kingaku: 500, isStock: 0);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		var genka = FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!;
		Assert.AreEqual(10, genka.PurchaseQty);
		Assert.AreEqual(1000, genka.PurchaseAmount);
		Assert.AreEqual(100, genka.AfterCost);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_PurchaseReturn_AppliesNegativeQtyAndAmount() {
		// 設計書§6.3: 仕入返品(Kubun=20)は数量・金額とも負で効く
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 20, kingaku: 2000);
		InsertPurchase("20260915", 20, idShohin, su: 5, kingaku: 500);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		var genka = FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!;
		Assert.AreEqual(15, genka.PurchaseQty);
		Assert.AreEqual(1500, genka.PurchaseAmount);
		Assert.AreEqual(100, genka.AfterCost);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_PastMonthRerun_RecalculatesFollowingMonthsInOrder() {
		// 設計書§11.5 T-05 / §6.6: 過去月Mと後続月に履歴があり、Mの仕入を修正して再実行すると、
		// M以降が古い順に再計算され、現在原価と全履歴が整合する
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);

		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain)).IsSuccess);

		InsertOpeningStock("202609", idShohin, su: 10);
		InsertPurchase("20261010", 10, idShohin, su: 10, kingaku: 2000);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202610", idShain)).IsSuccess);
		Assert.AreEqual(150, FetchGenka(idShohin, "202610", (int)EnumCostMethod.TotalAverage)!.AfterCost);

		// 202609の仕入を単価200(10個2000円)へ修正して再実行
		Db.Execute($"DELETE FROM {nameof(Tran03Shiire)} WHERE DenDay=@0", "20260910");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 2000);

		var rerun = costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(rerun.IsSuccess);
		Assert.AreEqual(200, FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!.AfterCost);
		// 202610はBeforeCost=200(202609の再計算結果)・前月在庫10個=2000、当月仕入10個2000円 → 200に再計算される
		Assert.AreEqual(200, FetchGenka(idShohin, "202610", (int)EnumCostMethod.TotalAverage)!.AfterCost);
		Assert.AreEqual(200, TankaGenkaOf(idShohin)); // 現在原価は最新月(202610)の値
	}

	[TestMethod]
	public void PreviewTotalAverageCost_IncludesFollowingMonthRows() {
		// 設計書§6.6: プレビューには対象月だけでなく再計算される後続月も表示する。DBは変更しない
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain)).IsSuccess);

		InsertOpeningStock("202609", idShohin, su: 10);
		InsertPurchase("20261010", 10, idShohin, su: 10, kingaku: 2000);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202610", idShain)).IsSuccess);

		Db.Execute($"DELETE FROM {nameof(Tran03Shiire)} WHERE DenDay=@0", "20260910");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 2000);

		var preview = costUpdateDb.PreviewTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(preview.Any(r => r.SumMonth == "202609" && r.Id_Shohin == idShohin));
		var row610 = preview.Single(r => r.SumMonth == "202610" && r.Id_Shohin == idShohin);
		Assert.AreEqual(200, row610.AfterCost);
		// プレビューなので実データは変更されていない(202610の保存済み値は旧計算のまま)
		Assert.AreEqual(150, FetchGenka(idShohin, "202610", (int)EnumCostMethod.TotalAverage)!.AfterCost);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_CostMethodFixed_IsBlocked() {
		CreateCostTables((int)EnumCostMethod.Fixed);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_CostMethodLastPurchase_IsBlocked() {
		CreateCostTables((int)EnumCostMethod.LastPurchase);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
	}

	[TestMethod]
	public void ApplyTotalAverageCost_RevalRow_SurvivesRerun_TankaGenkaStaysAtRevalValue() {
		// 設計書§2.7、§13 U-19: 評価替え行(ChangeKind=1)がある月で総平均原価更新を再実行しても、
		// RefreshCurrentProductCostの結果は評価替えの値のまま(ResolveCostAsOfのChangeKind DESCが効く)
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);

		var costUpdateDb = new CostUpdateDb(Db);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain)).IsSuccess);
		Assert.AreEqual(100, FetchGenka(idShohin, "202609", (int)EnumCostMethod.TotalAverage)!.AfterCost);

		// 評価替え行(ChangeKind=1)を直接挿入して模擬する(Step8の評価替え本体は本Stepの対象外)
		var period = costUpdateDb.ResolvePeriod("202609");
		Db.Insert(new TranGenka {
			BatchId = "reval1", SumMonth = "202609", EffectiveDay = period.DayFrom,
			CostMethod = (int)EnumCostMethod.TotalAverage, ChangeKind = (int)EnumCostChangeKind.Reval,
			SourceRevalId = 1, Id_Shohin = idShohin, BeforeCost = 100, AfterCost = 80, Vdc = 2, Vdu = 2,
		});
		costUpdateDb.RefreshCurrentProductCost([idShohin], EnumCostMethod.TotalAverage);
		Assert.AreEqual(80, TankaGenkaOf(idShohin));

		var rerun = costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(rerun.IsSuccess);
		Assert.AreEqual(80, TankaGenkaOf(idShohin));
	}
}
