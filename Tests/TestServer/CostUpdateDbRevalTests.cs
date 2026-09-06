using System;
using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 評価替え（原価4項目 詳細設計 §16、Step 8）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbCostTests"/>・<see cref="CostUpdateDbSundryTests"/>に合わせる。
/// テスト観点は設計書§16.12 T-R1〜T-R14を土台とする。
/// </summary>
[TestClass]
public class CostUpdateDbRevalTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostUpdateDbRevalTests-{Guid.NewGuid():N}";
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
		// マニュアル排他制御(設計書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`)が
		// ApplyRevaluation/CancelRevaluationで使うため、個々のテストのテーブル準備に関わらずここで作っておく。
		_db.CreateTable(typeof(SysSequence), true, false);
		_db.CreateTable(typeof(SysHistAutoexec), true, false);
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

	private void CreateRevalTables(int costMethod = (int)EnumCostMethod.Fixed, string fiscalStartDate = "19010101") {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.CreateTable(typeof(TranGenkaReval), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryKaiShi), true, false);
		// CreateTableはKeyDmlの一意索引を作らないため、UpsertGenkaRowsのON CONFLICTが参照する一意キーを明示的に作る
		// (CostUpdateDbCostTests.CreateCostTablesと同じ作法)
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
		// 末日締めにして対象月=暦月にする(既存テストと同じ作法。期間計算を単純にするため)
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = costMethod, FiscalStartDate = fiscalStartDate, Vdc = 1, Vdu = 1 });
	}

	private long InsertShohin(
		string code, int tankaGenka = 0, int isZaiko = 1, int purchaseType = (int)EnumPurchaseType.Normal, int tankaJodai = 0,
		string brandCd = "", string brandMei = "", string itemCd = "", string itemMei = "",
		string seasonCd = "", string seasonMei = "", string makerCd = "", string makerMei = "") {
		var shohin = new MasterShohin {
			Code = code, Name = $"商品{code}", TankaGenka = tankaGenka, IsZaiko = isZaiko, PurchaseType = purchaseType,
			TankaJodai = tankaJodai,
			VBrand = new CodeNameView(0, brandCd, brandMei),
			VItem = new CodeNameView(0, itemCd, itemMei),
			VSeason = new CodeNameView(0, seasonCd, seasonMei),
			VMaker = new CodeNameView(0, makerCd, makerMei),
			Vdc = 1, Vdu = 1,
		};
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertShain(string code = "E1") {
		var shain = new MasterShain { Code = code, Name = $"社員{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		return shain.Id;
	}

	private void InsertStock(string sumMonth, long idShohin, int su) {
		Db.Insert(new SummaryStock { SumMonth = sumMonth, Id_Soko = 1, Id_Shohin = idShohin, Su = su, Vdc = 1, Vdu = 1 });
	}

	private static CostRevaluationCondition NewCond(params (EnumCostRevalCondField Field, string From, string To)[] rows) => new() {
		Rows = [.. rows.Select(r => new CostRevaluationCondRow { FieldKind = (int)r.Field, CodeFrom = r.From, CodeTo = r.To })],
	};

	private static CostRevaluationParameter NewRevalParam(
		string targetMonth,
		EnumCostRevaluationMethod method,
		int ratePercent = 0,
		int fixedCost = 0,
		int roundingUnit = 1,
		EnumRounding rounding = EnumRounding.Round,
		EnumCostRevalApplyPoint applyPoint = EnumCostRevalApplyPoint.MonthEnd,
		EnumCostRevalGroupKey groupKey = EnumCostRevalGroupKey.Brand,
		CostRevaluationCondition? cond = null,
		long idShain = 0,
		string batchId = "R1",
		IReadOnlyDictionary<long, long>? confirmedShohinVdu = null,
		int? confirmedShimeBi = null,
		int? confirmedCostMethod = null) => new(
			targetMonth, applyPoint, cond ?? new CostRevaluationCondition(), groupKey, method,
			ratePercent, fixedCost, roundingUnit, rounding, idShain, batchId,
			confirmedShohinVdu, confirmedShimeBi, confirmedCostMethod);

	private TranGenka? FetchGenka(long idShohin, string sumMonth, int costMethod, int changeKind) =>
		Db.FirstOrDefault<TranGenka>(
			"WHERE Id_Shohin=@0 AND SumMonth=@1 AND CostMethod=@2 AND ChangeKind=@3", idShohin, sumMonth, costMethod, changeKind);

	private int TankaGenkaOf(long idShohin) => Db.FirstOrDefault<MasterShohin>("WHERE Id=@0", idShohin)!.TankaGenka;

	/// <summary>対象商品にCostMethod=0の基準行(設計書§2.6)を作り、ResolveCostAsOfがBeforeCostとして解決できるようにする。</summary>
	private void SeedBaseline(CostUpdateDb costUpdateDb, long idShain, params long[] idShohins) =>
		costUpdateDb.EnsureBaselineCostRows(idShohins, "base", idShain);

	// ------------------------------------------------------------------
	// T-R1〜T-R3: 計算式・端数
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewRevaluation_ByRate_T_R1() {
		// T-R1: 元原価3,000円・率70%・端数単位1・四捨五入 → 2,100円
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 3000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 70, idShain: idShain));

		var row = preview.DetailRows.Single(r => r.Id_Shohin == idShohin);
		Assert.IsTrue(row.IsTarget);
		Assert.AreEqual(2100, row.AfterCost);
		Assert.AreEqual(0, preview.ErrorCount);
	}

	[TestMethod]
	public void PreviewRevaluation_ByFixed_T_R2() {
		// T-R2: 金額一括指定1,000円。元原価3,525円→1,000円、元原価800円→対象外
		CreateRevalTables();
		var idShain = InsertShain();
		var idTarget = InsertShohin("A1", tankaGenka: 3525);
		var idExcluded = InsertShohin("A2", tankaGenka: 800);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idTarget, idExcluded);
		InsertStock("202609", idTarget, 1);
		InsertStock("202609", idExcluded, 1);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByFixed, fixedCost: 1000, idShain: idShain));

		var rowTarget = preview.DetailRows.Single(r => r.Id_Shohin == idTarget);
		var rowExcluded = preview.DetailRows.Single(r => r.Id_Shohin == idExcluded);
		Assert.IsTrue(rowTarget.IsTarget);
		Assert.AreEqual(1000, rowTarget.AfterCost);
		Assert.IsFalse(rowExcluded.IsTarget);
		Assert.AreEqual("引き下げにならない", rowExcluded.ExcludeReason);
		Assert.AreEqual(EnumCostCalcError.None, rowExcluded.Error);
	}

	[TestMethod]
	public void PreviewRevaluation_Rounding_T_R3() {
		// T-R3: 元原価3,333円・率90%(2,999.7円)・端数単位100 → 四捨五入3,000／切上3,000／切捨2,900
		CreateRevalTables();
		var idShain = InsertShain();
		var costUpdateDb = new CostUpdateDb(Db);

		foreach (var (rounding, expected) in new[] { (EnumRounding.Round, 3000), (EnumRounding.Ceiling, 3000), (EnumRounding.Floor, 2900) }) {
			var idShohin = InsertShohin($"R{(int)rounding}", tankaGenka: 3333);
			SeedBaseline(costUpdateDb, idShain, idShohin);
			InsertStock("202609", idShohin, 1);

			var preview = costUpdateDb.PreviewRevaluation(
				NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 90, roundingUnit: 100, rounding: rounding, idShain: idShain));

			var row = preview.DetailRows.Single(r => r.Id_Shohin == idShohin);
			Assert.AreEqual(expected, row.AfterCost, $"rounding={rounding}");
		}
	}

	// ------------------------------------------------------------------
	// T-R4: 冪等性
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_RerunWithSameValues_IsIdempotent_T_R4() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 3000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var param1 = NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 70, idShain: idShain, batchId: "R1");
		var result1 = costUpdateDb.ApplyRevaluation(param1);
		Assert.IsTrue(result1.IsSuccess);
		Assert.AreEqual(2100, TankaGenkaOf(idShohin));

		var param2 = NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 70, idShain: idShain, batchId: "R2");
		var result2 = costUpdateDb.ApplyRevaluation(param2);
		Assert.IsTrue(result2.IsSuccess);

		var genkaRows = Db.Fetch<TranGenka>($"WHERE Id_Shohin=@0 AND ChangeKind=@1", idShohin, (int)EnumCostChangeKind.Reval);
		Assert.AreEqual(1, genkaRows.Count);
		Assert.AreEqual(2100, genkaRows[0].AfterCost);
		Assert.AreEqual(2100, TankaGenkaOf(idShohin));
	}

	// ------------------------------------------------------------------
	// T-R5・T-R6・T-R6b: 対象外条件
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewRevaluation_ZeroOrNegativeStock_NotTarget_T_R5() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idZero = InsertShohin("A1", tankaGenka: 1000);
		var idNegative = InsertShohin("A2", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idZero, idNegative);
		InsertStock("202609", idZero, 0);
		InsertStock("202609", idNegative, -3);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		foreach (var id in new[] { idZero, idNegative }) {
			var row = preview.DetailRows.Single(r => r.Id_Shohin == id);
			Assert.IsFalse(row.IsTarget);
			Assert.AreEqual("在庫0", row.ExcludeReason);
		}
	}

	[TestMethod]
	public void PreviewRevaluation_IsZaikoZero_NotIncluded_T_R6() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000, isZaiko: 0);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsFalse(preview.DetailRows.Any(r => r.Id_Shohin == idShohin));
	}

	[TestMethod]
	public void PreviewRevaluation_ConsumptionPurchaseType_NotIncluded_EvenWithStock_T_R6b() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000, purchaseType: (int)EnumPurchaseType.Consumption);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5); // 在庫が残っていても対象外(設計書§16.5条件6)

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsFalse(preview.DetailRows.Any(r => r.Id_Shohin == idShohin));
	}

	// ------------------------------------------------------------------
	// T-R7: 原価0円商品は対象外・エラーで全体を止めない
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_BeforeCostZero_IsNotTarget_DoesNotBlockOthers_T_R7() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idNoCost = InsertShohin("A1", tankaGenka: 0); // 基準行を作らず履歴無し=BeforeCost解決結果0
		var idNormal = InsertShohin("A2", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idNormal); // idNoCostには基準行を作らない
		InsertStock("202609", idNoCost, 5);
		InsertStock("202609", idNormal, 5);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));
		var rowNoCost = preview.DetailRows.Single(r => r.Id_Shohin == idNoCost);
		Assert.IsFalse(rowNoCost.IsTarget);
		Assert.AreEqual("原価0", rowNoCost.ExcludeReason);
		Assert.AreEqual(EnumCostCalcError.None, rowNoCost.Error);
		Assert.AreEqual(0, preview.ErrorCount);

		var result = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));
		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(500, TankaGenkaOf(idNormal));
		Assert.AreEqual(0, TankaGenkaOf(idNoCost)); // 変更されない
	}

	// ------------------------------------------------------------------
	// T-R8: 評価替え後の総平均原価更新再実行でもTankaGenkaは評価替え値のまま
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_ThenTotalAverageRerun_TankaGenkaStaysAtRevalValue_T_R8() {
		CreateRevalTables(costMethod: (int)EnumCostMethod.TotalAverage);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1");
		var costUpdateDb = new CostUpdateDb(Db);

		var meisai = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = idShohin, Su = 10, Tanka = 100, Kingaku = 1000 } };
		var shiire = new Tran03Shiire { DenDay = "20260910", KakeDay = "20260910", IsStock = 1, IsPay = 1, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		shiire.Kubun = 10;
		Db.Insert(shiire);

		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(new CostUpdateParameter { TargetMonth = "202609", Id_Shain = idShain, BatchId = "TA1" }).IsSuccess);
		Assert.AreEqual(100, TankaGenkaOf(idShohin));

		InsertStock("202609", idShohin, 10);
		var revalResult = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain, batchId: "REVAL1"));
		Assert.IsTrue(revalResult.IsSuccess);
		Assert.AreEqual(50, TankaGenkaOf(idShohin));

		var rerun = costUpdateDb.ApplyTotalAverageCost(new CostUpdateParameter { TargetMonth = "202609", Id_Shain = idShain, BatchId = "TA2" });
		Assert.IsTrue(rerun.IsSuccess);
		Assert.AreEqual(50, TankaGenkaOf(idShohin));
	}

	// ------------------------------------------------------------------
	// T-R9: 取消
	// ------------------------------------------------------------------

	[TestMethod]
	public void CancelRevaluation_RevertsTankaGenka_T_R9() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var applyResult = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));
		Assert.IsTrue(applyResult.IsSuccess);
		Assert.AreEqual(500, TankaGenkaOf(idShohin));

		var header = Db.FirstOrDefault<TranGenkaReval>("WHERE BatchId=@0", "R1");
		Assert.IsNotNull(header);

		var cancelResult = costUpdateDb.CancelRevaluation(header!.Id, idShain);
		Assert.IsTrue(cancelResult.IsSuccess);
		Assert.AreEqual(1000, TankaGenkaOf(idShohin));
		Assert.IsNull(FetchGenka(idShohin, "202609", (int)EnumCostMethod.Fixed, (int)EnumCostChangeKind.Reval));

		var reloadedHeader = Db.FirstOrDefault<TranGenkaReval>("WHERE Id=@0", header.Id);
		Assert.AreEqual((int)EnumCostRevalStatus.Canceled, reloadedHeader!.Status); // ヘッダ行は監査のため残す
	}

	[TestMethod]
	public void CancelRevaluation_BlockedWhenNewerRevalExistsForSameProduct_T_R9() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);
		InsertStock("202610", idShohin, 5);

		var firstApply = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain, batchId: "R1"));
		Assert.IsTrue(firstApply.IsSuccess);
		var firstHeader = Db.FirstOrDefault<TranGenkaReval>("WHERE BatchId=@0", "R1");

		var secondApply = costUpdateDb.ApplyRevaluation(NewRevalParam("202610", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain, batchId: "R2"));
		Assert.IsTrue(secondApply.IsSuccess);

		var cancelResult = costUpdateDb.CancelRevaluation(firstHeader!.Id, idShain);

		Assert.IsFalse(cancelResult.IsSuccess);
		// 取消不可のため、対象商品のTankaGenkaは変わらない(202610=より新しい評価替えの値のまま)
		Assert.AreEqual(250, TankaGenkaOf(idShohin));
	}

	// ------------------------------------------------------------------
	// T-R10: 確認後の対象商品変化を検知して中断
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_ShohinChangedAfterConfirm_Aborts_T_R10() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var previewParam = NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain);
		var preview = costUpdateDb.PreviewRevaluation(previewParam);
		Assert.IsTrue(preview.ConfirmedShohinVdu.ContainsKey(idShohin));

		// 確認後に対象商品を直接更新する(例: 別画面での編集を模擬)
		var shohin = Db.FirstOrDefault<MasterShohin>("WHERE Id=@0", idShohin)!;
		shohin.Vdu += 1;
		Db.Update(shohin, ["Vdu"]);

		var applyParam = previewParam with {
			ConfirmedShohinVdu = preview.ConfirmedShohinVdu,
			ConfirmedShimeBi = preview.ConfirmedShimeBi,
			ConfirmedCostMethod = preview.ConfirmedCostMethod,
		};
		var result = costUpdateDb.ApplyRevaluation(applyParam);

		Assert.IsFalse(result.IsSuccess);
		StringAssert.Contains(result.Message, "確認後に対象商品が更新されました");
		Assert.AreEqual(0, Db.Fetch<TranGenka>($"WHERE ChangeKind=@0", (int)EnumCostChangeKind.Reval).Count);
	}

	// ------------------------------------------------------------------
	// T-R11: 集計行の合計と品番単位丸めの整合
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewRevaluation_SummaryTotal_UsesPerProductRounding_T_R11() {
		// 品番単位で丸めてから合計する(設計書§13 U-22)。旧実装(在庫金額へ一括で率を掛けて丸める)方式なら
		// 204*0.5=102円になるところ、品番単位丸めでは51+52=103円になり、意図的にずれる。
		CreateRevalTables();
		var idShain = InsertShain();
		var idA = InsertShohin("A1", tankaGenka: 101, tankaJodai: 200, brandCd: "B1", brandMei: "ブランド1");
		var idB = InsertShohin("A2", tankaGenka: 103, tankaJodai: 210, brandCd: "B1", brandMei: "ブランド1");
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idA, idB);
		InsertStock("202609", idA, 1);
		InsertStock("202609", idB, 1);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		var rowA = preview.DetailRows.Single(r => r.Id_Shohin == idA);
		var rowB = preview.DetailRows.Single(r => r.Id_Shohin == idB);
		Assert.AreEqual(51, rowA.AfterCost); // 101*0.5=50.5 → 四捨五入(絶対値が大きい方へ) → 51
		Assert.AreEqual(52, rowB.AfterCost); // 103*0.5=51.5 → 52

		Assert.AreEqual(1, preview.SummaryRows.Count);
		Assert.AreEqual(103, preview.SummaryRows[0].AfterAmount); // 51+52。204*0.5=102(旧実装方式)とは一致しない
		Assert.AreEqual(103, preview.Total.AfterAmount);
		Assert.AreEqual(204, preview.Total.BeforeAmount);
		Assert.AreEqual(410, preview.Total.JodaiAmount);
		Assert.AreEqual(preview.Total.BeforeAmount - preview.Total.AfterAmount, preview.Total.DiffAmount);
	}

	// ------------------------------------------------------------------
	// T-R12: 大量商品を1トランザクションで更新する(性能・§16.10)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_LargeProductSet_CompletesInOneTransaction_T_R12() {
		// 設計書§16.12は78,932件(全MasterShohin)での完了を求めるが、単体テストとしては
		// IdChunkSize(1000件)の境界をまたぐ規模(1,500件)に縮小し、同じ一括upsert・一括UPDATE経路を検証する。
		CreateRevalTables();
		var idShain = InsertShain();
		const int count = 1500;
		var ids = new List<long>(count);
		for (var i = 0; i < count; i++) {
			ids.Add(InsertShohin($"L{i:D5}", tankaGenka: 1000));
		}
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, [.. ids]);
		foreach (var id in ids) {
			InsertStock("202609", id, 1);
		}

		var result = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(count, result.UpdatedCount);
		Assert.AreEqual(count, Db.Fetch<TranGenka>($"WHERE ChangeKind=@0", (int)EnumCostChangeKind.Reval).Count);
	}

	// ------------------------------------------------------------------
	// T-R13: 適用時点=期末
	// ------------------------------------------------------------------

	[TestMethod]
	public void ResolveFiscalYearEndMonth_FixedExamples() {
		// 設計書§16.4の2例をテストで固定する。現在時刻に依存しない純粋な年月演算のみを検証する。
		Assert.AreEqual("202703", CostUpdateDb.ResolveFiscalYearEndMonth("202608", fiscalStartMonth: 4));
		Assert.AreEqual("202703", CostUpdateDb.ResolveFiscalYearEndMonth("202702", fiscalStartMonth: 4));
	}

	[TestMethod]
	public void PreviewRevaluation_FiscalEnd_ResolvesSumMonthAndEffectiveDay_T_R13() {
		// 未来月判定に現在時刻の実際の値が影響しないよう、確実に過去となる会計年度(2000年度)で検証する。
		CreateRevalTables(fiscalStartDate: "20000401");
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("200103", idShohin, 5); // 決算期末月(2001年3月)末の在庫

		var preview = costUpdateDb.PreviewRevaluation(
			NewRevalParam("200008", EnumCostRevaluationMethod.ByRate, ratePercent: 50, applyPoint: EnumCostRevalApplyPoint.FiscalEnd, idShain: idShain));

		var row = preview.DetailRows.Single(r => r.Id_Shohin == idShohin);
		Assert.IsTrue(row.IsTarget);

		var applyResult = costUpdateDb.ApplyRevaluation(
			NewRevalParam("200008", EnumCostRevaluationMethod.ByRate, ratePercent: 50, applyPoint: EnumCostRevalApplyPoint.FiscalEnd, idShain: idShain));
		Assert.IsTrue(applyResult.IsSuccess);

		var genka = FetchGenka(idShohin, "200103", (int)EnumCostMethod.Fixed, (int)EnumCostChangeKind.Reval);
		Assert.IsNotNull(genka);
		Assert.AreEqual("20010331", genka!.EffectiveDay); // 末日締めの2001年3月末
	}

	[TestMethod]
	public void PreviewRevaluation_FiscalEnd_FutureMonth_IsInputError() {
		CreateRevalTables(fiscalStartDate: "20260401");
		var idShain = InsertShain();
		var costUpdateDb = new CostUpdateDb(Db);

		// 年3000という、実行時の実時計がどの値であっても確実に未来となる計上月を使う
		var preview = costUpdateDb.PreviewRevaluation(
			NewRevalParam("300008", EnumCostRevaluationMethod.ByRate, ratePercent: 50, applyPoint: EnumCostRevalApplyPoint.FiscalEnd, idShain: idShain));

		Assert.AreEqual(1, preview.ErrorCount);
		Assert.IsTrue(preview.InfoMessages.Any(m => m.Contains("未来月")));
		Assert.AreEqual(0, preview.DetailRows.Count);
	}

	// ------------------------------------------------------------------
	// T-R14・抽出条件
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewRevaluation_EmptyCondition_IncludesAllStockedProducts_T_R14() {
		CreateRevalTables();
		var idShain = InsertShain();
		var id1 = InsertShohin("A1", tankaGenka: 1000, brandCd: "B1");
		var id2 = InsertShohin("A2", tankaGenka: 1000, brandCd: "B2");
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, id1, id2);
		InsertStock("202609", id1, 1);
		InsertStock("202609", id2, 1);

		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsTrue(preview.DetailRows.Any(r => r.Id_Shohin == id1));
		Assert.IsTrue(preview.DetailRows.Any(r => r.Id_Shohin == id2));
	}

	[TestMethod]
	public void PreviewRevaluation_BrandRangeCondition_FiltersProducts() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idB1 = InsertShohin("A1", tankaGenka: 1000, brandCd: "B1");
		var idB2 = InsertShohin("A2", tankaGenka: 1000, brandCd: "B2");
		var idB3 = InsertShohin("A3", tankaGenka: 1000, brandCd: "B3");
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idB1, idB2, idB3);
		InsertStock("202609", idB1, 1);
		InsertStock("202609", idB2, 1);
		InsertStock("202609", idB3, 1);

		var cond = NewCond((EnumCostRevalCondField.Brand, "B1", "B2"));
		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, cond: cond, idShain: idShain));

		Assert.IsTrue(preview.DetailRows.Any(r => r.Id_Shohin == idB1));
		Assert.IsTrue(preview.DetailRows.Any(r => r.Id_Shohin == idB2));
		Assert.IsFalse(preview.DetailRows.Any(r => r.Id_Shohin == idB3));
	}

	[TestMethod]
	public void PreviewRevaluation_ShohinCodeRangeCondition_FiltersProducts() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idA1 = InsertShohin("A1", tankaGenka: 1000);
		var idA9 = InsertShohin("A9", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idA1, idA9);
		InsertStock("202609", idA1, 1);
		InsertStock("202609", idA9, 1);

		var cond = NewCond((EnumCostRevalCondField.ShohinCode, "A1", "A1"));
		var preview = costUpdateDb.PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, cond: cond, idShain: idShain));

		Assert.IsTrue(preview.DetailRows.Any(r => r.Id_Shohin == idA1));
		Assert.IsFalse(preview.DetailRows.Any(r => r.Id_Shohin == idA9));
	}

	// ------------------------------------------------------------------
	// AfterCost<=0のエラー行が1件でもあれば全体を更新しない
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_AfterCostZeroError_BlocksEntireUpdate() {
		CreateRevalTables();
		var idShain = InsertShain();
		// rate=1%、端数単位100・切捨てにすると raw=5*0.01=0.05→scaled=0.0005→floor=0→after=0(エラー)
		var idError = InsertShohin("A1", tankaGenka: 5);
		// 同じ条件でも前原価が十分大きければ0円にならない(raw=20000*0.01=200→scaled=2.0→floor*100=200)
		var idNormal = InsertShohin("A2", tankaGenka: 20000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idError, idNormal);
		InsertStock("202609", idError, 1);
		InsertStock("202609", idNormal, 1);

		var param = NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 1, roundingUnit: 100, rounding: EnumRounding.Floor, idShain: idShain);
		var preview = costUpdateDb.PreviewRevaluation(param);
		Assert.AreEqual(1, preview.ErrorCount);
		var errorRow = preview.DetailRows.Single(r => r.Id_Shohin == idError);
		Assert.AreEqual(EnumCostCalcError.NonPositiveAfterCost, errorRow.Error);

		var result = costUpdateDb.ApplyRevaluation(param);
		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>($"WHERE ChangeKind=@0", (int)EnumCostChangeKind.Reval).Count);
		Assert.AreEqual(20000, TankaGenkaOf(idNormal)); // 正常行も含め、全体が更新されない
	}

	// ------------------------------------------------------------------
	// CostMethod=0(固定原価)でも実行できる(設計書§16.8、§13 U-20)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_WorksRegardlessOfCostMethod_Fixed() {
		CreateRevalTables(costMethod: (int)EnumCostMethod.Fixed);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);

		var result = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(500, TankaGenkaOf(idShohin));
	}

	// ------------------------------------------------------------------
	// 対象計上月が支払計算済みなら中断する(設計書§4.6・§16.9)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyRevaluation_PeriodAlreadyPaid_Throws() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		InsertStock("202609", idShohin, 5);
		Db.Insert(new SummaryKaiShi { Id_Shiire = 1, DenDay = "20260930", DayFrom = "20260901", DayTo = "20260930", Vdc = 1, Vdu = 1 });

		Assert.ThrowsExactly<CostRevaluationPaidPeriodException>(
			() => costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain)));
		Assert.AreEqual(0, Db.Fetch<TranGenka>($"WHERE ChangeKind=@0", (int)EnumCostChangeKind.Reval).Count);
	}

	// ------------------------------------------------------------------
	// 条件一致0件・対象0件のメッセージ
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewRevaluation_NoConditionMatch_ShowsNoDataMessage() {
		CreateRevalTables();
		var idShain = InsertShain();
		InsertShohin("A1", tankaGenka: 1000, isZaiko: 0); // IsZaiko=0のため抽出条件(IsZaiko=1)に一致しない

		var preview = new CostUpdateDb(Db).PreviewRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsTrue(preview.InfoMessages.Any(m => m.Contains("データが存在しません")));
		Assert.AreEqual(0, preview.DetailRows.Count);
	}

	[TestMethod]
	public void ApplyRevaluation_MatchedButNoTarget_ReturnsFailureWithReasonBreakdown() {
		CreateRevalTables();
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 1000);
		var costUpdateDb = new CostUpdateDb(Db);
		SeedBaseline(costUpdateDb, idShain, idShohin);
		// 在庫を積まない → 条件一致1件だが対象0件

		var result = costUpdateDb.ApplyRevaluation(NewRevalParam("202609", EnumCostRevaluationMethod.ByRate, ratePercent: 50, idShain: idShain));

		Assert.IsFalse(result.IsSuccess);
		StringAssert.Contains(result.Message, "更新対象がありませんでした");
		StringAssert.Contains(result.Message, "在庫0");
	}
}
