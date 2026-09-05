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
/// 諸掛（原価4項目 詳細設計 §3、Step 6）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbConsumptionTests"/>に合わせる。
/// </summary>
[TestClass]
public class CostUpdateDbSundryTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostUpdateDbSundryTests-{Guid.NewGuid():N}";
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

	private void CreateSundryTables(int costMethod = (int)EnumCostMethod.TotalAverage) {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterMaterial), true, false);
		Db.CreateTable(typeof(MasterShiire), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		// 末日締めにして対象月=暦月にする(CostUpdateDbConsumptionTestsと同じ作法。テストの期間計算を単純にするため)
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = costMethod, Vdc = 1, Vdu = 1 });
	}

	private long InsertShohin(string code, int isZaiko = 1) {
		var shohin = new MasterShohin { Code = code, Name = $"商品{code}", IsZaiko = isZaiko, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertMaterial(string code) {
		var material = new MasterMaterial { Code = code, Name = $"生地付属{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(material);
		return material.Id;
	}

	private long InsertShiire(string code) {
		var shiire = new MasterShiire { Code = code, Name = $"仕入先{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shiire);
		return shiire.Id;
	}

	private static Tran99MaterialMeisai NewMaterialLine(int no, long idMaterial, long idShohin, int su, long kingaku, int tax1 = 0) => new() {
		No = no,
		Id_Material = idMaterial,
		Id_Shohin = idShohin,
		Su = su,
		Tanka = su == 0 ? 0 : (int)(kingaku / su),
		Kingaku = kingaku,
		Tax = tax1,
	};

	private long InsertMaterialHeader(string denDay, int kubun, long idShiire, params Tran99MaterialMeisai[] meisai) {
		var header = new Tran02Material { DenDay = denDay, KakeDay = denDay, Id_Shiire = idShiire, Jmeisai = [.. meisai], Vdc = 1, Vdu = 1 };
		header.Kubun = kubun; // OnKubunChangedでCalcFlagを算出させる(Tran02Materialの既存作法)
		Db.Insert(header);
		return header.Id;
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

	private void InsertOpeningStock(string sumMonth, long idShohin, int su) {
		Db.Insert(new SummaryStock { SumMonth = sumMonth, Id_Soko = 1, Id_Shohin = idShohin, Su = su, Vdc = 1, Vdu = 1 });
	}

	private static CostUpdateParameter NewParam(string targetMonth) => new() { TargetMonth = targetMonth };

	// ------------------------------------------------------------------
	// SumSundryChargesByShohin(§3.4・§3.5)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Sum_ThreeSundryLines_AggregatesToTotal_AndIsIdempotent() {
		// 設計書§11.5 T-02: 商品Aへの諸掛明細が3行(30円・40円・30円) → 合計100円。2回呼んでも増えない
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertMaterialHeader("20260905", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 30));
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 40));
		InsertMaterialHeader("20260915", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 30));

		var costUpdateDb = new CostUpdateDb(Db);
		var period = costUpdateDb.ResolvePeriod("202609");

		var first = costUpdateDb.SumSundryChargesByShohin(period);
		Assert.AreEqual(100, first[idShohinA]);

		var second = costUpdateDb.SumSundryChargesByShohin(period);
		Assert.AreEqual(100, second[idShohinA]);
	}

	[TestMethod]
	public void Sum_Kubun20_AggregatesAsNegative() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertMaterialHeader("20260910", 20, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 50));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.SumSundryChargesByShohin(costUpdateDb.ResolvePeriod("202609"));

		Assert.AreEqual(-50, result[idShohinA]);
	}

	[TestMethod]
	public void Sum_Kubun30And99_AreExcluded() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertMaterialHeader("20260910", 30, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 999));
		InsertMaterialHeader("20260911", 99, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 999));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.SumSundryChargesByShohin(costUpdateDb.ResolvePeriod("202609"));

		Assert.IsFalse(result.ContainsKey(idShohinA));
	}

	[TestMethod]
	public void Sum_IdShohinZero_IsExcluded() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohin: 0, su: 1, kingaku: 999));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.SumSundryChargesByShohin(costUpdateDb.ResolvePeriod("202609"));

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void Sum_OutOfPeriodSlips_AreExcluded() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		// 対象月202609(末日締め=20260901-20260930)の前日・翌日
		InsertMaterialHeader("20260831", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));
		InsertMaterialHeader("20261001", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.SumSundryChargesByShohin(costUpdateDb.ResolvePeriod("202609"));

		Assert.IsFalse(result.ContainsKey(idShohinA));
	}

	[TestMethod]
	public void Sum_TaxColumns_AreNotIncluded() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		var header = new Tran02Material {
			DenDay = "20260910", KakeDay = "20260910", Id_Shiire = idShiire,
			Jmeisai = [NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100, tax1: 10)],
			Tax1 = 10, TaxableAmount1 = 100, Total = 110, Vdc = 1, Vdu = 1,
		};
		header.Kubun = 10;
		Db.Insert(header);

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.SumSundryChargesByShohin(costUpdateDb.ResolvePeriod("202609"));

		// 金額は明細Kingaku(税抜)のみ。Tax1・TaxableAmount1・Totalは混ざらない
		Assert.AreEqual(100, result[idShohinA]);
	}

	// ------------------------------------------------------------------
	// PreviewSundryCharges(§3.8)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Preview_ProductNotZaiko_IsError() {
		// 設計書§11.5 T-06
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1", isZaiko: 0);
		InsertPurchase("20260910", 10, idShohinA, su: 1, kingaku: 1000);
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.IsTrue(result.DetailRows.Any(r => r.Id_Shohin == idShohinA && r.Severity == EnumSundryCheckSeverity.Error));
		Assert.IsTrue(result.SummaryRows.Single(r => r.Id_Shohin == idShohinA).Severity == EnumSundryCheckSeverity.Error);
		Assert.IsTrue(result.ErrorCount > 0);
	}

	[TestMethod]
	public void Preview_ProductMissingInMaster_IsError() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		const long missingShohinId = 999999;
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, missingShohinId, su: 1, kingaku: 100));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.IsTrue(result.DetailRows.Any(r => r.Id_Shohin == missingShohinId && r.Severity == EnumSundryCheckSeverity.Error));
	}

	[TestMethod]
	public void Preview_NoPurchaseAndNoOpeningStock_IsError() {
		// 設計書§6.5: 当月仕入も前月在庫も無い商品への諸掛 → エラー
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinNone = InsertShohin("NONE");
		var idShohinWithPurchase = InsertShohin("PURCHASE");
		var idShohinWithOpening = InsertShohin("OPENING");
		InsertMaterialHeader("20260910", 10, idShiire,
			NewMaterialLine(1, idMaterial, idShohinNone, su: 1, kingaku: 100),
			NewMaterialLine(2, idMaterial, idShohinWithPurchase, su: 1, kingaku: 100),
			NewMaterialLine(3, idMaterial, idShohinWithOpening, su: 1, kingaku: 100));
		InsertPurchase("20260910", 10, idShohinWithPurchase, su: 2, kingaku: 2000);
		InsertOpeningStock("202608", idShohinWithOpening, su: 5);

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.AreEqual(EnumSundryCheckSeverity.Error, result.SummaryRows.Single(r => r.Id_Shohin == idShohinNone).Severity);
		Assert.AreEqual(EnumSundryCheckSeverity.Info, result.SummaryRows.Single(r => r.Id_Shohin == idShohinWithPurchase).Severity);
		Assert.AreEqual(EnumSundryCheckSeverity.Info, result.SummaryRows.Single(r => r.Id_Shohin == idShohinWithOpening).Severity);
	}

	/// <summary>
	/// 諸掛伝票(Id_Shohin>0の明細を1行でも含む伝票)の中の Id_Shohin=0 行は、負担商品の入力漏れとして
	/// 警告になる(設計書§3.8)。エラーではないので更新は止めない。
	/// </summary>
	[TestMethod]
	public void Preview_IdShohinZero_InSundrySlip_IsWarningNotError() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohinA, su: 1, kingaku: 1000);
		InsertMaterialHeader("20260910", 10, idShiire,
			NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100),
			NewMaterialLine(2, idMaterial, idShohin: 0, su: 1, kingaku: 100));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		var row = result.DetailRows.Single(r => r.Id_Shohin == 0);
		Assert.AreEqual(EnumSundryCheckSeverity.Warning, row.Severity);
		Assert.AreEqual(0, result.ErrorCount);
	}

	/// <summary>
	/// 諸掛を1行も含まない伝票（通常の生地付属仕入）の Id_Shohin=0 行は警告にしない。
	/// 設計書§3.4は Id_Shohin=0 を「在庫を持たない資材購入」として対象外と定めており、
	/// これを全件警告にすると諸掛と無関係な明細で画面が埋まり、本当の入力漏れが埋もれるため。
	/// </summary>
	[TestMethod]
	public void Preview_IdShohinZero_InNonSundrySlip_IsNotWarning() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		InsertMaterialHeader("20260910", 10, idShiire,
			NewMaterialLine(1, idMaterial, idShohin: 0, su: 1, kingaku: 100),
			NewMaterialLine(2, idMaterial, idShohin: 0, su: 1, kingaku: 200));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.IsTrue(result.DetailRows.All(r => r.Severity == EnumSundryCheckSeverity.Info));
		Assert.AreEqual(0, result.WarningCount);
		Assert.AreEqual(0, result.ErrorCount);
	}

	[TestMethod]
	public void Preview_KingakuZero_IsWarning() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohinA, su: 1, kingaku: 1000);
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 0));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		var row = result.DetailRows.Single(r => r.Id_Material_Slip > 0 && r.Kingaku == 0);
		Assert.AreEqual(EnumSundryCheckSeverity.Warning, row.Severity);
	}

	[TestMethod]
	public void Preview_Kubun30Or99WithShohin_IsWarningNotError() {
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertMaterialHeader("20260910", 30, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));
		InsertMaterialHeader("20260911", 99, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.AreEqual(2, result.DetailRows.Count);
		Assert.IsTrue(result.DetailRows.All(r => r.Severity == EnumSundryCheckSeverity.Warning));
		Assert.AreEqual(0, result.ErrorCount);
	}

	[TestMethod]
	public void Preview_CostMethodLastPurchase_HasInfoMessage_CostMethodTotalAverage_DoesNot() {
		CreateSundryTables(costMethod: (int)EnumCostMethod.LastPurchase);
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohinA, su: 1, kingaku: 1000);
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));

		var resultLastPurchase = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));
		Assert.IsTrue(resultLastPurchase.InfoMessages.Any(m => m.Contains("最終仕入原価")));

		Db.Execute($"UPDATE {nameof(MasterSysman)} SET CostMethod=@0", (int)EnumCostMethod.TotalAverage);
		var resultTotalAverage = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));
		Assert.IsFalse(resultTotalAverage.InfoMessages.Any(m => m.Contains("最終仕入原価")));
	}

	[TestMethod]
	public void Preview_NoSundryLinesInMonth_IsInfoOnly() {
		CreateSundryTables();

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		Assert.AreEqual(0, result.ErrorCount);
		Assert.AreEqual(0, result.WarningCount);
		Assert.IsTrue(result.InfoMessages.Any(m => m.Contains("諸掛明細がありません")));
	}

	[TestMethod]
	public void Preview_SummaryRow_UsesSummaryStockSu_NotCumulativeSu() {
		// SummaryStock.CumulativeSuを汚しても結果が変わらないこと(=Suを使っていることの確認、設計書§6.2)
		CreateSundryTables();
		var idShiire = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idShohinA = InsertShohin("A1");
		InsertPurchase("20260910", 10, idShohinA, su: 3, kingaku: 3000);
		Db.Insert(new SummaryStock { SumMonth = "202608", Id_Soko = 1, Id_Shohin = idShohinA, Su = 7, CumulativeSu = 99999, Vdc = 1, Vdu = 1 });
		InsertMaterialHeader("20260910", 10, idShiire, NewMaterialLine(1, idMaterial, idShohinA, su: 1, kingaku: 100));

		var result = new CostUpdateDb(Db).PreviewSundryCharges(NewParam("202609"));

		var summary = result.SummaryRows.Single(r => r.Id_Shohin == idShohinA);
		Assert.AreEqual(3, summary.PurchaseQty);
		Assert.AreEqual(3000, summary.PurchaseAmount);
		Assert.AreEqual(7, summary.OpeningQty);
		Assert.AreEqual(1, summary.SundryCount);
		Assert.AreEqual(100, summary.SundryAmount);
	}
}
