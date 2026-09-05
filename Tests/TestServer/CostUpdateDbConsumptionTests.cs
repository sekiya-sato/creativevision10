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
/// 消化仕入更新（原価4項目 詳細設計 §4、Step 5）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbTests"/>・<see cref="SummaryDbTests"/>に合わせる。
/// </summary>
[TestClass]
public class CostUpdateDbConsumptionTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostUpdateDbConsumptionTests-{Guid.NewGuid():N}";
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

	private void CreateConsumptionTables() {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		Db.CreateTable(typeof(MasterShiire), true, false);
		Db.CreateTable(typeof(MasterMeisho), true, false);
		// CreateSummaryStockSql/CreateRealStockSqlがLEFT JOINで参照するため必要(在庫集計のIsZaiko判定)
		Db.CreateTable(typeof(MasterTokui), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(Tran01Tenuri), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(Tran07Shiharai), true, false);
		Db.CreateTable(typeof(SummaryKaiKake), true, false);
		Db.CreateTable(typeof(SummaryKaiShi), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryRealStock), true, false);
		Db.CreateTable(typeof(TranConsumptionPurchaseLink), true, false);
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
		// CreateTableはKeyDmlの一意索引を作らないため、ON CONFLICTが参照する一意キーを明示的に作る(SummaryDbTests.PrepareStockTablesと同じ作法)
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		// 末日締めにして対象月=暦月にする(テストの期間計算を単純にするため)
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = 0, Vdc = 1, Vdu = 1 });
	}

	private long InsertConsumptionShohin(
		string code, long idConsignmentShiire, EnumConsumptionCalcType calcType,
		int rateBp = 0, int roundingUnit = 1, EnumRounding rounding = EnumRounding.Round,
		int tankaShiire = 0, int tankaGenka = 0, long idTax = 1) {
		var shohin = new MasterShohin {
			Code = code,
			Name = $"商品{code}",
			IsZaiko = 1,
			PurchaseType = (int)EnumPurchaseType.Consumption,
			Id_ConsignmentShiire = idConsignmentShiire,
			ConsumptionCalcType = (int)calcType,
			ConsumptionRateBasisPoints = rateBp,
			ConsumptionRoundingUnit = roundingUnit,
			ConsumptionRounding = (int)rounding,
			TankaShiire = tankaShiire,
			TankaGenka = tankaGenka,
			Id_Tax = idTax,
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertNormalShohin(string code) {
		var shohin = new MasterShohin { Code = code, Name = $"商品{code}", IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Normal, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertShiire(string code) {
		var shiire = new MasterShiire { Code = code, Name = $"仕入先{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shiire);
		return shiire.Id;
	}

	private long InsertShain(string code) {
		var shain = new MasterShain { Code = code, Name = $"社員{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		return shain.Id;
	}

	private long InsertUriage(string denDay, int kubun, long idSoko, params Tran99Meisai[] meisai) {
		var header = new Tran00Uriage { DenDay = denDay, KakeDay = denDay, Id_Soko = idSoko, Jmeisai = [.. meisai], Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private static Tran99Meisai NewLine(int no, long idShohin, int su, int tanka, int jodai = 0) => new() {
		No = no,
		Id_Shohin = idShohin,
		Su = su,
		Tanka = tanka,
		Jodai = jodai,
		Kingaku = (long)su * tanka,
	};

	private static CostUpdateParameter NewParam(string targetMonth, long idShain, string batchId = "batch-1") => new() {
		TargetMonth = targetMonth,
		ProcessKind = EnumCostProcessKind.ConsumptionPurchase,
		Id_Shain = idShain,
		BatchId = batchId,
	};

	// ------------------------------------------------------------------
	// 作業A回帰: 在庫除外・買掛計上(§11.5 T-03)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Apply_GeneratesPurchase_ExcludedFromStock_IncludedInKaiKake() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 3, tanka: 1000));

		var costUpdateDb = new CostUpdateDb(Db);
		var result = costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual(0, generated[0].IsStock);
		Assert.AreEqual(3, generated[0].Jmeisai![0].Su);
		Assert.AreEqual(500, generated[0].Jmeisai![0].Tanka);

		// 在庫は増減しない(作業A: CreateSummaryStockSql/CreateRealStockSqlのIsStock=1条件)
		new SummaryDb(Db).CalcTran2SummaryStock(nameof(Tran03Shiire), "Id_Soko", generated[0].Id, false);
		Assert.AreEqual(0, Db.Fetch<SummaryStock>("WHERE Id_Shohin=@0", idShohin).Count);
		Assert.AreEqual(0, Db.Fetch<SummaryRealStock>("WHERE Id_Shohin=@0", idShohin).Count);

		// 買掛には積まれる(IsStock=0でもIsPayを条件にする既存の買掛集計は素通しする)
		var kaikake = Db.Fetch<SummaryKaiKake>("WHERE Id_Shiire=@0", idShiire);
		Assert.AreEqual(1, kaikake.Count);
		Assert.AreEqual(1500, kaikake[0].Shiire);
	}

	// ------------------------------------------------------------------
	// 計算区分0/1
	// ------------------------------------------------------------------

	[TestMethod]
	public void Preview_CalcTypeCostBased_UsesTankaShiireWhenPositive() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 700, tankaGenka: 300);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 2, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual(EnumCostCalcError.None, rows[0].Error);
		Assert.AreEqual(700, rows[0].UnitCost);
	}

	[TestMethod]
	public void Preview_CalcTypeCostBased_FallsBackToResolveCostAsOf_ThenTankaGenka() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 0, tankaGenka: 300);
		var idShain = InsertShain("E1");
		// 履歴が無い場合はTankaGenkaへフォールバックする(設計書§4.4)
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual(300, rows[0].UnitCost);

		// TranGenkaに履歴があればそちらを優先する
		Db.Insert(new TranGenka {
			BatchId = "b", SumMonth = "202608", EffectiveDay = "20260820",
			CostMethod = (int)EnumCostMethod.Fixed, ChangeKind = 0, Id_Shohin = idShohin, AfterCost = 555, Vdc = 1, Vdu = 1,
		});
		var rows2 = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));
		Assert.AreEqual(555, rows2[0].UnitCost);
	}

	[TestMethod]
	public void Preview_CalcTypeRateBased_MatchesCostCalculator() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.RateBased, rateBp: 6500, roundingUnit: 10, rounding: EnumRounding.Floor);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1999));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		var expected = CostCalculator.CalcConsumptionUnitCostByRate(1999, 6500, 10, EnumRounding.Floor);
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual(expected.AfterCost, rows[0].UnitCost);
	}

	// ------------------------------------------------------------------
	// 生成単位(§4.5)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Apply_SameHeaderDifferentConsignmentShiire_SplitsIntoSeparateGeneratedPurchases() {
		CreateConsumptionTables();
		var idShiire1 = InsertShiire("SR1");
		var idShiire2 = InsertShiire("SR2");
		var idShohin1 = InsertConsumptionShohin("C1", idShiire1, EnumConsumptionCalcType.CostBased, tankaShiire: 100);
		var idShohin2 = InsertConsumptionShohin("C2", idShiire2, EnumConsumptionCalcType.CostBased, tankaShiire: 200);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin1, su: 1, tanka: 1000), NewLine(2, idShohin2, su: 1, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		Assert.AreEqual(2, result.UpdatedCount);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(2, generated.Count);
	}

	[TestMethod]
	public void Apply_PositiveAndNegativeKubun_ProducesSeparateHeadersWithExpectedSign() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 100);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 2, tanka: 1000)); // 売上→仕入生成
		InsertUriage("20260911", 20, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000)); // 返品→仕入返品生成

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(2, generated.Count);
		Assert.IsTrue(generated.Any(g => g.Kubun == (int)EnumShiire.Shiire && g.CalcFlag == 1));
		Assert.IsTrue(generated.Any(g => g.Kubun == (int)EnumShiire.Henpin && g.CalcFlag == -1));
	}

	// ------------------------------------------------------------------
	// 再実行(§4.6)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Apply_RunTwiceWithoutChange_IsIdempotent() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 3, tanka: 1000));

		var costUpdateDb = new CostUpdateDb(Db);
		var first = costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));
		Assert.IsTrue(first.IsSuccess, first.Message);
		var second = costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));
		Assert.IsTrue(second.IsSuccess, second.Message);

		Assert.AreEqual(1, Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase).Count);
		Assert.AreEqual(1, Db.Fetch<TranConsumptionPurchaseLink>("").Count);
	}

	[TestMethod]
	public void Apply_SourceSalesModified_RegeneratesWithNewContent() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		var uriageId = InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 3, tanka: 1000));

		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		// 元売上の数量を変更してVduを進める
		var uriage = Db.Single<Tran00Uriage>("WHERE Id=@0", uriageId);
		uriage.Jmeisai![0].Su = 9;
		uriage.Vdu += 1;
		Db.Update(uriage);

		var result = costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain, "batch-2"));
		Assert.IsTrue(result.IsSuccess, result.Message);

		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual(9, generated[0].Jmeisai![0].Su);
	}

	[TestMethod]
	public void Apply_SourceSalesDeleted_RemovesOldGeneratedPurchase() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		var uriageId = InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 3, tanka: 1000));

		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));
		Assert.AreEqual(1, Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase).Count);

		Db.Delete<Tran00Uriage>("WHERE Id=@0", uriageId);
		var result = costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain, "batch-2"));

		Assert.IsTrue(result.IsSuccess, result.Message);
		Assert.AreEqual(0, Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase).Count);
		Assert.AreEqual(0, Db.Fetch<TranConsumptionPurchaseLink>("").Count);
	}

	// ------------------------------------------------------------------
	// 支払計算済みの中断(§4.6)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Apply_PeriodAlreadyPaid_ThrowsAndChangesNothing() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 3, tanka: 1000));
		// 対象期間(202609、末日締め=20260901-20260930)と重なる支払計算済み範囲を用意する
		Db.Insert(new SummaryKaiShi { Id_Shiire = idShiire, DenDay = "20260930", DayFrom = "20260901", DayTo = "20260930", Vdc = 1, Vdu = 1 });

		var costUpdateDb = new CostUpdateDb(Db);
		Assert.ThrowsExactly<ConsumptionPurchasePaidPeriodException>(() => costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain)));

		Assert.AreEqual(0, Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase).Count);
		Assert.AreEqual(0, Db.Fetch<TranConsumptionPurchaseLink>("").Count);
	}

	// ------------------------------------------------------------------
	// エラー時は全件ロールバック(§2.4-2、§10.2)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Apply_AnyErrorRow_ChangesNothing() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var okShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		// 消化仕入先が未設定の不正な商品
		var badShohin = InsertConsumptionShohin("C2", idConsignmentShiire: 0, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, okShohin, su: 1, tanka: 1000));
		InsertUriage("20260911", 10, idSoko: 1, NewLine(1, badShohin, su: 1, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(1, result.ErrorCount);
		Assert.AreEqual(0, Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase).Count);
		Assert.AreEqual(0, Db.Fetch<TranConsumptionPurchaseLink>("").Count);
	}

	// ------------------------------------------------------------------
	// §4.8 エラー条件
	// ------------------------------------------------------------------

	[TestMethod]
	public void Preview_ConsignmentShiireNotSet_IsError() {
		CreateConsumptionTables();
		var idShohin = InsertConsumptionShohin("C1", idConsignmentShiire: 0, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreNotEqual(string.Empty, rows[0].ErrorMessage);
	}

	[TestMethod]
	public void Preview_RateOutOfRange_IsInvalidRateError() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.RateBased, rateBp: 0);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual(EnumCostCalcError.InvalidRate, rows[0].Error);
	}

	[TestMethod]
	public void Preview_NonPositiveQuantity_IsError() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 0, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreNotEqual(string.Empty, rows[0].ErrorMessage);
	}

	[TestMethod]
	public void Preview_ShohinIdZero_IsErrorWhenHeaderHasConsumptionLine() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		// 商品ID=0の壊れた行を、消化仕入商品の行と同じヘッダに混在させる
		InsertUriage("20260910", 10, idSoko: 1,
			NewLine(1, idShohin, su: 1, tanka: 1000),
			new Tran99Meisai { No = 2, Id_Shohin = 0, Su = 1, Tanka = 100, Kingaku = 100 });

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(2, rows.Count);
		Assert.IsTrue(rows.Any(r => r.SourceLineNo == 2 && !string.IsNullOrEmpty(r.ErrorMessage)));
	}

	[TestMethod]
	public void Preview_InvalidJson_IsError() {
		CreateConsumptionTables();
		var idShain = InsertShain("E1");
		Db.Execute($"INSERT INTO {nameof(Tran00Uriage)} (DenDay, Kubun, Jmeisai, Vdc, Vdu) VALUES (@0, @1, @2, 1, 1)",
			"20260910", 10, "not-json");

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreNotEqual(string.Empty, rows[0].ErrorMessage);
	}

	[TestMethod]
	public void Preview_NormalProductLine_IsIgnoredNotError() {
		CreateConsumptionTables();
		var idNormalShohin = InsertNormalShohin("N1");
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idNormalShohin, su: 1, tanka: 1000));

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(0, rows.Count);
	}

	[TestMethod]
	public void Preview_DiscountKubunWithConsumptionProduct_IsError() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 30, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000)); // Kubun=30 値引

		var rows = new CostUpdateDb(Db).PreviewConsumptionPurchases(NewParam("202609", idShain));

		Assert.AreEqual(1, rows.Count);
		Assert.AreNotEqual(string.Empty, rows[0].ErrorMessage);
	}

	// ------------------------------------------------------------------
	// 月次状態(§2.5.6、B-7)
	// ------------------------------------------------------------------

	[TestMethod]
	public void FetchCostMonthStatus_Consumption_NoLinks_ReturnsNotRun() {
		CreateConsumptionTables();
		var status = new CostUpdateDb(Db).FetchCostMonthStatus("202609", EnumCostProcessKind.ConsumptionPurchase);
		Assert.AreEqual(EnumCostProcessStatus.NotRun, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_Consumption_AfterApply_ReturnsCompleted() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));
		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.ConsumptionPurchase);

		Assert.AreEqual(EnumCostProcessStatus.Completed, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_Consumption_NewSalesAdded_ReturnsRerunRequired() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));
		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		InsertUriage("20260911", 10, idSoko: 1, NewLine(1, idShohin, su: 2, tanka: 1000));

		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.ConsumptionPurchase);
		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_Consumption_SourceSalesDeleted_ReturnsRerunRequired() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		var uriageId = InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));
		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		Db.Delete<Tran00Uriage>("WHERE Id=@0", uriageId);

		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.ConsumptionPurchase);
		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	[TestMethod]
	public void FetchCostMonthStatus_Consumption_SourceSalesUpdated_ReturnsRerunRequired() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		var uriageId = InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));
		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));

		var uriage = Db.Single<Tran00Uriage>("WHERE Id=@0", uriageId);
		uriage.Vdu += 1;
		Db.Update(uriage);

		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.ConsumptionPurchase);
		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	// ------------------------------------------------------------------
	// 原価更新への無効化連鎖(§2.5.6手順3、§7)
	// ------------------------------------------------------------------

	[TestMethod]
	public void FetchCostMonthStatus_CostUpdate_ConsumptionRerunRequired_AlsoBecomesRerunRequired() {
		CreateConsumptionTables();
		Db.CreateTable(typeof(SummaryStock), true, false);
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));
		var costUpdateDb = new CostUpdateDb(Db);
		costUpdateDb.ApplyConsumptionPurchases(NewParam("202609", idShain));
		// 原価更新側にも成果行を作り、それ単体では完了扱いになる状態にしておく
		Db.Insert(new TranGenka {
			BatchId = "b", SumMonth = "202609", EffectiveDay = "20260920",
			CostMethod = 0, ChangeKind = 0, Id_Shohin = idShohin, AfterCost = 500, Vdc = 1, Vdu = 999999,
		});

		// 消化仕入対象売上を後から追加し、消化仕入側を再実行要にする
		InsertUriage("20260911", 10, idSoko: 1, NewLine(1, idShohin, su: 2, tanka: 1000));

		var status = costUpdateDb.FetchCostMonthStatus("202609", EnumCostProcessKind.CostUpdate);
		Assert.AreEqual(EnumCostProcessStatus.RerunRequired, status.Status);
	}

	// ------------------------------------------------------------------
	// 税(§4.7): TaxCalculator.Applyは生成単位(ヘッダ1件)ごとに1回だけ呼ぶ
	// ------------------------------------------------------------------

	/// <summary>
	/// 指定した消費税区分の税率(%)をMasterSysman.Jsubへ設定する。<c>DateFrom</c>は空文字にして、
	/// <see cref="TaxRateResolver"/>が新税率(<c>TaxNewRate</c>、既定0)へ読み替える判定
	/// (DateFromの既定値"19010101"は妥当な日付のため、素で設定すると常に新税率側に倒れてしまう)を避ける。
	/// <see cref="HhtProcessUpdateTests"/>が使っているのと同じ回避策。
	/// </summary>
	private void SetTaxRates(params (long idTax, int ratePercent)[] rates) {
		var sysman = Db.Single<MasterSysman>("where Id=1");
		sysman.Jsub = [.. rates.Select(r => new MasterSysTax { Id = r.idTax, TaxRate = r.ratePercent, DateFrom = string.Empty })];
		Db.Update(sysman);
	}

	[TestMethod]
	public void Apply_SlipTaxUnit_HeaderRounding_DiffersFromPerLineRounding() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		// 仕入先マスタを伝票単位(§4.7 EnumTaxCalcUnit.Slip)にする
		var shiire = Db.Single<MasterShiire>("WHERE Id=@0", idShiire);
		shiire.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		Db.Update(shiire);
		// Id_Taxは既定(1)。MasterSysman.Jsubを設定していないためTaxRateResolverはDefaultTaxRatePercent=10%を返す
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 105);
		var idShain = InsertShain("E1");
		// 同一の生成単位(同一売上ヘッダ・同一仕入先・同一倉庫・同一正負)に落ちる明細を2行用意する。
		// 105円は10%課税でちょうど10.5円になり、四捨五入の境界(端数=50/100)にちょうど乗る値である。
		// 明細ごとに丸めると 105*10%→10.5→四捨五入で11円 が2回発生し、合計は22円になる。
		// 一方、TaxCalculator.Applyは伝票単位でヘッダ1件分の明細をまとめてから1回だけ丸めるため、
		// 210*10%=21.0(端数なし)がそのまま21円になる。21(伝票単位)と22(明細ごとの合計)が一致しない
		// ことを狙って105円を選んでいる。もし実装が退行して明細ごとにApplyを呼ぶようになると、
		// このテストはTax1=22を観測して失敗する。
		InsertUriage("20260910", 10, idSoko: 1,
			NewLine(1, idShohin, su: 1, tanka: 1000),
			NewLine(2, idShohin, su: 1, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual(210L, generated[0].TaxableAmount1);
		// 手計算した定数でアサートする(TaxCalculator.Applyを呼んで期待値を得ると退行を検出できないため)
		Assert.AreEqual(21L, generated[0].Tax1);
	}

	[TestMethod]
	public void Apply_MixedTaxCategories_AggregatesTaxableAmountAndTaxPerCategory() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var shiire = Db.Single<MasterShiire>("WHERE Id=@0", idShiire);
		shiire.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		Db.Update(shiire);
		// Id_Tax=1→10%、Id_Tax=2→8%。既定のDefaultTaxRatePercent(10%)に両方が倒れないよう明示的に分ける
		SetTaxRates((1, 10), (2, 8));
		var idShohin1 = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 1000, idTax: 1);
		var idShohin2 = InsertConsumptionShohin("C2", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 500, idTax: 2);
		var idShain = InsertShain("E1");
		// 同一仕入先・同一ヘッダに税区分1と2の商品を混在させ、同一生成単位へ落とす
		InsertUriage("20260910", 10, idSoko: 1,
			NewLine(1, idShohin1, su: 1, tanka: 1000),
			NewLine(2, idShohin2, su: 1, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual(1000L, generated[0].TaxableAmount1);
		Assert.AreEqual(500L, generated[0].TaxableAmount2);
		Assert.AreEqual(0L, generated[0].TaxableAmount3);
		Assert.AreEqual(100L, generated[0].Tax1); // 1000 * 10%
		Assert.AreEqual(40L, generated[0].Tax2);  // 500 * 8%
		Assert.AreEqual(0L, generated[0].Tax3);
	}

	[TestMethod]
	public void Apply_PurchaseReturn_KeepsTaxPositiveWithNegativeHeaderCalcFlag() {
		CreateConsumptionTables();
		var idShiire = InsertShiire("SR1");
		var shiire = Db.Single<MasterShiire>("WHERE Id=@0", idShiire);
		shiire.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		Db.Update(shiire);
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 100);
		var idShain = InsertShain("E1");
		// Kubun=20(仕入返品)。数量は正値のまま保持し、正負はヘッダCalcFlagで表現する(設計書§4.3・§4.7末尾)
		InsertUriage("20260910", 20, idSoko: 1, NewLine(1, idShohin, su: 1, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual((int)EnumShiire.Henpin, generated[0].Kubun);
		Assert.AreEqual(-1, generated[0].CalcFlag);
		// 課税対象額・税額は符号を持たず正値のまま保持する。符号はヘッダCalcFlagだけで表現する
		Assert.AreEqual(100L, generated[0].TaxableAmount1);
		Assert.AreEqual(10L, generated[0].Tax1); // 100 * 10%
	}

	[TestMethod]
	public void Apply_BillingTaxUnit_LeavesTaxZero_AccumulatesTaxableAmountOnly() {
		CreateConsumptionTables();
		// TaxCalcUnitは既定のEnumTaxCalcUnit.Billing(0)のまま変更しない
		var idShiire = InsertShiire("SR1");
		var idShohin = InsertConsumptionShohin("C1", idShiire, EnumConsumptionCalcType.CostBased, tankaShiire: 1000);
		var idShain = InsertShain("E1");
		InsertUriage("20260910", 10, idSoko: 1, NewLine(1, idShohin, su: 2, tanka: 1000));

		var result = new CostUpdateDb(Db).ApplyConsumptionPurchases(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess, result.Message);
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual((int)EnumTaxCalcUnit.Billing, generated[0].TaxCalcUnit);
		// 請求単位: 税額は請求/支払計算側で確定するため0のまま(CvBase/TaxCalculator.cs:124のBilling分岐)
		Assert.AreEqual(0L, generated[0].Tax1);
		Assert.AreEqual(0L, generated[0].Tax2);
		Assert.AreEqual(0L, generated[0].Tax3);
		Assert.AreEqual(2000L, generated[0].TaxableAmount1);
	}
}
