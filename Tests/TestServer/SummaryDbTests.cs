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
		// 引当数の反映が ON CONFLICT を使うので、本番と同じユニークインデックスを張る
		var db = PrepareStockTables();

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
		AssertRealStock(db, 9, 13, idShohin: 90, idCol: 900, idSiz: 9000);
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
		Assert.AreEqual("20260809", rebuilt.StocktakeDdate);
		Assert.AreEqual(88, rebuilt.ActualQty);
		// AdjustQty は 2026-08-17 の決定(F0/F2)で在庫調整伝票 Tran61Chosei から導出する列になったため、
		// 非Tran列として復元する対象から外れた。伝票が無ければ 0 になるのが正しい
		Assert.AreEqual(0, rebuilt.AdjustQty, "調整数は伝票から再計算されるので手で入れた値は残らない");
	}

	[TestMethod]
	public void CalcHaibun2Reserve_InsertAndDelete_UpdatesReserveQtyWithoutTouchingStock() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		InsertSummaryStock(db, "202608", 1, 10, 100, 1000, 50);
		db.Insert(new SummaryRealStock { Id_Soko = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = 50, Vdc = 1, Vdu = 1 });

		var first = CreateHaibun("20260815", 1, 7);
		db.Insert(first);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(first));

		AssertMonthReserve(db, "202608", 1, 7);
		AssertRealReserve(db, 1, 7);

		var second = CreateHaibun("20260820", 1, 3);
		db.Insert(second);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(second));

		AssertMonthReserve(db, "202608", 1, 10);
		AssertRealReserve(db, 1, 10);

		db.Delete(first);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(first));

		AssertMonthReserve(db, "202608", 1, 3);
		AssertRealReserve(db, 1, 3);

		db.Delete(second);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(second));

		AssertMonthReserve(db, "202608", 1, 0);
		AssertRealReserve(db, 1, 0);
		// 引当数は実在庫の数量を変えない
		AssertRealStock(db, 1, 50);
	}

	[TestMethod]
	public void CalcHaibun2Reserve_EndFlagTransition_ReleasesAndRestoresReserve() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		var haibun = CreateHaibun("20260815", 1, 7);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		// 在庫実績が無いSKUでも引当だけの行が作られる（有効在庫がマイナスで見える）
		AssertMonthReserve(db, "202608", 1, 7);
		AssertRealReserve(db, 1, 7);
		AssertRealStock(db, 1, 0);

		// 振り分け後入庫済み(EndFlag=1)で引当解除
		haibun.EndFlag = 1;
		db.Update(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		AssertMonthReserve(db, "202608", 1, 0);
		AssertRealReserve(db, 1, 0);

		// 入庫を取り消したら再び引当に戻る
		haibun.EndFlag = 0;
		db.Update(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		AssertMonthReserve(db, "202608", 1, 7);
		AssertRealReserve(db, 1, 7);
	}

	/// <summary>
	/// 初回配分(Kubun=0)は入荷前の振り分けであり現物を押さえないため引当対象外とする。
	/// 仕様は 2026-08-17_旧cvnet比較_仕様決定判断材料.md 5.2.2。
	/// </summary>
	[TestMethod]
	public void CalcHaibun2Reserve_HatsukaiHaibun_IsNotReserved() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		var hatsukai = CreateHaibun("20260815", 1, 7, kubun: EnumHaibun.Hatsukai);
		db.Insert(hatsukai);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(hatsukai));

		AssertMonthReserve(db, "202608", 1, 0);
		AssertRealReserve(db, 1, 0);

		// 同じキーへ在庫配分を足すと、その分だけが引当になる
		var zaiko = CreateHaibun("20260815", 1, 3, kubun: EnumHaibun.Zaiko);
		db.Insert(zaiko);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(zaiko));

		AssertMonthReserve(db, "202608", 1, 3);
		AssertRealReserve(db, 1, 3);

		// 初回配分以外は区分を問わず引当対象（取置も含む）
		var reservation = CreateHaibun("20260815", 1, 2, kubun: EnumHaibun.Reservation);
		db.Insert(reservation);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(reservation));

		AssertMonthReserve(db, "202608", 1, 5);
		AssertRealReserve(db, 1, 5);

		// 初回配分を除外する条件は全件Rebuild側にも効く
		var incremental = GetReserveSnapshot(db);
		summaryDb.CalcReserveQtyAll();
		CollectionAssert.AreEqual(incremental, GetReserveSnapshot(db), "通常更新値とRebuild値は一致する");
	}

	/// <summary>
	/// 未確定は指示数 Su、確定済み(KakuteiDayに有効日付)は確定数 JitsuSu を引当に積む。
	/// 欠品(ShortSu)は確定と同時に引当から外れる。仕様は 5.2.2c。
	/// </summary>
	[TestMethod]
	public void CalcHaibun2Reserve_AfterKakutei_UsesJitsuSuInsteadOfSu() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		var haibun = CreateHaibun("20260815", 1, 10);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		// 未確定のうちは指示数をそのまま押さえる
		AssertMonthReserve(db, "202608", 1, 10);
		AssertRealReserve(db, 1, 10);

		// 倉庫から JitsuSu=4 / ShortSu=6 が返り確定する。Su = JitsuSu + ShortSu
		haibun.JitsuSu = 4;
		haibun.ShortSu = 6;
		haibun.KakuteiDay = "20260816";
		db.Update(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		AssertMonthReserve(db, "202608", 1, 4);
		AssertRealReserve(db, 1, 4);

		// 全量欠品なら引当は消える
		haibun.JitsuSu = 0;
		haibun.ShortSu = 10;
		db.Update(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		AssertMonthReserve(db, "202608", 1, 0);
		AssertRealReserve(db, 1, 0);
	}

	[TestMethod]
	public void CalcHaibun2Reserve_WarehouseAndMonthChanged_MovesReserveToNewKey() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		var haibun = CreateHaibun("20260815", 1, 7);
		db.Insert(haibun);
		var orgKey = ReserveKey.From(haibun);
		summaryDb.CalcHaibun2Reserve(orgKey);

		haibun.DenDay = "20260901";
		haibun.Id_Soko = 2;
		db.Update(haibun);
		// 修正前後の両方のキーを渡す
		summaryDb.CalcHaibun2Reserve(orgKey, ReserveKey.From(haibun));

		AssertMonthReserve(db, "202608", 1, 0);
		AssertRealReserve(db, 1, 0);
		AssertMonthReserve(db, "202609", 2, 7);
		AssertRealReserve(db, 2, 7);
	}

	[TestMethod]
	public void CalcReserveQtyAll_MatchesIncrementalUpdateAndSumsAllMonthsForRealStock() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		TranHaibun[] haibunRows = [
			CreateHaibun("20260815", 1, 7),
			CreateHaibun("20260820", 1, 3),
			CreateHaibun("20260905", 1, 5),
			CreateHaibun("20260910", 1, 4, endFlag: 1), // 入庫済みは引当に数えない
			CreateHaibun("20260815", 2, 9),
		];
		foreach (var haibun in haibunRows) {
			db.Insert(haibun);
			summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));
		}
		var incremental = GetReserveSnapshot(db);

		summaryDb.CalcReserveQtyAll();

		CollectionAssert.AreEqual(incremental, GetReserveSnapshot(db), "通常更新値とRebuild値は一致する");
		AssertMonthReserve(db, "202608", 1, 10);
		AssertMonthReserve(db, "202609", 1, 5);
		AssertMonthReserve(db, "202608", 2, 9);
		// 現在庫の引当数は全月合計
		AssertRealReserve(db, 1, 15);
		AssertRealReserve(db, 2, 9);
	}

	/// <summary>
	/// 出荷指示確定は有効在庫を割ると1件も確定しない。旧CV.netの
	/// 「有効在庫数 − 入力した予指示が正の場合のみ確定できる」に対応する（仕様 5.2.4 / I3）。
	/// </summary>
	[TestMethod]
	public void ConfirmShipping_RejectsAllWhenAvailableStockGoesNegative() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		// 実在庫5に対して8を配分すると有効在庫が -3 になる
		var purchase = CreatePurchase("20260810", 1, 5, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 8);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		var cnt = shippingDb.ConfirmShipping([haibun.Id], "20260816", out var errors);

		Assert.AreEqual(0, cnt);
		Assert.AreEqual(1, errors.Count);
		Assert.AreEqual(-3, errors[0].Yuko);
		Assert.AreEqual(8, errors[0].Shiji);
		Assert.AreEqual("", db.Single<TranHaibun>("where Id=@0", haibun.Id).KakuteiDay, "1件も確定しない");

		// 在庫を積み増すと確定できる
		var extra = CreatePurchase("20260811", 1, 3, EnumShiire.Shiire);
		db.Insert(extra);
		ApplyImmediate(summaryDb, extra, false);

		Assert.AreEqual(1, shippingDb.ConfirmShipping([haibun.Id], "20260816", out var ok));
		Assert.AreEqual(0, ok.Count);
		Assert.AreEqual("20260816", db.Single<TranHaibun>("where Id=@0", haibun.Id).KakuteiDay);
	}

	/// <summary>
	/// 出荷処理は仮想ヘッダ単位で伝票を作る。出荷先の店種区分で出荷売上と移動出庫に分かれ、
	/// 伝票Idを RelateNo2 へ書いて EndFlag=1 で引当を解除する（仕様 I2 / I4 / I5）。
	/// </summary>
	[TestMethod]
	public void CreateShippingSlips_SplitsByTenTypeAndReleasesReserve() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var oroshiId = InsertTokui(db, "T011", "卸先", tenType: 1);
		var chokueiId = InsertTokui(db, "T016", "直営店", tenType: 6);
		var purchase = CreatePurchase("20260810", 1, 100, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);

		// 同じ倉庫から卸先と直営店へ配分する。出荷先が違うので仮想ヘッダは別になる
		var toOroshi = CreateHaibun("20260815", 1, 10);
		toOroshi.Id_Tenpo = oroshiId;
		var toChokuei = CreateHaibun("20260815", 1, 4);
		toChokuei.Id_Tenpo = chokueiId;
		db.Insert(toOroshi);
		db.Insert(toChokuei);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(toOroshi));
		AssertRealReserve(db, 1, 14);

		shippingDb.ConfirmShipping([toOroshi.Id, toChokuei.Id], "20260816", out _);
		// 倉庫から確定数が返る。卸先は8出荷2欠品、直営店は全量出荷
		db.Execute("update TranHaibun set JitsuSu=8, ShortSu=2 where Id=@0", toOroshi.Id);
		db.Execute("update TranHaibun set JitsuSu=4 where Id=@0", toChokuei.Id);

		var created = shippingDb.CreateShippingSlips([toOroshi.Id, toChokuei.Id], "20260817", idShain: 1);

		Assert.AreEqual(2, created.Count, "出荷先ごとに1伝票");
		var uriage = db.Single<Tran00Uriage>("where Id_Tokui=@0", oroshiId);
		Assert.AreEqual(8, uriage.SuTotal, "卸先は出荷売上。欠品2は出荷しない");
		var ido = db.Single<Tran10IdoOut>("where Id_Ido=@0", chokueiId);
		Assert.AreEqual(4, ido.SuTotal, "直営店は移動出庫");

		Assert.AreEqual(1, db.Single<TranHaibun>("where Id=@0", toOroshi.Id).EndFlag);
		Assert.AreEqual((int)uriage.Id, db.Single<TranHaibun>("where Id=@0", toOroshi.Id).RelateNo2);
		AssertRealReserve(db, 1, 0);
		// 仕入100 − 出荷売上8 − 移動出庫4
		AssertRealStock(db, 1, 88);
	}

	/// <summary>全量欠品の行は伝票を作らずに完了だけ立てて引当から外す</summary>
	[TestMethod]
	public void CreateShippingSlips_AllShortage_ReleasesReserveWithoutSlip() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var oroshiId = InsertTokui(db, "T011", "卸先", tenType: 1);
		var purchase = CreatePurchase("20260810", 1, 50, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 6);
		haibun.Id_Tenpo = oroshiId;
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));
		shippingDb.ConfirmShipping([haibun.Id], "20260816", out _);
		db.Execute("update TranHaibun set JitsuSu=0, ShortSu=6 where Id=@0", haibun.Id);

		var created = shippingDb.CreateShippingSlips([haibun.Id], "20260817", idShain: 1);

		Assert.AreEqual(0, created.Count, "出荷数0なら伝票を作らない");
		Assert.AreEqual(1, db.Single<TranHaibun>("where Id=@0", haibun.Id).EndFlag);
		AssertRealReserve(db, 1, 0);
		AssertRealStock(db, 1, 50, "在庫は動かない");
	}

	/// <summary>
	/// 棚卸開始処理は対象年月末時点の帳簿在庫を凍結し、棚卸確定処理は実棚数との差を
	/// 在庫調整伝票(Tran61Chosei)として起こす。仕様 8.1 / 8.4(F0 / F0' / F0'')。
	/// </summary>
	[TestMethod]
	public void Stocktake_StartAndFix_AdjustsStockByChoseiSlip() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		// 仕入20 → 帳簿在庫20
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		AssertRealStock(db, 1, 20);

		stocktakeDb.StartStocktake("202608");
		var afterStart = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(20, afterStart.BookQty, "棚卸開始処理が帳簿在庫を保存する");

		// 棚卸開始のあとに伝票が入っても帳簿在庫は動かない（棚卸中の凍結）
		var extra = CreatePurchase("20260811", 1, 5, EnumShiire.Shiire);
		db.Insert(extra);
		ApplyImmediate(summaryDb, extra, false);
		Assert.AreEqual(20, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).BookQty);

		// 実棚18 を登録して確定する。帳簿20との差 -2 が調整伝票になる
		db.Insert(CreateTana("20260831", 1, 18));
		var cnt = stocktakeDb.FixStocktake("202608", "20260831", idShain: 1);

		Assert.AreEqual(1, cnt, "倉庫単位に1伝票");
		var chosei = db.Single<Tran61Chosei>("where TanaMonth=@0", "202608");
		Assert.AreEqual(-2, chosei.SuTotal);
		Assert.AreEqual((int)EnumChosei.Tanaoroshi, chosei.Kubun);

		var fixedRow = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(18, fixedRow.ActualQty);
		Assert.AreEqual(-2, fixedRow.AdjustQty, "差は調整数へ入る");
		Assert.AreEqual(fixedRow.InQty + fixedRow.OutQty + fixedRow.AdjustQty, fixedRow.Su,
			"Su = InQty + OutQty + AdjustQty（仕様 8.4.1）");
		AssertRealStock(db, 1, 23, "仕入20+5に調整-2で23");
	}

	/// <summary>棚卸確定は再実行できる。前回の調整伝票を取り消してから作り直す（仕様 F0''）</summary>
	[TestMethod]
	public void Stocktake_Refix_ReplacesPreviousAdjustment() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608");
		db.Insert(CreateTana("20260831", 1, 18));
		stocktakeDb.FixStocktake("202608", "20260831", idShain: 1);
		AssertRealStock(db, 1, 18);

		// 棚卸数を数え直して再確定する
		db.Execute("DELETE FROM Tran60Tana");
		db.Insert(CreateTana("20260831", 1, 21));
		stocktakeDb.FixStocktake("202608", "20260831", idShain: 1);

		Assert.AreEqual(1, db.Fetch<Tran61Chosei>("where TanaMonth=@0", "202608").Count, "調整伝票は作り直しで1件のまま");
		AssertRealStock(db, 1, 21, "再確定後は最新の棚卸数に一致する");
		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(1, row.AdjustQty);
		Assert.AreEqual(row.InQty + row.OutQty + row.AdjustQty, row.Su);
	}

	/// <summary>調整伝票は他の伝票と同じ経路でRebuildできる。集計へ直接書かない理由（仕様 8.4 F2）</summary>
	[TestMethod]
	public async Task Stocktake_Adjustment_SurvivesRebuild() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608");
		db.Insert(CreateTana("20260831", 1, 18));
		stocktakeDb.FixStocktake("202608", "20260831", idShain: 1);
		var immediate = GetStockSnapshot(db);

		await RunRebuildAsync(summaryDb, "202608", "202608");

		CollectionAssert.AreEqual(immediate, GetStockSnapshot(db), "通常更新値とRebuild値は一致する");
		AssertRealStock(db, 1, 18);
		Assert.AreEqual(-2, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).AdjustQty);
	}

	[TestMethod]
	public async Task SummaryAllAsyncStream_Rebuild_PreservesReserveQty() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 7);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));
		var immediateSnapshot = GetStockSnapshot(db);

		await RunRebuildAsync(summaryDb, "202608", "202608");

		// DELETE→再INSERTしても引当数が失われない
		CollectionAssert.AreEqual(immediateSnapshot, GetStockSnapshot(db));
		AssertSummaryStock(db, "202608", 1, 20, 20, 0, 0);
		AssertMonthReserve(db, "202608", 1, 7);
		AssertRealStock(db, 1, 20);
		AssertRealReserve(db, 1, 7);
	}

	[TestMethod]
	public void CalcSummaryRealStock_FullRebuild_RestoresReserveQty() {
		var db = PrepareStockTables();
		var summaryDb = new SummaryDb(db);
		InsertSummaryStock(db, "202608", 1, 10, 100, 1000, 50);
		var haibun = CreateHaibun("20260815", 1, 7);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		summaryDb.CalcSummaryRealStock("202608");

		AssertRealStock(db, 1, 50);
		AssertRealReserve(db, 1, 7);
		AssertMonthReserve(db, "202608", 1, 7);
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
		// 引当数(ReserveQty)の源泉。Rebuildも通常更新もTranHaibunを読むので常に作成する
		db.CreateTable(typeof(TranHaibun), true, false);
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
		db.CreateTable(typeof(Tran60Tana), true, false);
		db.CreateTable(typeof(Tran61Chosei), true, false);
		return db;
	}

	/// <summary>出荷処理は得意先マスタの店種区分で伝票種別を分けるので MasterTokui も要る</summary>
	private ExDatabaseSqlite PrepareShippingTables() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(MasterTokui), true, false);
		return db;
	}

	/// <summary>得意先を登録して採番されたIdを返す。Idは自動採番なので明示指定しても反映されない</summary>
	private static long InsertTokui(ExDatabaseSqlite db, string code, string name, int tenType) {
		var tokui = new MasterTokui { Code = code, Name = name, TenType = tenType };
		db.Insert(tokui);
		return tokui.Id;
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
			.Select(x => $"M:{x.SumMonth}:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.Su}:{x.InQty}:{x.OutQty}:{x.TransitQty}:{x.ReserveQty}");
		var real = db.Fetch<SummaryRealStock>("order by Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.Select(x => $"R:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.Su}:{x.ReserveQty}");
		return monthly.Concat(real).ToArray();
	}

	/// <summary>引当数だけを抜き出したスナップショット。通常更新値とRebuild値の一致確認に使う</summary>
	private static string[] GetReserveSnapshot(ExDatabaseSqlite db) {
		var monthly = db.Fetch<SummaryStock>("order by SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.Select(x => $"M:{x.SumMonth}:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.ReserveQty}");
		var real = db.Fetch<SummaryRealStock>("order by Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.Select(x => $"R:{x.Id_Soko}:{x.Id_Shohin}:{x.Id_Col}:{x.Id_Siz}:{x.ReserveQty}");
		return monthly.Concat(real).ToArray();
	}

	/// <summary>
	/// 引当テスト用の配分行。既定は在庫配分(引当対象)・未確定とする。
	/// 初回配分(<see cref="EnumHaibun.Hatsukai"/>)は引当対象外なので、明示的に kubun を渡す。
	/// </summary>
	private static TranHaibun CreateHaibun(
		string denDay,
		long idSoko,
		int su,
		int endFlag = 0,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000,
		EnumHaibun kubun = EnumHaibun.Zaiko,
		string kakuteiDay = "",
		int jitsuSu = 0,
		int shortSu = 0) => new() {
			DenDay = denDay,
			Id_Soko = idSoko,
			Id_Shohin = idShohin,
			Id_Col = idCol,
			Id_Siz = idSiz,
			Su = su,
			EndFlag = endFlag,
			Kubun = (int)kubun,
			KakuteiDay = kakuteiDay,
			JitsuSu = jitsuSu,
			ShortSu = shortSu,
		};

	/// <summary>月次の引当数。行が無い場合は0とみなす（引当が0のキーに行を作らないため）</summary>
	private static void AssertMonthReserve(
		ExDatabaseSqlite db,
		string sumMonth,
		long idSoko,
		int reserveQty,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var rows = db.Fetch<SummaryStock>(
			"where SumMonth=@0 and Id_Soko=@1 and Id_Shohin=@2 and Id_Col=@3 and Id_Siz=@4",
			sumMonth,
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(reserveQty, rows.Count == 0 ? 0 : rows[0].ReserveQty);
	}

	/// <summary>現在庫の引当数。行が無い場合は0とみなす</summary>
	private static void AssertRealReserve(
		ExDatabaseSqlite db,
		long idSoko,
		int reserveQty,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var rows = db.Fetch<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(reserveQty, rows.Count == 0 ? 0 : rows[0].ReserveQty);
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
		string? message = null,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) {
		var row = db.Single<SummaryRealStock>(
			"where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			idSoko,
			idShohin,
			idCol,
			idSiz);
		Assert.AreEqual(su, row.Su, message ?? string.Empty);
	}

	/// <summary>棚卸入力伝票。在庫は動かさず、棚卸確定処理だけが読む</summary>
	private static Tran60Tana CreateTana(string denDay, long idSoko, int su,
		long idShohin = 10, long idCol = 100, long idSiz = 1000) => new() {
			DenDay = denDay,
			Id_Soko = idSoko,
			SuTotal = su,
			Jmeisai = [new Tran99Meisai {
				No = 1, Id_Shohin = idShohin, Id_Col = idCol, Id_Siz = idSiz, Su = su,
			}],
		};

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
