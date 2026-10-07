using System;
using System.Globalization;
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
		// 全体排他が
		// StreamStepProgressRunner経由の全ストリーム処理(SummaryAllAsyncStream等)で使うため、
		// 個々のテストのテーブル準備に関わらずここで作っておく。
		_db.CreateTable(typeof(SysSequence), true, false);
		_db.CreateTable(typeof(SysHistAutoexec), true, false);
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

	/// <summary>在庫管理FLG=0の得意先/倉庫・商品には在庫データを作らない</summary>
	[TestMethod]
	public void CalcTran2SummaryStock_NonManagedSide_IsSkippedOnBothTokuiAndShohin() {
		var db = PrepareStockTables();
		db.CreateTable(typeof(Tran05Ido), true, false);
		db.Insert(new MasterTokui { Code = "S1", Name = "在庫管理あり", IsZaiko = 1 }); // Id=1
		db.Insert(new MasterTokui { Code = "S2", Name = "在庫管理なし", IsZaiko = 0 }); // Id=2
		var tran = CreateTransfer<Tran05Ido>("20260815", 1, 2, 7);
		db.Insert(tran);
		var summaryDb = new SummaryDb(db);

		ApplyImmediate(summaryDb, tran, false);

		AssertRealStock(db, 1, -7, "移動元(在庫管理FLG=1)は減る");
		Assert.AreEqual(0, db.Fetch<SummaryRealStock>("where Id_Soko=@0", 2).Count, "移動先(在庫管理FLG=0)には在庫データを作らない");
		Assert.AreEqual(0, db.Fetch<SummaryStock>("where Id_Soko=@0", 2).Count);

		// 逆に商品側の在庫管理FLG=0でも同様に抑止される
		db.CreateTable(typeof(MasterShohin), true, false);
		var shohin = new MasterShohin { Code = "Z1", Name = "在庫管理しない商品", IsZaiko = 0 };
		db.Insert(shohin);
		// Idは採番されるので、CreateTransferが使う固定Id(10)へ寄せる
		db.Execute("update MasterShohin set Id=10 where Id=@0", shohin.Id);
		var tran2 = CreateTransfer<Tran05Ido>("20260815", 1, 2, 5);
		db.Insert(tran2);
		ApplyImmediate(summaryDb, tran2, false);

		AssertRealStock(db, 1, -7, "商品の在庫管理FLG=0の分は追加されない");
	}

	[TestMethod]
	public async Task SummaryStock_UsesOwnClosingDayForImmediateUpdateAndRebuild() {
		var db = PrepareAllStockTables();
		db.Execute($"UPDATE {nameof(MasterSysman)} SET ShimeBi=@0", 20);
		var rows = new[] {
			CreatePurchase("20260720", 1, 1, EnumShiire.Shiire),
			CreatePurchase("20260721", 1, 2, EnumShiire.Shiire),
			CreatePurchase("20260820", 1, 4, EnumShiire.Shiire),
			CreatePurchase("20260821", 1, 8, EnumShiire.Shiire),
		};
		var summaryDb = new SummaryDb(db);
		foreach (var row in rows) {
			db.Insert(row);
			ApplyImmediate(summaryDb, row, false);
		}

		AssertSummaryStock(db, "202607", 1, 1, 1, 0, 0);
		AssertSummaryStock(db, "202608", 1, 6, 6, 0, 0);
		AssertSummaryStock(db, "202609", 1, 8, 8, 0, 0);
		var immediate = GetStockSnapshot(db);

		await RunRebuildAsync(new SummaryDb(db), "202607", "202609");

		CollectionAssert.AreEqual(immediate, GetStockSnapshot(db));
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
	/// 初回配分の入荷後の数量は仕入配分の入荷割当で扱う。
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
	/// 欠品(ShortSu)は確定と同時に引当から外れる。
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
	/// 配分確定は有効在庫を割ると1件も確定しない。対象全件の検査後に一括適用する。
	/// 確定数を減らして欠品にすれば、残りの在庫の範囲で確定できる。
	/// </summary>
	[TestMethod]
	public void Commit_RejectsAllWhenAvailableStockGoesNegative() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		// 実在庫5に対して8を配分する
		var purchase = CreatePurchase("20260810", 1, 5, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 8);
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));
		var vdu = db.Single<TranHaibun>("where Id=@0", haibun.Id).Vdu;

		var result = shippingDb.Commit([(haibun.Id, vdu, 8)], "20260816", 1, out var outcome, out var errors);

		Assert.AreEqual(CommitOutcome.Shortage, outcome);
		Assert.AreEqual(0, result.CreatedSlipIds.Count);
		Assert.AreEqual(1, errors.Count);
		Assert.AreEqual(8, errors[0].Shiji);
		Assert.AreEqual(5, errors[0].Yuko, "自分の引当分を除いた有効在庫");
		var rejected = db.Single<TranHaibun>("where Id=@0", haibun.Id);
		Assert.AreEqual("", rejected.KakuteiDay, "1件も確定しない");
		Assert.AreEqual(0, rejected.EndFlag);
		AssertRealReserve(db, 1, 8);

		// 確定数5（欠品3）なら在庫の範囲内なので確定できる
		result = shippingDb.Commit([(haibun.Id, vdu, 5)], "20260816", 1, out outcome, out _);

		Assert.AreEqual(CommitOutcome.Success, outcome);
		Assert.AreEqual(1, result.CreatedSlipIds.Count);
		Assert.AreEqual(1, result.ShortageRowCount);
		var after = db.Single<TranHaibun>("where Id=@0", haibun.Id);
		Assert.AreEqual("20260816", after.KakuteiDay);
		Assert.AreEqual(5, after.JitsuSu);
		Assert.AreEqual(3, after.ShortSu);
		Assert.AreEqual(1, after.EndFlag);
		AssertRealReserve(db, 1, 0);
		AssertRealStock(db, 1, 0);
	}

	/// <summary>
	/// 配分確定は確定数を反映してから仮想ヘッダ単位で伝票を作る。出荷先の店種区分で出荷売上と移動出庫に分かれ、
	/// 伝票Idを RelateNo2 へ書いて EndFlag=1 で引当を解除する（決定 D8 / I2 / I4 / I5）。
	/// </summary>
	[TestMethod]
	public void Commit_SplitsByTenTypeAndReleasesReserve() {
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

		// 卸先は8出荷2欠品、直営店は全量出荷
		var result = shippingDb.Commit([
			(toOroshi.Id, VduOf(db, toOroshi.Id), 8),
			(toChokuei.Id, VduOf(db, toChokuei.Id), 4),
		], "20260817", 1, out var outcome, out _);

		Assert.AreEqual(CommitOutcome.Success, outcome);
		Assert.AreEqual(2, result.CreatedSlipIds.Count, "出荷先ごとに1伝票");
		Assert.AreEqual(2, result.CommittedCount);
		Assert.AreEqual(1, result.ShortageRowCount);
		var uriage = db.Single<Tran00Uriage>("where Id_Tokui=@0", oroshiId);
		Assert.AreEqual(8, uriage.SuTotal, "卸先は出荷売上。欠品2は出荷しない");
		Assert.AreEqual("20260817", uriage.DenDay, "伝票日は確定日");
		var ido = db.Single<Tran10IdoOut>("where Id_Ido=@0", chokueiId);
		Assert.AreEqual(4, ido.SuTotal, "直営店は移動出庫");

		var oroshiRow = db.Single<TranHaibun>("where Id=@0", toOroshi.Id);
		Assert.AreEqual(1, oroshiRow.EndFlag);
		Assert.AreEqual((int)uriage.Id, oroshiRow.RelateNo2);
		Assert.AreEqual(8, oroshiRow.JitsuSu);
		Assert.AreEqual(2, oroshiRow.ShortSu);
		AssertRealReserve(db, 1, 0);
		// 仕入100 − 出荷売上8 − 移動出庫4
		AssertRealStock(db, 1, 88);
		var incremental = GetReserveSnapshot(db);
		summaryDb.CalcReserveQtyAll();
		CollectionAssert.AreEqual(incremental, GetReserveSnapshot(db), "通常更新値とRebuild値は一致する");
	}

	/// <summary>
	/// 確定数0（全量欠品）は伝票を作らずに完了だけ立てて引当から外す。在庫が無くても在庫検査で止めない（指示取消）。
	/// </summary>
	[TestMethod]
	public void Commit_ZeroQty_CompletesWithoutSlipEvenWithoutStock() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var oroshiId = InsertTokui(db, "T011", "卸先", tenType: 1);
		var haibun = CreateHaibun("20260815", 1, 6);
		haibun.Id_Tenpo = oroshiId;
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		var result = shippingDb.Commit([(haibun.Id, VduOf(db, haibun.Id), 0)], "20260817", 1, out var outcome, out _);

		Assert.AreEqual(CommitOutcome.Success, outcome);
		Assert.AreEqual(0, result.CreatedSlipIds.Count, "確定数0なら伝票を作らない");
		var after = db.Single<TranHaibun>("where Id=@0", haibun.Id);
		Assert.AreEqual(1, after.EndFlag);
		Assert.AreEqual(0, after.JitsuSu);
		Assert.AreEqual(6, after.ShortSu);
		Assert.AreEqual(0, after.RelateNo2);
		Assert.AreEqual(0, db.Fetch<Tran00Uriage>("").Count);
		AssertRealReserve(db, 1, 0);
	}

	/// <summary>確定数は指示数(Su)を超えないようサーバ側で収める。欠品は Su − 確定数。</summary>
	[TestMethod]
	public void Commit_ClampsKakuteiSuToShiji() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var oroshiId = InsertTokui(db, "T011", "卸先", tenType: 1);
		var purchase = CreatePurchase("20260810", 1, 100, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 10);
		haibun.Id_Tenpo = oroshiId;
		db.Insert(haibun);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));

		shippingDb.Commit([(haibun.Id, VduOf(db, haibun.Id), 99)], "20260817", 1, out var outcome, out _);

		Assert.AreEqual(CommitOutcome.Success, outcome);
		var after = db.Single<TranHaibun>("where Id=@0", haibun.Id);
		Assert.AreEqual(10, after.JitsuSu);
		Assert.AreEqual(0, after.ShortSu);
		AssertRealStock(db, 1, 90);
	}

	/// <summary>一覧取得時点と違うVdu・完了済みの行が1件でもあれば競合。何も書かない。</summary>
	[TestMethod]
	public void Commit_ConcurrencyConflict_WritesNothing() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var oroshiId = InsertTokui(db, "T011", "卸先", tenType: 1);
		var purchase = CreatePurchase("20260810", 1, 100, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var haibun = CreateHaibun("20260815", 1, 10);
		haibun.Id_Tenpo = oroshiId;
		var done = CreateHaibun("20260815", 1, 3, endFlag: 1, kakuteiDay: "20260814", jitsuSu: 3);
		done.Id_Tenpo = oroshiId;
		db.Insert(haibun);
		db.Insert(done);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(haibun));
		var vdu = VduOf(db, haibun.Id);

		// Vdu不一致
		shippingDb.Commit([(haibun.Id, vdu + 1, 8)], "20260817", 1, out var outcome, out _);
		Assert.AreEqual(CommitOutcome.Conflict, outcome);
		// 完了済みの行が混ざる
		shippingDb.Commit([(haibun.Id, vdu, 8), (done.Id, VduOf(db, done.Id), 3)], "20260817", 1, out outcome, out _);
		Assert.AreEqual(CommitOutcome.Conflict, outcome);

		var after = db.Single<TranHaibun>("where Id=@0", haibun.Id);
		Assert.AreEqual(0, after.JitsuSu, "確定数は書かれていない");
		Assert.AreEqual(0, after.EndFlag, "完了していない");
		Assert.AreEqual("", after.KakuteiDay);
		Assert.AreEqual(0, db.Fetch<Tran00Uriage>("").Count);
		AssertRealReserve(db, 1, 10);
	}

	/// <summary>取置配分は店舗売上へ変換する別経路なので、配分確定では扱わない（何も書かない）</summary>
	[TestMethod]
	public void Commit_Reservation_IsRejected() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var purchase = CreatePurchase("20260810", 1, 100, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var reservation = CreateHaibun("20260815", 1, 2, kubun: EnumHaibun.Reservation);
		db.Insert(reservation);

		shippingDb.Commit([(reservation.Id, VduOf(db, reservation.Id), 2)], "20260817", 1, out var outcome, out _);

		Assert.AreEqual(CommitOutcome.InvalidKubun, outcome);
		Assert.AreEqual(0, db.Single<TranHaibun>("where Id=@0", reservation.Id).EndFlag);
	}

	/// <summary>
	/// 仕入配分(Kubun=0)も確定時は自分の確定数を必ず在庫から差し引いて検査する（P6）。
	/// 発注に紐付かない既存の初回配分は migration で入荷済み扱い(ArrivedSu=Su)になるので、それを再現する。
	/// 入荷済み8が引当に入っても、自分の引当分は検査で差し引くので有効在庫(確定前)は実在庫5になる。
	/// </summary>
	[TestMethod]
	public void Commit_HatsukaiQtyIsCheckedAgainstStock() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var purchase = CreatePurchase("20260810", 1, 5, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var hatsukai = CreateHaibun("20260815", 1, 8, kubun: EnumHaibun.Hatsukai);
		hatsukai.ArrivedSu = 8;
		db.Insert(hatsukai);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(hatsukai));
		AssertRealReserve(db, 1, 8);

		shippingDb.Commit([(hatsukai.Id, VduOf(db, hatsukai.Id), 8)], "20260817", 1, out var outcome, out var errors);

		Assert.AreEqual(CommitOutcome.Shortage, outcome);
		Assert.AreEqual(5, errors[0].Yuko);
		Assert.AreEqual(8, errors[0].Shiji);
	}

	/// <summary>
	/// 旧状態「確定済み・未出荷」(KakuteiDay有効・EndFlag=0)の行も確定でき、確定日は上書きされる（移行時の扱い）。
	/// </summary>
	[TestMethod]
	public void Commit_LegacyConfirmedRow_CanBeCommitted() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var purchase = CreatePurchase("20260810", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var legacy = CreateHaibun("20260815", 1, 10, kakuteiDay: "20260816");
		db.Insert(legacy);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(legacy));

		var result = shippingDb.Commit([(legacy.Id, VduOf(db, legacy.Id), 10)], "20260820", 1, out var outcome, out _);

		Assert.AreEqual(CommitOutcome.Success, outcome);
		Assert.AreEqual(1, result.CreatedSlipIds.Count);
		var after = db.Single<TranHaibun>("where Id=@0", legacy.Id);
		Assert.AreEqual("20260820", after.KakuteiDay);
		Assert.AreEqual(1, after.EndFlag);
		AssertRealStock(db, 1, 0);
		AssertRealReserve(db, 1, 0);
	}

	/// <summary>
	/// 旧 ConfirmShipping は KakuteiDay を立てるだけで引当を引き直さなかったため、「確定済み・未出荷」行のキーには
	/// 未確定時の引当数(Su)が残っていることがある。配分確定は検査前に引き直すので、偽の在庫割れにならない。
	/// </summary>
	[TestMethod]
	public void Commit_LegacyConfirmedRowWithStaleReserve_IsNotFalseShortage() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var purchase = CreatePurchase("20260810", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var legacy = CreateHaibun("20260815", 1, 10);
		db.Insert(legacy);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(legacy));
		// 旧方式の確定: KakuteiDay だけを立て、引当は引き直さない（保存済み引当10が残る）
		db.Execute("update TranHaibun set KakuteiDay='20260816' where Id=@0", legacy.Id);
		AssertRealReserve(db, 1, 10);

		var result = shippingDb.Commit([(legacy.Id, VduOf(db, legacy.Id), 10)], "20260820", 1, out var outcome, out var errors);

		Assert.AreEqual(CommitOutcome.Success, outcome, string.Join(",", errors));
		Assert.AreEqual(1, result.CreatedSlipIds.Count);
		AssertRealStock(db, 1, 0);
		AssertRealReserve(db, 1, 0);
	}

	/// <summary>同じ倉庫+SKUの複数行は合計で在庫検査し、確定対象外の引当も差し引く</summary>
	[TestMethod]
	public void Commit_SameSkuRowsAreSummedWithOtherReserve() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		var purchase = CreatePurchase("20260810", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var a = CreateHaibun("20260815", 1, 4);
		var b = CreateHaibun("20260815", 1, 4);
		var other = CreateHaibun("20260815", 1, 3);
		db.Insert(a);
		db.Insert(b);
		db.Insert(other);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(a));
		AssertRealReserve(db, 1, 11);

		// 実在庫10 − 対象外引当3 = 7 に対し 4+4=8 → 割れ
		shippingDb.Commit([(a.Id, VduOf(db, a.Id), 4), (b.Id, VduOf(db, b.Id), 4)], "20260820", 1, out var outcome, out var errors);
		Assert.AreEqual(CommitOutcome.Shortage, outcome);
		Assert.AreEqual(8, errors.Single().Shiji);
		Assert.AreEqual(7, errors.Single().Yuko);

		// 片方を3にすれば 7 で足りる
		shippingDb.Commit([(a.Id, VduOf(db, a.Id), 4), (b.Id, VduOf(db, b.Id), 3)], "20260820", 1, out outcome, out _);
		Assert.AreEqual(CommitOutcome.Success, outcome);
		AssertRealStock(db, 1, 3);
		AssertRealReserve(db, 1, 3); // 対象外の引当3だけが残る
	}

	/// <summary>
	/// 棚卸開始処理は対象年月末時点の帳簿在庫を凍結し、棚卸確定処理は実棚数との差を
	/// 在庫調整伝票(Tran61Chosei)として起こす。
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
		var cnt = stocktakeDb.FixStocktake("202608", idShain: 1);

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
		stocktakeDb.FixStocktake("202608", idShain: 1);
		AssertRealStock(db, 1, 18);

		// 棚卸数を数え直して再確定する
		db.Execute("DELETE FROM Tran60Tana");
		db.Insert(CreateTana("20260831", 1, 21));
		stocktakeDb.FixStocktake("202608", idShain: 1);

		Assert.AreEqual(1, db.Fetch<Tran61Chosei>("where TanaMonth=@0", "202608").Count, "調整伝票は作り直しで1件のまま");
		AssertRealStock(db, 1, 21, "再確定後は最新の棚卸数に一致する");
		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(1, row.AdjustQty);
		Assert.AreEqual(row.InQty + row.OutQty + row.AdjustQty, row.Su);
	}

	/// <summary>
	/// 画面が使うストリーミング経路（Msg054 / Msg055 が呼ぶ入口）が、直接呼び出しと同じ結果になることを確認する。
	/// 確定処理はトランザクションで包まれる。
	/// </summary>
	[TestMethod]
	public async Task Stocktake_AsyncStream_ProducesSameResultAsDirectCall() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		db.Insert(CreateTana("20260831", 1, 18));
		var param = new StocktakeParameter("202608", 0, []);

		await foreach (var p in stocktakeDb.StartAsyncStream(param)) {
			Assert.IsFalse(p.IsError, $"{p.StepName}: {p.ErrorMessage}");
		}
		Assert.AreEqual(20, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).BookQty);

		await foreach (var p in stocktakeDb.FixAsyncStream(param)) {
			Assert.IsFalse(p.IsError, $"{p.StepName}: {p.ErrorMessage}");
		}

		AssertRealStock(db, 1, 18);
		Assert.AreEqual(-2, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).AdjustQty);
		Assert.AreEqual(1, db.Fetch<Tran61Chosei>("where TanaMonth=@0", "202608").Count);
	}

	/// <summary>倉庫を指定すると、その倉庫だけが処理対象になる</summary>
	[TestMethod]
	public void Stocktake_WithSokoFilter_TouchesOnlySelectedWarehouse() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		foreach (var soko in new[] { 1L, 2L }) {
			var purchase = CreatePurchase("20260810", soko, 20, EnumShiire.Shiire);
			db.Insert(purchase);
			ApplyImmediate(summaryDb, purchase, false);
			db.Insert(CreateTana("20260831", soko, 18));
		}

		stocktakeDb.StartStocktake("202608", [1]);
		stocktakeDb.FixStocktake("202608", idShain: 0, sokoIds: [1]);

		AssertRealStock(db, 1, 18, "指定した倉庫は確定される");
		AssertRealStock(db, 2, 20, "指定しなかった倉庫は動かない");
		Assert.AreEqual(0, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2).BookQty);
	}

	/// <summary>
	/// UAT-04通しシナリオ: 店舗ごとに違う棚卸日で「開始→入力→差異→確定→過去伝票修正→再確定」を通す。
	/// 店舗1は棚卸日8/25、店舗2は棚卸日8/31。8/28の仕入は店舗1の帳簿在庫からだけ差し引かれる。
	/// </summary>
	[TestMethod]
	public void Stocktake_EndToEnd_PerShopTanaDay() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		db.Insert(new Tran60TanaDate { Id_Shop = 2, TanaDay = "20260831" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);

		// 1) 仕入(店舗ごとに8月累計を作る)
		var p1a = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(p1a);
		ApplyImmediate(summaryDb, p1a, false);
		var p1b = CreatePurchase("20260828", 1, 5, EnumShiire.Shiire);
		db.Insert(p1b);
		ApplyImmediate(summaryDb, p1b, false);
		var p2a = CreatePurchase("20260810", 2, 30, EnumShiire.Shiire);
		db.Insert(p2a);
		ApplyImmediate(summaryDb, p2a, false);
		var p2b = CreatePurchase("20260828", 2, 7, EnumShiire.Shiire);
		db.Insert(p2b);
		ApplyImmediate(summaryDb, p2b, false);
		AssertRealStock(db, 1, 25, "前提: 店舗1の8月累計は20+5=25");
		AssertRealStock(db, 2, 37, "前提: 店舗2の8月累計は30+7=37");

		// 2) 棚卸開始。店舗1は棚卸日8/25なので8/28分(+5)を差し引き、店舗2は月末なので差し引かない
		stocktakeDb.StartStocktake("202608", [1L, 2L]);
		Assert.AreEqual(20, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).BookQty);
		Assert.AreEqual(37, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2).BookQty);

		// 3) 実棚入力(店舗ごとの基準日で)
		db.Insert(CreateTana("20260825", 1, 18));
		db.Insert(CreateTana("20260831", 2, 40));

		// 4) 確定。倉庫ごとに1伝票、計上日は店舗の棚卸基準日
		var days = stocktakeDb.ResolveDays("202608", [1L, 2L]);
		var result = stocktakeDb.FixStocktake(days, 1);

		Assert.IsFalse(result.IsConfirmationRequired);
		Assert.AreEqual(2, result.SlipCount, "店舗ごとに1伝票");
		var chosei1 = db.Single<Tran61Chosei>("where Id_Soko=@0", 1);
		Assert.AreEqual("20260825", chosei1.DenDay);
		Assert.AreEqual(-2, chosei1.SuTotal);
		var chosei2 = db.Single<Tran61Chosei>("where Id_Soko=@0", 2);
		Assert.AreEqual("20260831", chosei2.DenDay);
		Assert.AreEqual(3, chosei2.SuTotal);
		AssertRealStock(db, 1, 23, "店舗1: 仕入25 + 調整-2 = 23");
		AssertRealStock(db, 2, 40, "店舗2: 仕入37 + 調整+3 = 40");

		// 5) 確定直後は両店舗とも再確定不要
		var status = stocktakeDb.FetchRefixStatus(days);
		Assert.IsTrue(status.All(x => x.IsFixed));
		Assert.IsFalse(status.Any(x => x.IsRefixRequired), "確定直後は再確定不要");

		// 6) 過去伝票の修正(店舗1の8/10の仕入伝票だけ)
		p1a.Vdu = CvAsset.Common.GetVdate() + 1;
		db.Update(p1a);

		var statusAfterEdit = stocktakeDb.FetchRefixStatus(days);
		Assert.IsTrue(statusAfterEdit.Single(x => x.Id_Soko == 1).IsRefixRequired, "店舗1は基準日以前の伝票が確定後に更新された");
		Assert.IsFalse(statusAfterEdit.Single(x => x.Id_Soko == 2).IsRefixRequired, "店舗2の伝票は触っていない");

		// 7) 店舗1だけ再確定。前回の調整伝票は置き換わり、増えない
		var result2 = stocktakeDb.FixStocktake(stocktakeDb.ResolveDays("202608", [1L]), 1);

		Assert.AreEqual(1, result2.SlipCount);
		Assert.AreEqual(1, db.Fetch<Tran61Chosei>("where Id_Soko=@0", 1).Count, "前回分が置き換わり増えていない");
		AssertRealStock(db, 1, 23, "再確定で二重計上されない");
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
		stocktakeDb.FixStocktake("202608", idShain: 1);
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

	/// <summary>
	/// 基準日時点の帳簿在庫の逆算。基準日より後・計上月末までの伝票増減を
	/// 月末累計から差し引くことで、月次スナップショットしか持たない SummaryStock から
	/// 任意日時点の帳簿在庫を復元できることを確認する。
	/// </summary>
	[TestMethod]
	public void FetchBookQtyAsOf_ExcludesSlipsAfterBaseDay() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var early = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(early);
		ApplyImmediate(summaryDb, early, false);
		var late = CreatePurchase("20260828", 1, 5, EnumShiire.Shiire);
		db.Insert(late);
		ApplyImmediate(summaryDb, late, false);
		Assert.AreEqual(25, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).Su,
			"前提: 8月分の累計は20+5=25");

		var beforeLate = StocktakeDaySet.Resolve(1, "20260825", 99, "202608");
		Assert.AreEqual(20, GetBookQty(stocktakeDb.FetchBookQtyAsOf(beforeLate)), "基準日より後の8/28分(+5)を差し引く");

		var atMonthEnd = StocktakeDaySet.Resolve(1, "20260831", 99, "202608");
		Assert.AreEqual(25, GetBookQty(stocktakeDb.FetchBookQtyAsOf(atMonthEnd)), "基準日が月末なら差し引く伝票が無い");
	}

	/// <summary>逆算条件は `DenDay > 基準日` なので、基準日当日の伝票は帳簿在庫に含まれる(の仕様)</summary>
	[TestMethod]
	public void FetchBookQtyAsOf_IncludesSlipOnBaseDay() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var early = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(early);
		ApplyImmediate(summaryDb, early, false);
		var onBaseDay = CreatePurchase("20260825", 1, 6, EnumShiire.Shiire);
		db.Insert(onBaseDay);
		ApplyImmediate(summaryDb, onBaseDay, false);

		var day = StocktakeDaySet.Resolve(1, "20260825", 99, "202608");

		Assert.AreEqual(26, GetBookQty(stocktakeDb.FetchBookQtyAsOf(day)), "基準日当日の伝票は差し引かず帳簿在庫に含む");
	}

	/// <summary>計上月をまたいで前月以前の SummaryStock 行が累計へ繰り越されることを確認する</summary>
	[TestMethod]
	public void FetchBookQtyAsOf_CarriesOverPreviousMonths() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var lastMonth = CreatePurchase("20260710", 1, 30, EnumShiire.Shiire);
		db.Insert(lastMonth);
		ApplyImmediate(summaryDb, lastMonth, false);
		var thisMonth = CreatePurchase("20260820", 1, 4, EnumShiire.Shiire);
		db.Insert(thisMonth);
		ApplyImmediate(summaryDb, thisMonth, false);

		var midMonth = StocktakeDaySet.Resolve(1, "20260815", 99, "202608");
		Assert.AreEqual(30, GetBookQty(stocktakeDb.FetchBookQtyAsOf(midMonth)), "当月分(8/20の+4)は基準日より後なので差し引く");

		var monthEnd = StocktakeDaySet.Resolve(1, "20260831", 99, "202608");
		Assert.AreEqual(34, GetBookQty(stocktakeDb.FetchBookQtyAsOf(monthEnd)), "月末なら前月30+当月4");
	}

	/// <summary>
	/// 移動(即時)は発側 Id_Soko と着側 Id_Ido の両軸に効く。基準日を移動前に取ると
	/// 両倉庫とも移動前の状態(発側は減る前、着側は増える前)へ戻ることを確認する。
	/// </summary>
	[TestMethod]
	public void FetchBookQtyAsOf_HandlesTransferOnBothAxes() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260801", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var transfer = CreateTransfer<Tran05Ido>("20260820", 1, 2, 7);
		db.Insert(transfer);
		ApplyImmediate(summaryDb, transfer, false);
		Assert.AreEqual(3, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).Su, "前提: 発側は10-7=3");
		Assert.AreEqual(7, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2).Su, "前提: 着側は7");

		var beforeTransfer = StocktakeDaySet.Resolve(1, "20260815", 99, "202608");
		Assert.AreEqual(10, GetBookQty(stocktakeDb.FetchBookQtyAsOf(beforeTransfer)), "移動前は発側が10のまま");
		var beforeTransferDest = StocktakeDaySet.Resolve(2, "20260815", 99, "202608");
		Assert.AreEqual(0, GetBookQty(stocktakeDb.FetchBookQtyAsOf(beforeTransferDest)), "移動前は着側は未着で0");

		var afterTransfer = StocktakeDaySet.Resolve(1, "20260825", 99, "202608");
		Assert.AreEqual(3, GetBookQty(stocktakeDb.FetchBookQtyAsOf(afterTransfer)), "移動後は発側が3");
		var afterTransferDest = StocktakeDaySet.Resolve(2, "20260825", 99, "202608");
		Assert.AreEqual(7, GetBookQty(stocktakeDb.FetchBookQtyAsOf(afterTransferDest)), "移動後は着側が7");
	}

	/// <summary>
	/// 移動中(TransitQty)は Su の外側の内訳列であり逆算の加減算対象にならない。
	/// 移動出庫だけ(未入庫)の状態では着側の Su も TransitQty も帳簿在庫に反映されないことを確認する。
	/// </summary>
	[TestMethod]
	public void FetchBookQtyAsOf_IgnoresTransitQty() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260801", 1, 10, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		var transitOut = CreateTransfer<Tran10IdoOut>("20260820", 1, 2, 7);
		db.Insert(transitOut);
		ApplyImmediate(summaryDb, transitOut, false);
		var destRow = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2);
		Assert.AreEqual(0, destRow.Su, "前提: 未入庫のため着側Suは0");
		Assert.AreEqual(7, destRow.TransitQty, "前提: 移動中数量は7");

		var beforeTransit = StocktakeDaySet.Resolve(2, "20260815", 99, "202608");
		Assert.AreEqual(0, GetBookQty(stocktakeDb.FetchBookQtyAsOf(beforeTransit)), "移動中は実地棚卸で数えられないので帳簿在庫に含めない");

		var afterTransit = StocktakeDaySet.Resolve(2, "20260825", 99, "202608");
		Assert.AreEqual(0, GetBookQty(stocktakeDb.FetchBookQtyAsOf(afterTransit)), "基準日を移動出庫日より後にしても着側は0のまま");
	}

	/// <summary>
	/// 棚卸開始処理は計上月末の帳簿在庫を8桁(yyyyMMdd)で保存する。
	/// 旧実装は計上月(6桁 "202608")をそのまま <see cref="SummaryStock.StocktakeDdate"/> に書いていた不具合があった。
	/// </summary>
	[TestMethod]
	public void StartStocktake_WritesEightDigitStocktakeDate() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);

		stocktakeDb.StartStocktake("202608");

		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual("20260831", row.StocktakeDdate, "棚卸日未設定なら計上月末が8桁で入る");
		Assert.AreEqual(20, row.BookQty);
	}

	/// <summary>
	/// 店舗別棚卸日(<see cref="Tran60TanaDate"/>)の核心動作。店舗ごとに違う基準日で帳簿在庫が凍結される。
	/// </summary>
	[TestMethod]
	public void StartStocktake_UsesPerShopTanaDay() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		db.Insert(new Tran60TanaDate { Id_Shop = 2, TanaDay = "20260831" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var soko1Early = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(soko1Early);
		ApplyImmediate(summaryDb, soko1Early, false);
		var soko1Late = CreatePurchase("20260828", 1, 5, EnumShiire.Shiire);
		db.Insert(soko1Late);
		ApplyImmediate(summaryDb, soko1Late, false);
		var soko2Early = CreatePurchase("20260810", 2, 30, EnumShiire.Shiire);
		db.Insert(soko2Early);
		ApplyImmediate(summaryDb, soko2Early, false);
		var soko2Late = CreatePurchase("20260828", 2, 7, EnumShiire.Shiire);
		db.Insert(soko2Late);
		ApplyImmediate(summaryDb, soko2Late, false);
		Assert.AreEqual(25, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).Su, "前提: soko1の8月累計は20+5=25");
		Assert.AreEqual(37, db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2).Su, "前提: soko2の8月累計は30+7=37");

		stocktakeDb.StartStocktake("202608", [1L, 2L]);

		var soko1 = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(20, soko1.BookQty, "soko1は棚卸日8/25なので8/28の+5を差し引く");
		Assert.AreEqual("20260825", soko1.StocktakeDdate);
		var soko2 = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 2);
		Assert.AreEqual(37, soko2.BookQty, "soko2は棚卸日が月末なので差し引かない");
		Assert.AreEqual("20260831", soko2.StocktakeDdate);
	}

	/// <summary>調整伝票の計上日は店舗ごとの棚卸基準日になる</summary>
	[TestMethod]
	public void FixStocktake_UsesPerShopTanaDayForChoseiSlip() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));

		var result = stocktakeDb.FixStocktake(days, 1);

		Assert.AreEqual(1, result.SlipCount);
		Assert.IsFalse(result.IsConfirmationRequired);
		var chosei = db.Single<Tran61Chosei>("");
		Assert.AreEqual("20260825", chosei.DenDay, "調整伝票の計上日は店舗の棚卸基準日");
		Assert.AreEqual("202608", chosei.TanaMonth);
		Assert.AreEqual(-2, chosei.SuTotal);
		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(18, row.ActualQty);
		Assert.AreEqual(-2, row.AdjustQty);
	}

	/// <summary>棚番違いの複数の棚卸伝票は合計して1件の調整伝票にまとめる</summary>
	[TestMethod]
	public void FixStocktake_SumsMultipleTanaSlipsOnBaseDay() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 10));
		db.Insert(CreateTana("20260825", 1, 8));

		var result = stocktakeDb.FixStocktake(days, 1);

		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(18, row.ActualQty, "棚番違いの複数伝票は合計する");
		Assert.AreEqual(1, result.SlipCount);
		Assert.AreEqual(-2, db.Single<Tran61Chosei>("").SuTotal);
	}

	/// <summary>基準日以外の棚卸入力があると、何も変更せず中断して内訳(Misdated)を返す</summary>
	[TestMethod]
	public void FixStocktake_AbortsWhenTanaDateMismatch() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		// 基準日8/25と違うが計上月(8月)内の棚卸入力
		db.Insert(CreateTana("20260820", 1, 18));

		var result = stocktakeDb.FixStocktake(days, 1);

		Assert.IsTrue(result.IsConfirmationRequired);
		Assert.AreEqual(0, result.SlipCount);
		Assert.AreEqual(0, result.AlignedCount);
		Assert.AreEqual(1, result.Misdated.Count);
		Assert.AreEqual("20260820", result.Misdated[0].DenDay);
		Assert.AreEqual(1, result.Misdated[0].SlipCount);
		Assert.AreEqual(0, db.Fetch<Tran61Chosei>("").Count, "調整伝票は作られない");
		var tana = db.Single<Tran60Tana>("");
		Assert.AreEqual("20260820", tana.DenDay, "棚卸伝票の計上日は補正されない");
	}

	/// <summary>補正(alignMisdated)を指示すると、日付違いの棚卸伝票を基準日へ揃えてから確定する</summary>
	[TestMethod]
	public void FixStocktake_AlignsMisdatedTanaWhenConfirmed() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260820", 1, 18));

		var result = stocktakeDb.FixStocktake(days, 1, alignMisdated: true);

		Assert.AreEqual(1, result.AlignedCount);
		Assert.AreEqual(1, result.SlipCount);
		var tana = db.Single<Tran60Tana>("");
		Assert.AreEqual("20260825", tana.DenDay, "棚卸伝票の計上日は基準日へ補正される");
		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(18, row.ActualQty);
		Assert.AreEqual(-2, db.Single<Tran61Chosei>("").SuTotal);
	}

	/// <summary>計上月の外にある棚卸入力は別の月の棚卸なので、日付違いの検知にも集計にも含めない</summary>
	[TestMethod]
	public void FixStocktake_IgnoresTanaOutsideAccountingMonth() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));
		db.Insert(CreateTana("20260705", 1, 99));

		var result = stocktakeDb.FixStocktake(days, 1);

		Assert.IsFalse(result.IsConfirmationRequired, "7月分は8月の棚卸確定の検知対象外");
		Assert.AreEqual(1, result.SlipCount);
		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(18, row.ActualQty, "7月分の99は混ざらない");
	}

	/// <summary>確定処理の実行日は再確定要否判定の基準として Tran60TanaDate.FixDay に書かれる</summary>
	[TestMethod]
	public void FixStocktake_WritesFixDay() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));
		Assert.AreEqual("19010101", db.Single<Tran60TanaDate>("where Id_Shop=@0", 1).FixDay, "前提: 未確定");

		stocktakeDb.FixStocktake(days, 1);

		Assert.AreEqual(
			DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			db.Single<Tran60TanaDate>("where Id_Shop=@0", 1).FixDay);
	}

	/// <summary>当月に動きが無い在庫は当該計上月の行を持たないので、行補完しないと帳簿在庫が記録できない</summary>
	[TestMethod]
	public void StartStocktake_CompletesRowForSkuWithoutCurrentMonthRow() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var lastMonth = CreatePurchase("20260710", 1, 12, EnumShiire.Shiire);
		db.Insert(lastMonth);
		ApplyImmediate(summaryDb, lastMonth, false);
		Assert.AreEqual(0, db.Fetch<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1).Count, "前提: 202608/soko1の行は無い");

		stocktakeDb.StartStocktake("202608", [1L]);

		var row = db.Single<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(12, row.BookQty, "行が作られ帳簿在庫が記録される");
		Assert.AreEqual(0, row.Su);
		Assert.AreEqual("20260831", row.StocktakeDdate);
	}

	/// <summary>在庫履歴が無く実棚入力だけあるSKUにも行を作る</summary>
	[TestMethod]
	public void StartStocktake_CompletesRowForCountedOnlySku() {
		var db = PrepareAllStockTables();
		var stocktakeDb = new StocktakeDb(db);
		db.Insert(CreateTana("20260831", 1, 3, 20L, 200L, 2000L));

		stocktakeDb.StartStocktake("202608", [1L]);

		var row = db.Single<SummaryStock>(
			"where SumMonth=@0 and Id_Soko=@1 and Id_Shohin=@2 and Id_Col=@3 and Id_Siz=@4",
			"202608", 1, 20L, 200L, 2000L);
		Assert.AreEqual(0, row.BookQty, "在庫履歴が無いSKUの帳簿在庫は0");
	}

	/// <summary>何度実行しても同じ結果になり、行補完が重複行を作らない</summary>
	[TestMethod]
	public void StartStocktake_IsIdempotent() {
		var db = PrepareAllStockTables();
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);

		stocktakeDb.StartStocktake("202608", [1L]);
		stocktakeDb.StartStocktake("202608", [1L]);

		var rows = db.Fetch<SummaryStock>("where SumMonth=@0 and Id_Soko=@1", "202608", 1);
		Assert.AreEqual(1, rows.Count, "行補完は重複行を作らない");
		Assert.AreEqual(20, rows[0].BookQty);
	}

	/// <summary>棚卸開始前は IsStarted/IsFixed/IsRefixRequired が全て false になる</summary>
	[TestMethod]
	public void FetchRefixStatus_ReportsNotStartedBeforeStart() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);

		var days = stocktakeDb.ResolveDays("202608", [1L]);
		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.AreEqual(1, st.Count);
		Assert.IsFalse(st[0].IsStarted, "棚卸開始処理をまだ実行していない");
		Assert.IsFalse(st[0].IsFixed);
		Assert.IsFalse(st[0].IsRefixRequired);
		Assert.AreEqual("20260825", st[0].TanaDay);
		Assert.AreEqual("202608", st[0].SumMonth);
		Assert.IsFalse(st[0].IsFallback);
	}

	/// <summary>棚卸開始処理を実行すると IsStarted が true になる</summary>
	[TestMethod]
	public void FetchRefixStatus_ReportsStartedAfterStart() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);

		var days = stocktakeDb.ResolveDays("202608", [1L]);
		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.IsTrue(st[0].IsStarted);
		Assert.IsFalse(st[0].IsFixed);
		Assert.IsFalse(st[0].IsRefixRequired);
	}

	/// <summary>確定直後は再確定不要。確定処理が作った調整伝票自身が再確定要を誘発しないことの担保でもある</summary>
	[TestMethod]
	public void FetchRefixStatus_ReportsFixedAfterFix() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));
		stocktakeDb.FixStocktake(days, 1);

		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.IsTrue(st[0].IsStarted);
		Assert.IsTrue(st[0].IsFixed);
		Assert.IsFalse(st[0].IsRefixRequired, "確定直後は再確定不要");
		Assert.AreNotEqual(CvBase.StocktakeDaySet.UnsetDay, st[0].FixDay);
	}

	/// <summary>確定後に基準日以前の伝票を修正すると再確定要になる(本判定の核)</summary>
	[TestMethod]
	public void FetchRefixStatus_RequiresRefixWhenPastSlipChanged() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));
		stocktakeDb.FixStocktake(days, 1);
		Assert.IsFalse(stocktakeDb.FetchRefixStatus(days)[0].IsRefixRequired, "前提: 確定直後は再確定不要");

		// 基準日以前の仕入伝票を修正して Vdu を進める(確定時刻より確実に後にする)
		var target = db.Single<Tran03Shiire>("where DenDay=@0", "20260810");
		target.Vdu = CvAsset.Common.GetVdate() + 1;
		db.Update(target);

		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.IsTrue(st[0].IsFixed);
		Assert.IsTrue(st[0].IsRefixRequired, "確定後に基準日以前の伝票が更新されたので再確定要");
	}

	/// <summary>基準日より後の伝票を修正しても再確定要にはならない(基準日時点の棚卸には影響しない)</summary>
	[TestMethod]
	public void FetchRefixStatus_IgnoresSlipAfterBaseDay() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		db.Insert(new Tran60TanaDate { Id_Shop = 1, TanaDay = "20260825" });
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var early = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(early);
		ApplyImmediate(summaryDb, early, false);
		var late = CreatePurchase("20260828", 1, 5, EnumShiire.Shiire);
		db.Insert(late);
		ApplyImmediate(summaryDb, late, false);
		stocktakeDb.StartStocktake("202608", [1L]);
		var days = stocktakeDb.ResolveDays("202608", [1L]);
		db.Insert(CreateTana("20260825", 1, 18));
		stocktakeDb.FixStocktake(days, 1);

		// 基準日(8/25)より後の8/28の伝票だけを修正する
		var target = db.Single<Tran03Shiire>("where DenDay=@0", "20260828");
		target.Vdu = CvAsset.Common.GetVdate() + 1;
		db.Update(target);

		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.IsFalse(st[0].IsRefixRequired, "基準日より後の伝票の修正は再確定要にならない");
	}

	/// <summary>棚卸日未設定の店舗は計上月末へフォールバックする</summary>
	[TestMethod]
	public void FetchRefixStatus_FallsBackWhenTanaDayUnset() {
		var db = PrepareAllStockTables();
		db.CreateTable(typeof(Tran60TanaDate), true, false);
		var summaryDb = new SummaryDb(db);
		var stocktakeDb = new StocktakeDb(db);
		var purchase = CreatePurchase("20260810", 1, 20, EnumShiire.Shiire);
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		stocktakeDb.StartStocktake("202608", [1L]);

		var days = stocktakeDb.ResolveDays("202608", [1L]);
		var st = stocktakeDb.FetchRefixStatus(days);

		Assert.IsTrue(st[0].IsFallback, "棚卸日未設定なら計上月末へフォールバック");
		Assert.AreEqual("20260831", st[0].TanaDay);
		Assert.IsTrue(st[0].IsStarted);
		Assert.IsFalse(st[0].IsFixed);
	}

	/// <summary>既定SKU(10/100/1000)のBookQtyを取り出す。行が無ければ0(帳簿在庫の履歴も棚卸入力も無いSKU)</summary>
	private static int GetBookQty(
		System.Collections.Generic.List<StocktakeDb.StocktakeBookQty> rows,
		long idShohin = 10,
		long idCol = 100,
		long idSiz = 1000) =>
		rows.Where(x => x.Id_Shohin == idShohin && x.Id_Col == idCol && x.Id_Siz == idSiz)
			.Select(x => x.BookQty)
			.DefaultIfEmpty(0)
			.First();

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
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { ShimeBi = 99 });
		// IsZaiko の判定に使うので在庫更新の全テストで必要。行が無ければ IsZaiko=1 相当として扱う(SummaryDb側でCOALESCE)
		db.CreateTable(typeof(MasterTokui), true, false);
		db.CreateTable(typeof(MasterShohin), true, false);
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

	// ===== 仕入配分の入荷割当（配分再設計 Step 4） =====

	/// <summary>発注に紐付く仕入を登録し、在庫と入荷割当を反映する（画面の仕入保存と同じ順序）</summary>
	private static void ReceivePurchase(ExDatabaseSqlite db, SummaryDb summaryDb, long hachuId, long idSoko, int su, EnumShiire kubun = EnumShiire.Shiire) {
		var purchase = CreatePurchase("20260901", idSoko, su, kubun);
		purchase.RelateNo1 = hachuId;
		db.Insert(purchase);
		ApplyImmediate(summaryDb, purchase, false);
		new ArrivalDb(db).Recalc([hachuId]);
	}

	private static TranHaibun CreateReceiptAllocation(long hachuId, long idTenpo, int su) {
		var h = CreateHaibun("20260901", 1, su, kubun: EnumHaibun.Hatsukai);
		h.RelateNo1 = (int)hachuId;
		h.Id_Tenpo = idTenpo;
		return h;
	}

	private static int ArrivedOf(ExDatabaseSqlite db, long id) => db.Single<TranHaibun>("where Id=@0", id).ArrivedSu;

	/// <summary>
	/// 部分入荷は配分先の店舗コード順に入荷済み数を割り当て、その分だけが引当に入る。
	/// 追加の仕入で残りが割り当たり、仕入返品で減る。別の倉庫への仕入は数えない。
	/// </summary>
	[TestMethod]
	public void Arrival_PartialReceipt_FillsByTenpoCodeOrderAndFollowsReturns() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		const long hachuId = 777;
		var tenpoB = InsertTokui(db, "T002", "店舗B", tenType: 6);
		var tenpoA = InsertTokui(db, "T001", "店舗A", tenType: 6);
		// 行は B を先に作るが、割当は店舗コード順（A→B）
		var toB = CreateReceiptAllocation(hachuId, tenpoB, 4);
		var toA = CreateReceiptAllocation(hachuId, tenpoA, 6);
		db.Insert(toB);
		db.Insert(toA);
		summaryDb.CalcHaibun2Reserve(ReserveKey.From(toA));
		AssertRealReserve(db, 1, 0);

		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 5);
		Assert.AreEqual(5, ArrivedOf(db, toA.Id), "店舗コードの若いAから割り当てる");
		Assert.AreEqual(0, ArrivedOf(db, toB.Id));
		AssertRealReserve(db, 1, 5);

		ReceivePurchase(db, summaryDb, hachuId, idSoko: 2, su: 9);
		Assert.AreEqual(5, ArrivedOf(db, toA.Id), "別の倉庫への仕入は数えない");

		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 5);
		Assert.AreEqual(6, ArrivedOf(db, toA.Id));
		Assert.AreEqual(4, ArrivedOf(db, toB.Id));
		AssertRealReserve(db, 1, 10);

		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 3, EnumShiire.Henpin);
		Assert.AreEqual(6, ArrivedOf(db, toA.Id));
		Assert.AreEqual(1, ArrivedOf(db, toB.Id), "仕入返品で入荷済みが減る（後ろの優先順位から）");
		AssertRealReserve(db, 1, 7);

		var incremental = GetReserveSnapshot(db);
		summaryDb.CalcReserveQtyAll();
		CollectionAssert.AreEqual(incremental, GetReserveSnapshot(db), "通常更新値とRebuild値は一致する");
	}

	/// <summary>
	/// 仕入配分は入荷済み数を超えて確定できない（何も書かない）。確定で消費した入荷数は差し引かれ、
	/// 欠品で余った入荷は次の優先順位の行へ回る。
	/// </summary>
	[TestMethod]
	public void Arrival_CommitWithinArrivedAndLeftoverMovesToNextRow() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		const long hachuId = 888;
		var tenpoA = InsertTokui(db, "T001", "店舗A", tenType: 6);
		var tenpoB = InsertTokui(db, "T002", "店舗B", tenType: 6);
		var toA = CreateReceiptAllocation(hachuId, tenpoA, 6);
		var toB = CreateReceiptAllocation(hachuId, tenpoB, 4);
		db.Insert(toA);
		db.Insert(toB);
		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 5);
		Assert.AreEqual(5, ArrivedOf(db, toA.Id));

		// 入荷済み5を超える6は確定できない
		shippingDb.Commit([(toA.Id, VduOf(db, toA.Id), 6)], "20260905", 1, out var outcome, out _);
		Assert.AreEqual(CommitOutcome.NotArrived, outcome);
		Assert.AreEqual(1, shippingDb.NotArrivedRows.Count);
		Assert.AreEqual(0, db.Single<TranHaibun>("where Id=@0", toA.Id).EndFlag, "何も確定しない");

		// 入荷済みのうち3だけ確定（欠品3）。余った入荷2はBへ回る
		var result = shippingDb.Commit([(toA.Id, VduOf(db, toA.Id), 3)], "20260905", 1, out outcome, out _);
		Assert.AreEqual(CommitOutcome.Success, outcome);
		Assert.AreEqual(1, result.CreatedSlipIds.Count, "直営店向けは移動伝票");
		Assert.AreEqual(2, ArrivedOf(db, toB.Id), "欠品で余った入荷はBへ回る");
		AssertRealReserve(db, 1, 2);
		AssertRealStock(db, 1, 2, "入荷5 − 移動3");

		// 確定済み3を差し引いた残りで割り当てる。追加の仕入5 → 入荷計10 − 消費3 = 7 → Bは上限4
		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 5);
		Assert.AreEqual(4, ArrivedOf(db, toB.Id));
		shippingDb.Commit([(toB.Id, VduOf(db, toB.Id), 4)], "20260906", 1, out outcome, out _);
		Assert.AreEqual(CommitOutcome.Success, outcome);
		AssertRealReserve(db, 1, 0);
		AssertRealStock(db, 1, 3, "入荷10 − 移動3 − 移動4");
	}

	/// <summary>
	/// 仕入配分を卸先へ確定しても、出荷売上の RelateNo1（受注Idの規約）に発注Idを入れない。
	/// 入れると同じ値のIdの受注の残を誤って消化する（Step 4 レビュー指摘）。指示取消（確定数0）の入荷は次の行へ回る。
	/// </summary>
	[TestMethod]
	public void Arrival_WholesalerCommitDoesNotLinkOrderAndZeroCommitMovesArrival() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var shippingDb = new ShippingDb(db);
		const long hachuId = 999;
		var oroshi = InsertTokui(db, "T001", "卸先", tenType: 1);
		var tenpo = InsertTokui(db, "T002", "店舗", tenType: 6);
		var toOroshi = CreateReceiptAllocation(hachuId, oroshi, 3);
		var toTenpo = CreateReceiptAllocation(hachuId, tenpo, 4);
		db.Insert(toOroshi);
		db.Insert(toTenpo);
		ReceivePurchase(db, summaryDb, hachuId, idSoko: 1, su: 5);
		Assert.AreEqual(3, ArrivedOf(db, toOroshi.Id));
		Assert.AreEqual(2, ArrivedOf(db, toTenpo.Id));

		shippingDb.Commit([(toOroshi.Id, VduOf(db, toOroshi.Id), 3)], "20260905", 1, out var outcome, out _);
		Assert.AreEqual(CommitOutcome.Success, outcome);
		var uriage = db.Single<Tran00Uriage>("where Id_Tokui=@0", oroshi);
		Assert.AreEqual(0, uriage.RelateNo1, "仕入配分の出荷売上は受注に紐付けない");

		// 店舗を指示取消（確定数0）しても、入荷2は消費されないので再割当の対象に残る（行は完了するので割当先は無い）
		shippingDb.Commit([(toTenpo.Id, VduOf(db, toTenpo.Id), 0)], "20260905", 1, out outcome, out _);
		Assert.AreEqual(CommitOutcome.Success, outcome);
		var again = CreateReceiptAllocation(hachuId, tenpo, 4);
		db.Insert(again);
		new ArrivalDb(db).Recalc([hachuId]);
		Assert.AreEqual(2, ArrivedOf(db, again.Id), "指示取消で使わなかった入荷は、新しい仕入配分へ回る");
		AssertRealReserve(db, 1, 2);
	}

	/// <summary>発注に紐付かない仕入配分は入荷割当の対象外（migrationで入荷済み扱いにした値をそのまま使う）</summary>
	[TestMethod]
	public void Arrival_UnlinkedHatsukaiKeepsArrivedSu() {
		var db = PrepareShippingTables();
		var summaryDb = new SummaryDb(db);
		var legacy = CreateHaibun("20260901", 1, 3, kubun: EnumHaibun.Hatsukai);
		legacy.ArrivedSu = 3;
		db.Insert(legacy);
		summaryDb.CalcReserveQtyAll();
		Assert.AreEqual(3, ArrivedOf(db, legacy.Id));
		AssertRealReserve(db, 1, 3);
	}

	/// <summary>DB上の配分行の現在のVdu（確定の楽観排他に渡す値）</summary>
	private static long VduOf(ExDatabaseSqlite db, long id) => db.Single<TranHaibun>("where Id=@0", id).Vdu;

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
