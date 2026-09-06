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
/// 原価4項目（詳細設計 `Doc/spec/2026-09-05_原価4項目_詳細設計.md` §11.3）の通しUAT。
/// 設計書§11.3の7手順を1本の自動テストとして固定する。
/// <para>
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbCostTests"/>・<see cref="CostUpdateDbConsumptionTests"/>に合わせる。
/// 原価方式は総平均原価（<c>CostMethod=2</c>）で通す（設計書§3.6: 諸掛が原価に効くのはこちらのため）。
/// </para>
/// <para>
/// 期待値はすべてテスト内に手計算した定数として書く（実装の関数を呼んで期待値を作らない）。
/// 失敗した場合はテストの期待値ではなく実装が設計書どおりかを疑うこと。
/// </para>
/// </summary>
[TestClass]
public class CostFourItemsEndToEndTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"CostFourItemsEndToEndTests-{Guid.NewGuid():N}";
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

	private void CreateAllTables() {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		Db.CreateTable(typeof(MasterShiire), true, false);
		Db.CreateTable(typeof(MasterMaterial), true, false);
		Db.CreateTable(typeof(MasterMeisho), true, false);
		Db.CreateTable(typeof(MasterTokui), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(Tran01Tenuri), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran07Shiharai), true, false);
		Db.CreateTable(typeof(SummaryKaiKake), true, false);
		Db.CreateTable(typeof(SummaryKaiShi), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryRealStock), true, false);
		// SummaryAllAsyncStream(在庫Rebuild)がTranテーブル全種を横断してSQLを組み立てるため、
		// データが無くてもテーブル自体は必要(SummaryDbTests.PrepareAllStockTablesと同じ作法)。
		Db.CreateTable(typeof(Tran05Ido), true, false);
		Db.CreateTable(typeof(Tran10IdoOut), true, false);
		Db.CreateTable(typeof(Tran11IdoIn), true, false);
		Db.CreateTable(typeof(Tran60Tana), true, false);
		Db.CreateTable(typeof(Tran61Chosei), true, false);
		Db.CreateTable(typeof(TranHaibun), true, false);
		Db.CreateTable(typeof(TranConsumptionPurchaseLink), true, false);
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		// マニュアル排他制御(設計書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`)が
		// ApplyConsumptionPurchases/ApplyTotalAverageCostで使う。
		Db.CreateTable(typeof(SysSequence), true, false);
		Db.CreateTable(typeof(SysHistAutoexec), true, false);
		// 末日締め(ShimeBi=99)にして対象月=暦月にする(既存テストと同じ作法)。総平均原価方式(CostMethod=2)。
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = (int)EnumCostMethod.TotalAverage, Vdc = 1, Vdu = 1 });
	}

	private long InsertNormalShohin(string code, int tankaGenka) {
		var shohin = new MasterShohin { Code = code, Name = $"商品{code}", IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Normal, TankaGenka = tankaGenka, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertConsumptionShohin(string code, long idConsignmentShiire, int tankaShiire) {
		var shohin = new MasterShohin {
			Code = code, Name = $"商品{code}", IsZaiko = 1, PurchaseType = (int)EnumPurchaseType.Consumption,
			Id_ConsignmentShiire = idConsignmentShiire, ConsumptionCalcType = (int)EnumConsumptionCalcType.CostBased,
			ConsumptionRoundingUnit = 1, ConsumptionRounding = (int)EnumRounding.Round, TankaShiire = tankaShiire, Vdc = 1, Vdu = 1,
		};
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

	private void InsertOpeningStock(string sumMonth, long idShohin, int su) {
		Db.Insert(new SummaryStock { SumMonth = sumMonth, Id_Soko = 1, Id_Shohin = idShohin, Su = su, Vdc = 1, Vdu = 1 });
	}

	private long InsertPurchase(string denDay, int kubun, long idShohin, int su, long kingaku) {
		var meisai = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku } };
		var header = new Tran03Shiire { DenDay = denDay, KakeDay = denDay, Id_Soko = 1, IsStock = 1, IsPay = 1, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private long InsertMaterialHeader(string denDay, int kubun, long idShiire, long idMaterial, long idShohin, int su, long kingaku) {
		var meisai = new List<Tran99MaterialMeisai> { new() { No = 1, Id_Material = idMaterial, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku } };
		var header = new Tran02Material { DenDay = denDay, KakeDay = denDay, Id_Shiire = idShiire, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private long InsertUriage(string denDay, int kubun, long idSoko, long idShohin, int su, int tanka) {
		var meisai = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = idShohin, Su = su, Tanka = tanka, Kingaku = (long)su * tanka } };
		var header = new Tran00Uriage { DenDay = denDay, KakeDay = denDay, Id_Soko = idSoko, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private TranGenka? FetchGenka(long idShohin, string sumMonth, int changeKind = 0) =>
		Db.FirstOrDefault<TranGenka>(
			"WHERE Id_Shohin=@0 AND SumMonth=@1 AND CostMethod=@2 AND ChangeKind=@3", idShohin, sumMonth, (int)EnumCostMethod.TotalAverage, changeKind);

	private int TankaGenkaOf(long idShohin) => Db.FirstOrDefault<MasterShohin>("WHERE Id=@0", idShohin)!.TankaGenka;

	/// <summary>原価4処理が触る全テーブルのスナップショット(§11.3手順7の冪等性比較に使う)。</summary>
	private sealed record DbSnapshot(
		string TranGenka, string ConsumptionLink, string GeneratedShiire, string ShohinTankaGenka, string SummaryStock, string SummaryKaiKake);

	/// <summary>
	/// 原価4処理が触る全テーブルを、比較に関係しない揮発列(Id・Vdc・Vdu・作成日時等)を除いて
	/// 正規化した文字列へシリアライズする。2回の実行結果をこの文字列で単純比較し、1文字でも
	/// 違えば「差分あり」とみなす(設計書§11.3手順7「全テーブル差分が0件になることを確認する」)。
	/// </summary>
	private DbSnapshot TakeSnapshot() {
		string Rows<T>(string orderBy, Func<T, string> project) where T : new() =>
			string.Join("|", Db.Fetch<T>($"ORDER BY {orderBy}").Select(project));

		var tranGenka = Rows<TranGenka>("SumMonth,Id_Shohin,ChangeKind", r =>
			$"{r.SumMonth}/{r.Id_Shohin}/{r.CostMethod}/{r.ChangeKind}/{r.BeforeCost}/{r.AfterCost}/{r.PurchaseQty}/{r.PurchaseAmount}/{r.SundryAmount}/{r.EffectiveDay}");
		// GeneratedShiireIdは含めない: ApplyConsumptionPurchasesは毎回DeleteExistingGenerated→InsertGeneratedで
		// 対象期間の生成仕入を作り直す設計(設計書§4.6)のため、内容が完全に同一でも自動採番Idは再実行のたびに
		// 変わる。それ自体は冪等性違反ではない(生成仕入の内容はgeneratedShiireスナップショット側で別途比較する)。
		var link = Rows<TranConsumptionPurchaseLink>("SourceType,SourceId,SourceLineNo", r =>
			$"{r.SourceType}/{r.SourceId}/{r.SourceLineNo}/{r.Id_Shohin}/{r.Id_Shiire}/{r.GeneratedLineNo}");
		// 生成仕入もIdは自動採番のたびに変わるため、DenDay・Id_Shiireなど内容ベースで並べて比較する。
		var generatedShiire = Rows<Tran03Shiire>("DenDay,Id_Shiire,Kubun", r =>
			$"{r.GeneratedKind}/{r.Kubun}/{r.CalcFlag}/{r.Id_Shiire}/{r.DenDay}/{string.Join(",", r.Jmeisai?.Select(m => $"{m.Id_Shohin}:{m.Su}:{m.Tanka}:{m.Kingaku}") ?? [])}");
		var shohin = Rows<MasterShohin>("Id", r => $"{r.Id}/{r.TankaGenka}");
		var summaryStock = Rows<SummaryStock>("SumMonth,Id_Soko,Id_Shohin", r => $"{r.SumMonth}/{r.Id_Soko}/{r.Id_Shohin}/{r.Su}");
		var summaryKaiKake = Rows<SummaryKaiKake>("DenMonth,Id_Shiire", r => $"{r.DenMonth}/{r.Id_Shiire}/{r.Shiire}");

		return new DbSnapshot(tranGenka, link, generatedShiire, shohin, summaryStock, summaryKaiKake);
	}

	/// <summary>
	/// 設計書§7推奨順(消化仕入→諸掛確認→在庫Rebuild→原価更新)の1サイクルを実行する。
	/// 在庫Rebuildは<see cref="SummaryDb.CalcTran2SummaryStock"/>(1伝票ずつの差分加算、伝票保存時の
	/// リアルタイム更新用)ではなく、<see cref="SummaryDb.SummaryAllAsyncStream"/>(対象期間のSummaryStockを
	/// 一度削除してTranテーブルから再構築する、真の「Rebuild」)を使う。前者は複数回実行すると
	/// 二重加算されるため、手順7(冪等性)の検証には使えない(<see cref="SummaryDbTests"/>の
	/// <c>SummaryAllAsyncStream_RepeatedRebuild_IsIdempotentAndMatchesImmediateUpdate</c>が
	/// 同じ理由でこちらを使っている)。
	/// </summary>
	private async System.Threading.Tasks.Task RunFullCostCycle(string targetMonth, long idShain, string batchId) {
		var costUpdateDb = new CostUpdateDb(Db);
		var summaryDb = new SummaryDb(Db);

		// 1) 消化仕入更新: 消化仕入を生成し、買掛(SummaryKaiKake)も内部で更新する
		var consumptionResult = costUpdateDb.ApplyConsumptionPurchases(new CostUpdateParameter {
			TargetMonth = targetMonth, ProcessKind = EnumCostProcessKind.ConsumptionPurchase, Id_Shain = idShain, BatchId = batchId + "-C",
		});
		Assert.IsTrue(consumptionResult.IsSuccess, consumptionResult.Message);

		// 2) 諸掛確認: 更新前のエラー確認(DBは変更しない)
		var sundryPreview = costUpdateDb.PreviewSundryCharges(new CostUpdateParameter { TargetMonth = targetMonth });
		Assert.AreEqual(0, sundryPreview.ErrorCount, "諸掛確認でエラーが検出された");

		// 3) 在庫Rebuild: 対象期間のSummaryStock/SummaryRealStockをTranテーブルから再構築する
		//    (消化仕入で生成されたTran03ShiireはIsStock=0のためC-07のとおり積まれない)
		await foreach (var _ in summaryDb.SummaryAllAsyncStream(new CalcDateTermParameter(targetMonth, targetMonth))) {
			// 進捗イベントは使わない。列挙を最後まで回すことが目的。
		}

		// 4) 原価更新: 総平均原価
		var costResult = costUpdateDb.ApplyTotalAverageCost(new CostUpdateParameter { TargetMonth = targetMonth, Id_Shain = idShain, BatchId = batchId + "-T" });
		Assert.IsTrue(costResult.IsSuccess, costResult.Message);
	}

	// ------------------------------------------------------------------
	// 手順1〜5: 登録・4処理実行・手計算との突合
	// ------------------------------------------------------------------

	[TestMethod]
	public async System.Threading.Tasks.Task FullCycle_MatchesHandCalculatedExpectations() {
		CreateAllTables();
		var idShain = InsertShain();
		var idShiireConsignment = InsertShiire("SR1"); // 消化仕入先
		var idMaterial = InsertMaterial("M1");

		// 手順1: 通常商品と消化仕入商品を登録する
		var idNormal = InsertNormalShohin("NORMAL", tankaGenka: 5000); // 前月原価5,000円
		var idConsump = InsertConsumptionShohin("CONSUMP", idShiireConsignment, tankaShiire: 500);

		// 手順2: 前月在庫を作る(NORMAL: 10個×5,000円=50,000円。消化仕入商品は在庫対象外なので作らない)
		InsertOpeningStock("202608", idNormal, su: 10);

		// 手順3: 当月(202609、末日締め=20260901-20260930)に商品仕入・生地付属仕入(諸掛)・売上・返品を登録する
		// 商品仕入: 20個・100,000円(単価5,000)
		InsertPurchase("20260905", 10, idNormal, su: 20, kingaku: 100_000);
		// 仕入返品: 5個・25,000円(単価5,000)
		InsertPurchase("20260910", 20, idNormal, su: 5, kingaku: 25_000);
		// 生地付属仕入(諸掛): NORMALへ3明細 50+30+20=100円
		InsertMaterialHeader("20260906", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 50);
		InsertMaterialHeader("20260907", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 30);
		InsertMaterialHeader("20260908", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 20);
		// 卸売上: NORMAL 6個×8,000円=48,000円、消化仕入商品CONSUMP 3個×2,000円=6,000円
		InsertUriage("20260915", 10, idSoko: 1, idNormal, su: 6, tanka: 8000);
		InsertUriage("20260916", 10, idSoko: 1, idConsump, su: 3, tanka: 2000);
		// 返品(売上返品): NORMAL 1個×8,000円
		InsertUriage("20260920", 20, idSoko: 1, idNormal, su: 1, tanka: 8000);

		// 手順4: 消化仕入→諸掛確認→在庫Rebuild→原価更新(設計書§7推奨順)を実行する
		await RunFullCostCycle("202609", idShain, "E2E1");

		// ------------------------------------------------------------------
		// 手順5: 商品原価・原価履歴・生成仕入・買掛・在庫・売上粗利を手計算と突合する
		// ------------------------------------------------------------------

		// 総平均原価(NORMAL): Denominator=OpeningQty(10)+PurchaseQty(20-5=15)=25
		//                     Numerator=OpeningAmount(50,000)+PurchaseAmount(100,000-25,000=75,000)+Sundry(100)=125,100
		//                     AfterCost=floor(125,100/25)=5,004 (25*5,004=125,100、割り切れる)
		const int expectedNormalAfterCost = 5004;
		var normalGenka = FetchGenka(idNormal, "202609");
		Assert.IsNotNull(normalGenka, "NORMALのTranGenka行が作られていない");
		Assert.AreEqual(5000, normalGenka!.BeforeCost);
		Assert.AreEqual(15, normalGenka.PurchaseQty);
		Assert.AreEqual(75_000, normalGenka.PurchaseAmount);
		Assert.AreEqual(100, normalGenka.SundryAmount);
		Assert.AreEqual(expectedNormalAfterCost, normalGenka.AfterCost);
		Assert.AreEqual(expectedNormalAfterCost, TankaGenkaOf(idNormal)); // 現在原価へ反映済み

		// 消化仕入商品(CONSUMP)は原価代用(TankaShiire=500固定)のため総平均原価更新の対象外(PurchaseType=Consumption)
		Assert.IsNull(FetchGenka(idConsump, "202609"));

		// 生成仕入(消化仕入): CONSUMP 3個×500円=1,500円、IsStock=0
		var generated = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generated.Count);
		Assert.AreEqual(0, generated[0].IsStock);
		Assert.AreEqual(3, generated[0].Jmeisai![0].Su);
		Assert.AreEqual(500, generated[0].Jmeisai![0].Tanka);
		Assert.AreEqual(1500L, generated[0].Jmeisai![0].Kingaku);

		// 買掛(SummaryKaiKake): 消化仕入先SR1への計上額は生成仕入の1,500円のみ
		// (NORMALの通常仕入はId_Shiire未設定(0)のため、SR1のバケットには混ざらない)
		var kaikake = Db.Fetch<SummaryKaiKake>("WHERE Id_Shiire=@0", idShiireConsignment);
		Assert.AreEqual(1, kaikake.Count);
		Assert.AreEqual(1500, kaikake[0].Shiire);

		// 在庫(SummaryStock, 202609のNORMAL): 純増減=購入20-返品5-売上6+売上返品1=+10
		// (消化仕入で生成された仕入はIsStock=0のため在庫Rebuildをかけても積まれない。設計書§4.3、C-07)
		var normalStock202609 = Db.FirstOrDefault<SummaryStock>("WHERE SumMonth=@0 AND Id_Shohin=@1 AND Id_Soko=1", "202609", idNormal);
		Assert.IsNotNull(normalStock202609, "NORMALの202609在庫行が作られていない");
		Assert.AreEqual(10, normalStock202609!.Su);
		// 消化仕入商品(CONSUMP)自体はIsZaiko=1のため、売上(卸売上)による出庫はSummaryStockへ通常どおり積まれる
		// (C-07が除外するのは「消化仕入で生成された仕入」がIsStock=0で積まれないことであり、消化仕入商品という
		// 商品区分そのものをSummaryStock集計から除外するわけではない)。ここでは売上3個ぶんの出庫(-3)を確認する。
		var consumpStock202609 = Db.FirstOrDefault<SummaryStock>("WHERE SumMonth=@0 AND Id_Shohin=@1 AND Id_Soko=1", "202609", idConsump);
		Assert.IsNotNull(consumpStock202609, "CONSUMPの202609在庫行が作られていない");
		Assert.AreEqual(-3, consumpStock202609!.Su);

		// 売上粗利(手計算): 原価4処理そのものはこの値を保存しないため、実際の売上明細(Su・Kingaku)と
		// 本テストが確定させた原価(NORMALはTankaGenka、CONSUMPは固定TankaShiire)から粗利を計算し、
		// 手計算した定数と突合する。
		var normalUriageLines = Db.Fetch<Tran00Uriage>("WHERE DenDay IN (@0,@1)", "20260915", "20260920")
			.SelectMany(h => h.Jmeisai!.Select(m => (m.Su, m.Kingaku, CalcFlag: h.CalcFlag)));
		var normalNetQty = normalUriageLines.Sum(l => l.Su * l.CalcFlag);
		var normalNetRevenue = normalUriageLines.Sum(l => l.Kingaku * l.CalcFlag);
		// 純売上6-1=5個×8,000円=40,000円の売上に対し、原価は5個×5,004円=25,020円 → 粗利14,980円
		Assert.AreEqual(5, normalNetQty);
		Assert.AreEqual(40_000L, normalNetRevenue);
		var normalGrossMargin = normalNetRevenue - normalNetQty * expectedNormalAfterCost;
		Assert.AreEqual(14_980L, normalGrossMargin);

		var consumpUriageLine = Db.Fetch<Tran00Uriage>("WHERE DenDay=@0", "20260916")
			.SelectMany(h => h.Jmeisai!.Select(m => (m.Su, m.Kingaku))).Single();
		// 3個×2,000円=6,000円の売上に対し、原価は3個×500円=1,500円 → 粗利4,500円
		Assert.AreEqual(6_000L, consumpUriageLine.Kingaku);
		var consumpGrossMargin = consumpUriageLine.Kingaku - consumpUriageLine.Su * 500;
		Assert.AreEqual(4_500L, consumpGrossMargin);

		// ------------------------------------------------------------------
		// 手順6: 元売上・仕入を修正して同じ順で再実行し、二重計上がないことを確認する
		// ------------------------------------------------------------------

		// NORMALの仕入数量を20→30個(金額150,000円)へ修正し、CONSUMPの売上数量を3→5個へ修正する
		var purchaseHeader = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind IS NULL OR GeneratedKind=0")
			.Single(h => h.Kubun == 10 && h.Jmeisai![0].Id_Shohin == idNormal);
		purchaseHeader.Jmeisai![0].Su = 30;
		purchaseHeader.Jmeisai![0].Kingaku = 150_000;
		purchaseHeader.Vdu += 1;
		Db.Update(purchaseHeader);

		var consumpUriage = Db.Single<Tran00Uriage>("WHERE Kubun=10 AND DenDay=@0", "20260916");
		consumpUriage.Jmeisai![0].Su = 5;
		consumpUriage.Jmeisai![0].Kingaku = 5 * 2000;
		consumpUriage.Vdu += 1;
		Db.Update(consumpUriage);

		await RunFullCostCycle("202609", idShain, "E2E2");

		// 消化仕入: 再生成後も1件のまま(重複0件)、内容は新しい売上数量(5個)を反映する
		var generatedAfterRerun = Db.Fetch<Tran03Shiire>("WHERE GeneratedKind=@0", (int)EnumGeneratedKind.ConsumptionPurchase);
		Assert.AreEqual(1, generatedAfterRerun.Count, "消化仕入が二重計上されている");
		Assert.AreEqual(5, generatedAfterRerun[0].Jmeisai![0].Su);
		Assert.AreEqual(2500L, generatedAfterRerun[0].Jmeisai![0].Kingaku);
		Assert.AreEqual(1, Db.Fetch<TranConsumptionPurchaseLink>("").Count, "消化仕入対応表が二重計上されている");

		// 買掛: 新しい生成仕入額(2,500円)のみ。二重計上されず古い値(1,500円)も残らない
		var kaikakeAfterRerun = Db.Fetch<SummaryKaiKake>("WHERE Id_Shiire=@0", idShiireConsignment);
		Assert.AreEqual(1, kaikakeAfterRerun.Count, "買掛行が二重計上されている");
		Assert.AreEqual(2500, kaikakeAfterRerun[0].Shiire);

		// 総平均原価: Denominator=10+(30-5)=35、Numerator=50,000+(150,000-25,000)+100=175,100
		// AfterCost=floor(175,100/35)=5,002(35*5,002=175,070、余り30)
		var normalGenkaAfterRerun = FetchGenka(idNormal, "202609");
		Assert.IsNotNull(normalGenkaAfterRerun);
		Assert.AreEqual(25, normalGenkaAfterRerun!.PurchaseQty);
		Assert.AreEqual(125_000, normalGenkaAfterRerun.PurchaseAmount);
		Assert.AreEqual(5002, normalGenkaAfterRerun.AfterCost);
		// ChangeKind=0だけで絞ると基準行(SumMonth=190101、EnsureBaselineCostRowsが作る)も含まれてしまうため、
		// 対象月・原価方式まで絞って202609のTranGenka行が1件だけであることを確認する。
		Assert.AreEqual(1, Db.Fetch<TranGenka>(
			"WHERE Id_Shohin=@0 AND SumMonth='202609' AND CostMethod=@1 AND ChangeKind=0", idNormal, (int)EnumCostMethod.TotalAverage).Count,
			"総平均原価の履歴行が二重計上されている");
		Assert.AreEqual(5002, TankaGenkaOf(idNormal));
	}

	// ------------------------------------------------------------------
	// 手順7: 同じDBスナップショットから2回実行し、全テーブル差分が0件になることを確認する(冪等性)
	// ------------------------------------------------------------------

	[TestMethod]
	public async System.Threading.Tasks.Task FullCycle_RunTwiceFromSameStartingData_ProducesNoTableDiff() {
		CreateAllTables();
		var idShain = InsertShain();
		var idShiireConsignment = InsertShiire("SR1");
		var idMaterial = InsertMaterial("M1");
		var idNormal = InsertNormalShohin("NORMAL", tankaGenka: 5000);
		var idConsump = InsertConsumptionShohin("CONSUMP", idShiireConsignment, tankaShiire: 500);
		InsertOpeningStock("202608", idNormal, su: 10);
		InsertPurchase("20260905", 10, idNormal, su: 20, kingaku: 100_000);
		InsertPurchase("20260910", 20, idNormal, su: 5, kingaku: 25_000);
		InsertMaterialHeader("20260906", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 50);
		InsertMaterialHeader("20260907", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 30);
		InsertMaterialHeader("20260908", 10, idShiireConsignment, idMaterial, idNormal, su: 1, kingaku: 20);
		InsertUriage("20260915", 10, idSoko: 1, idNormal, su: 6, tanka: 8000);
		InsertUriage("20260916", 10, idSoko: 1, idConsump, su: 3, tanka: 2000);
		InsertUriage("20260920", 20, idSoko: 1, idNormal, su: 1, tanka: 8000);

		// 1回目
		await RunFullCostCycle("202609", idShain, "RUN1");
		var snapshotAfterFirstRun = TakeSnapshot();

		// 2回目: 同じ計上月・同じ元データに対してもう一度、消化仕入→諸掛確認→在庫Rebuild→原価更新をかける
		await RunFullCostCycle("202609", idShain, "RUN2");
		var snapshotAfterSecondRun = TakeSnapshot();

		// 原価4処理が触る全テーブル(TranGenka/TranConsumptionPurchaseLink/生成Tran03Shiire/
		// MasterShohin.TankaGenka/SummaryStock/SummaryKaiKake)を、Id・Vdc・Vdu等の揮発列を除いて
		// 正規化した文字列として比較する。1文字でも異なれば差分ありとみなす(設計書§11.3手順7)。
		Assert.AreEqual(snapshotAfterFirstRun.TranGenka, snapshotAfterSecondRun.TranGenka, "TranGenkaに差分がある(冪等性違反)");
		Assert.AreEqual(snapshotAfterFirstRun.ConsumptionLink, snapshotAfterSecondRun.ConsumptionLink, "TranConsumptionPurchaseLinkに差分がある(冪等性違反)");
		Assert.AreEqual(snapshotAfterFirstRun.GeneratedShiire, snapshotAfterSecondRun.GeneratedShiire, "生成Tran03Shiireに差分がある(冪等性違反)");
		Assert.AreEqual(snapshotAfterFirstRun.ShohinTankaGenka, snapshotAfterSecondRun.ShohinTankaGenka, "MasterShohin.TankaGenkaに差分がある(冪等性違反)");
		Assert.AreEqual(snapshotAfterFirstRun.SummaryStock, snapshotAfterSecondRun.SummaryStock, "SummaryStockに差分がある(冪等性違反)");
		Assert.AreEqual(snapshotAfterFirstRun.SummaryKaiKake, snapshotAfterSecondRun.SummaryKaiKake, "SummaryKaiKakeに差分がある(冪等性違反)");
	}
}
