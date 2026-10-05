using System;
using System.Linq;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class AutoReplenishDbTests {
	private static string Day => DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
	private SqliteConnection? _anchor;
	private ExDatabaseSqlite _db = null!;
	private string _connectionString = string.Empty;
	private long _soko;
	private long _store;
	private long _shiire;
	private long _product;
	private AutoReplenishDb Replenish => new(_db);

	[TestInitialize]
	public void Initialize() {
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = "AutoReplenish-" + Guid.NewGuid().ToString("N"),
			Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared,
		}.ToString();
		_connectionString = connectionString;
		_anchor = new SqliteConnection(connectionString);
		_anchor.Open();
		var connection = new SqliteConnection(connectionString);
		connection.Open();
		_db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		Type[] tables = [typeof(MasterAutoReplenishStock), typeof(MasterAutoReplenishExclude), typeof(TranAutoReplenishBatch),
			typeof(TranHoju), typeof(TranHaibun), typeof(Tran13Hachu), typeof(Tran03Shiire), typeof(SummaryRealStock),
			typeof(SummaryStock), typeof(MasterShohin), typeof(DerivedShohinColSiz), typeof(MasterTokui),
			typeof(MasterShiire), typeof(MasterShain), typeof(MasterSysman), typeof(Tran60TanaDate), typeof(DerivedJodai)];
		foreach (var table in tables) Assert.IsTrue(_db.CreateTable(table, true, false), table.Name);
		// SummaryStockは基底型の索引属性も継承するため、実運用と同じ一意キーを明示する。
		_db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock(SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		_db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock(Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		_db.Execute("CREATE UNIQUE INDEX AutoStock_unq1 ON MasterAutoReplenishStock(Id_Tenpo, Id_Shohin, Id_Col, Id_Siz)");
		_db.Execute("CREATE UNIQUE INDEX AutoExclude_unq1 ON MasterAutoReplenishExclude(Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		_db.Execute("CREATE UNIQUE INDEX AutoBatch_execution ON TranAutoReplenishBatch(ExecutionKey)");
		_db.Execute("CREATE UNIQUE INDEX AutoBatch_active ON TranAutoReplenishBatch(ActiveKey)");
		_db.Insert(new MasterSysman { ShimeBi = 99 });
		_soko = Insert(new MasterTokui { Code = "S01", Name = "倉庫", TenType = 0 });
		_store = AddStore("T01");
		_shiire = Insert(new MasterShiire { Code = "P01", Name = "仕入先", LeadTimeDays = 3 });
		_product = Insert(new MasterShohin { Code = "A001", Name = "通常商品", IsZaiko = 1,
			PurchaseType = 0, Id_ConsignmentShiire = _shiire, TankaGenka = 100, TankaJodai = 200, Id_Tax = 0 });
		Insert(new DerivedShohinColSiz { Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Code_Col = "01", Code_Siz = "M" });
		AddSetting(_store, 10);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchor?.Close();
	}

	[TestMethod]
	public void Preview_MissingStockRowsStillProducesStoreDemandWithoutWriting() {
		var preview = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		Assert.AreEqual(0, preview.Errors.Count);
		var row = preview.Rows.Single().Row;
		Assert.AreEqual((_store, 10, 0, 0, 10, _shiire),
			(row.Id_Tenpo, row.DemandSu, row.TransferSu, row.CoveredSu, row.Su, row.Id_Shiire));
		Assert.AreEqual(0, _db.Fetch<TranHoju>().Count);
		Assert.AreEqual(0, _db.Fetch<TranAutoReplenishBatch>().Count);
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
		Assert.AreEqual(0, _db.Fetch<TranHaibun>().Count);
	}

	[TestMethod]
	public void Preview_DuplicateOrderSkuPartialReceiptReturnAndBoundAllocationUseSupplyOnce() {
		var secondStore = AddStore("T02");
		AddSetting(secondStore, 10);
		var order = new Tran13Hachu { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire,
			Jmeisai = [Meisai(1, 8), Meisai(2, 7)] };
		Insert(order);
		Insert(new Tran03Shiire { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire,
			RelateNo1 = checked((int)order.Id), Kubun = 10, Jmeisai = [Meisai(1, 4)] });
		Insert(new Tran03Shiire { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire,
			RelateNo1 = checked((int)order.Id), Kubun = 20, Jmeisai = [Meisai(1, 1)] });
		Insert(new TranHaibun { DenDay = Day, Id_Soko = _soko, Id_Tenpo = _store,
			Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Kubun = 0, Su = 6, ArrivedSu = 2,
			RelateNo1 = checked((int)order.Id) });
		Insert(new SummaryRealStock { Id_Soko = _soko, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = 2, ReserveQty = 2 });
		var preview = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		Assert.AreEqual(0, preview.Errors.Count);
		var first = preview.Rows.Single(x => x.Row.Id_Tenpo == _store).Row;
		var second = preview.Rows.Single(x => x.Row.Id_Tenpo == secondStore).Row;
		Assert.AreEqual((4, 4, 0), (first.DemandSu, first.CoveredSu, first.Su));
		Assert.AreEqual((10, 4, 6), (second.DemandSu, second.CoveredSu, second.Su));
	}

	[TestMethod]
	public void Preview_OverallocatedPurchaseSupplyIsCappedAndNotCountedByTwoStores() {
		var other = AddStore("T02");
		AddSetting(other, 10);
		var order = Insert(new Tran13Hachu { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire, Jmeisai = [Meisai(1, 5)] });
		foreach (var store in new[] { _store, other }) Insert(new TranHaibun {
			DenDay = Day, Id_Soko = _soko, Id_Tenpo = store, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2,
			Kubun = 0, Su = 6, RelateNo1 = checked((int)order),
		});
		var preview = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		var first = preview.Rows.Single(x => x.Row.Id_Tenpo == _store && x.Row.DemandSu > 0).Row;
		var second = preview.Rows.Single(x => x.Row.Id_Tenpo == other && x.Row.DemandSu > 0).Row;
		Assert.AreEqual((5, 5), (first.DemandSu, first.Su));
		Assert.AreEqual((10, 10), (second.DemandSu, second.Su));
	}

	[TestMethod]
	public void Preview_TransitNetAcrossMonthsAndOtherSizesDoNotDuplicateSupply() {
		Insert(new SummaryStock { SumMonth = "202601", Id_Soko = _store, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, TransitQty = 5 });
		Insert(new SummaryStock { SumMonth = "202602", Id_Soko = _store, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, TransitQty = -2 });
		Insert(new SummaryRealStock { Id_Soko = _store, Id_Shohin = _product, Id_Col = 1, Id_Siz = 3, Su = 100 });
		Insert(new Tran13Hachu { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire, EndFlag = 1, Jmeisai = [Meisai(1, 50)] });
		var row = Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Single().Row;
		Assert.AreEqual((7, 0, 7), (row.DemandSu, row.CoveredSu, row.Su));
	}

	[TestMethod]
	public void Preview_DisabledWrongWeekdayAndExcludedSkuGenerateNoDemand() {
		_db.Execute("UPDATE Tran60TanaDate SET AutoHoju = 0 WHERE Id_Shop = @0", _store);
		Assert.AreEqual(0, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Sum(x => x.Row.DemandSu));
		_db.Execute("UPDATE Tran60TanaDate SET AutoHoju = @0 WHERE Id_Shop = @1", 127 ^ (1 << (int)DateTime.Today.DayOfWeek), _store);
		Assert.AreEqual(0, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Sum(x => x.Row.DemandSu));
		_db.Execute("UPDATE Tran60TanaDate SET AutoHoju = 127 WHERE Id_Shop = @0", _store);
		_db.Execute("UPDATE MasterAutoReplenishStock SET Enabled = 0");
		Assert.AreEqual(0, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Sum(x => x.Row.DemandSu));
		_db.Execute("UPDATE MasterAutoReplenishStock SET Enabled = 1");
		Insert(new MasterAutoReplenishExclude { Id_Soko = _soko, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Excluded = 1 });
		Assert.AreEqual(0, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Sum(x => x.Row.DemandSu));
	}

	[TestMethod]
	public void Preview_ConsumptionPurchaseAllowsStockTransferButRejectsAdditionalOrder() {
		_db.Execute("UPDATE MasterShohin SET PurchaseType = 3 WHERE Id = @0", _product);
		Insert(new SummaryRealStock { Id_Soko = _soko, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = 10 });
		var supplied = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		Assert.AreEqual(0, supplied.Errors.Count);
		Assert.AreEqual((10, 0), (supplied.Rows.Single().Row.TransferSu, supplied.Rows.Single().Row.Su));
		_db.Execute("UPDATE SummaryRealStock SET Su = 9");
		var shortage = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		Assert.AreEqual(1, shortage.Errors.Count);
		Assert.AreEqual((9, 1), (shortage.Rows.Single().Row.TransferSu, shortage.Rows.Single().Row.Su));
	}

	[TestMethod]
	public void Preview_MissingSupplierAndNegativeStockSettingAreErrors() {
		_db.Execute("UPDATE MasterShohin SET Id_ConsignmentShiire = 99999 WHERE Id = @0", _product);
		Assert.AreEqual(1, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Errors.Count);
		_db.Execute("UPDATE MasterShohin SET Id_ConsignmentShiire = @0 WHERE Id = @1", _shiire, _product);
		_db.Execute("UPDATE MasterAutoReplenishStock SET TargetSu = -1");
		Assert.AreEqual(1, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Errors.Count);
	}

	[TestMethod]
	public void SaveAndCommit_RetryCreatesOneOrderAndTransferAndOnlyReservesStock() {
		Insert(new SummaryRealStock { Id_Soko = _soko, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = 4 });
		var saved = SaveDraft(out var saveParam);
		Assert.AreEqual(saved.Batch!.Id, Replenish.Save(saveParam).Batch!.Id);
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
		Assert.AreEqual(0, _db.Fetch<TranHaibun>().Count);
		Assert.AreEqual(0, _db.Fetch<SummaryRealStock>().Single().ReserveQty);
		var commit = BatchParam(saved, AutoReplenishOperation.Commit);
		var effects = new WriteEffectRunner(_db);
		var confirmed = Replenish.Commit(commit, row => effects.After(WriteOp.Insert, row.GetType(), row, null, row.Vdu));
		var again = Replenish.Commit(commit);
		CollectionAssert.AreEqual(confirmed.CreatedHachuIds, again.CreatedHachuIds);
		CollectionAssert.AreEqual(confirmed.CreatedHaibunIds, again.CreatedHaibunIds);
		Assert.AreEqual((int)AutoReplenishStatus.Confirmed, again.Batch!.Status);
		var order = _db.Fetch<Tran13Hachu>().Single();
		var transfer = _db.Fetch<TranHaibun>().Single();
		Assert.AreEqual((15, _shiire, 6, 600L, 1200L, 600L),
			(order.Kubun, order.Id_Shiire, order.SuTotal, order.KingakuTotal, order.JodaiTotal, order.GedaiTotal));
		Assert.AreEqual(DateTime.Today.AddDays(3).ToString("yyyyMMdd"), order.NouhinDay);
		var detail = order.Jmeisai!.Single();
		Assert.AreEqual((_product, 1L, 2L, 6, 100, 600L),
			(detail.Id_Shohin, detail.Id_Col, detail.Id_Siz, detail.Su, detail.Tanka, detail.Kingaku));
		Assert.AreEqual((1, _store, 4, 0), (transfer.Kubun, transfer.Id_Tenpo, transfer.Su, transfer.EndFlag));
		Assert.AreEqual((4, 4), (_db.Fetch<SummaryRealStock>().Single().Su, _db.Fetch<SummaryRealStock>().Single().ReserveQty));
		var hoju = _db.Fetch<TranHoju>().Single();
		Assert.AreEqual((6, Day, order.Id, transfer.Id), (hoju.JitsuSu, hoju.KakuteiDay, hoju.GeneratedHachuId, hoju.GeneratedHaibunId));
		Assert.AreEqual(0, Replenish.Preview(Param(AutoReplenishOperation.Preview)).Rows.Sum(x => x.Row.Su), "既存配分4と発注6で次回需要を覆う");
	}

	[TestMethod]
	public void Commit_NewInputRowChangesFingerprintAndWritesNothing() {
		var saved = SaveDraft(out _);
		Insert(new SummaryStock { SumMonth = DateTime.Today.ToString("yyyyMM"), Id_Soko = _store,
			Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, TransitQty = 1 });
		Assert.ThrowsExactly<AutoReplenishConflictException>(() => Replenish.Commit(BatchParam(saved, AutoReplenishOperation.Commit)));
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
		Assert.AreEqual(0, _db.Fetch<TranHaibun>().Count);
		Assert.AreEqual((int)AutoReplenishStatus.Draft, _db.Fetch<TranAutoReplenishBatch>().Single().Status);
		Assert.AreEqual(string.Empty, _db.Fetch<TranHoju>().Single().KakuteiDay);
	}

	[TestMethod]
	public void Commit_TamperedSavedQuantityIsRejected() {
		var saved = SaveDraft(out _);
		_db.Execute("UPDATE TranHoju SET Su = 99");
		Assert.ThrowsExactly<AutoReplenishConflictException>(() => Replenish.Commit(BatchParam(saved, AutoReplenishOperation.Commit)));
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
		Assert.AreEqual((int)AutoReplenishStatus.Draft, Replenish.Load(saved.Batch!.Id).Batch!.Status);
	}

	[TestMethod]
	public void Commit_FailureAfterTransferAndOrderInsertionRollsEverythingBack() {
		Insert(new SummaryRealStock { Id_Soko = _soko, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = 4 });
		var saved = SaveDraft(out _);
		var insertCount = 0;
		Assert.ThrowsExactly<InvalidOperationException>(() => Replenish.Commit(BatchParam(saved, AutoReplenishOperation.Commit), row => {
			insertCount++;
			if (row is TranHaibun allocation) new SummaryDb(_db).CalcHaibun2Reserve([ReserveKey.From(allocation)]);
			if (row is Tran13Hachu) throw new InvalidOperationException("確定途中の障害");
		}));
		Assert.AreEqual(2, insertCount, "配分・発注挿入後に障害を発生させる");
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
		Assert.AreEqual(0, _db.Fetch<TranHaibun>().Count);
		Assert.AreEqual((4, 0), (_db.Fetch<SummaryRealStock>().Single().Su, _db.Fetch<SummaryRealStock>().Single().ReserveQty));
		Assert.AreEqual(0, _db.Fetch<SummaryStock>().Count);
		Assert.AreEqual((int)AutoReplenishStatus.Draft, _db.Fetch<TranAutoReplenishBatch>().Single().Status);
		Assert.AreEqual((0L, 0L, string.Empty), (_db.Fetch<TranHoju>().Single().GeneratedHachuId, _db.Fetch<TranHoju>().Single().GeneratedHaibunId, _db.Fetch<TranHoju>().Single().KakuteiDay));
	}

	[TestMethod]
	public void Cancel_CompetingDraftIsBlockedUntilCancellationAndCancelledCommitIsRejected() {
		var saved = SaveDraft(out var original);
		Assert.ThrowsExactly<AutoReplenishConflictException>(() => Replenish.Save(original with { ExecutionKey = Guid.NewGuid().ToString() }));
		var cancel = BatchParam(saved, AutoReplenishOperation.Cancel);
		var cancelled = Replenish.Cancel(cancel);
		Assert.AreEqual((int)AutoReplenishStatus.Cancelled, cancelled.Batch!.Status);
		Assert.AreEqual(saved.Batch!.Id, Replenish.Cancel(cancel).Batch!.Id);
		Assert.ThrowsExactly<AutoReplenishConflictException>(() => Replenish.Commit(BatchParam(cancelled, AutoReplenishOperation.Commit)));
		var next = Replenish.Save(original with { ExecutionKey = Guid.NewGuid().ToString() });
		Assert.AreNotEqual(saved.Batch.Id, next.Batch!.Id);
		Assert.AreEqual(2, _db.Fetch<TranAutoReplenishBatch>().Count);
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
	}

	[TestMethod]
	public void Save_ConcurrentExecutionKeysLeaveExactlyOneDraft() {
		var preview = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		using var gate = new System.Threading.Barrier(2);
		System.Threading.Tasks.Task<bool> Start() => System.Threading.Tasks.Task.Run(() => {
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();
			using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
			gate.SignalAndWait();
			try {
				new AutoReplenishDb(db).Save(Param(AutoReplenishOperation.Save) with {
					ExecutionKey = Guid.NewGuid().ToString(), Fingerprint = preview.Fingerprint });
				return true;
			}
			catch (AutoReplenishConflictException) { return false; }
			catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 or 19) { return false; }
		});
		var first = Start();
		var second = Start();
		System.Threading.Tasks.Task.WaitAll(first, second);
		Assert.AreEqual(1, new[] { first.Result, second.Result }.Count(x => x));
		Assert.AreEqual(1, _db.Fetch<TranAutoReplenishBatch>().Count);
		Assert.AreEqual(1, _db.Fetch<TranHoju>().Count);
		Assert.AreEqual(0, _db.Fetch<Tran13Hachu>().Count);
	}

	[TestMethod]
	public void Commit_ConcurrentRetryLeavesOneOrderAndReturnsOriginalIds() {
		var saved = SaveDraft(out _);
		var param = BatchParam(saved, AutoReplenishOperation.Commit);
		using var gate = new System.Threading.Barrier(2);
		System.Threading.Tasks.Task<AutoReplenishResult?> Start() => System.Threading.Tasks.Task.Run(() => {
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();
			using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
			gate.SignalAndWait();
			try { return new AutoReplenishDb(db).Commit(param); }
			catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { return null; }
		});
		var first = Start();
		var second = Start();
		System.Threading.Tasks.Task.WaitAll(first, second);
		Assert.IsTrue(first.Result != null || second.Result != null, "少なくとも1要求は確定する");
		var retry = Replenish.Commit(param);
		var orderId = _db.Fetch<Tran13Hachu>().Single().Id;
		CollectionAssert.AreEqual(new[] { orderId }, retry.CreatedHachuIds);
		foreach (var result in new[] { first.Result, second.Result }.Where(x => x != null))
			CollectionAssert.AreEqual(new[] { orderId }, result!.CreatedHachuIds);
		Assert.AreEqual((int)AutoReplenishStatus.Confirmed, _db.Fetch<TranAutoReplenishBatch>().Single().Status);
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Migration_LegacyHojuGetsDefaultColumnsAndBatchIndexPreservingQuantity() {
		Insert(new TranHoju { DenDay = Day, Id_Soko = _soko, Id_Shiire = _shiire, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = 7 });
		string[] addedColumns = ["Id_Batch", "Id_Tenpo", "DemandSu", "TransferSu", "CoveredSu", "GeneratedHachuId", "GeneratedHaibunId"];
		foreach (var column in addedColumns) _db.Execute("ALTER TABLE TranHoju DROP COLUMN " + column);
		Assert.IsTrue(_db.CreateTable(typeof(SysUpdateDb), true, false));
		Insert(new SysUpdateDb { DbVersion = 26_10_05_01 });
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(_db, false), "通常起動で旧補充表へ移行できる");
		var row = _db.Fetch<TranHoju>().Single();
		Assert.AreEqual((7, _soko, _shiire), (row.Su, row.Id_Soko, row.Id_Shiire));
		Assert.AreEqual((0L, 0L, 0, 0, 0, 0L, 0L), (row.Id_Batch, row.Id_Tenpo, row.DemandSu, row.TransferSu, row.CoveredSu, row.GeneratedHachuId, row.GeneratedHaibunId));
		Assert.AreEqual(1, _db.Fetch<string>("SELECT name FROM sqlite_master WHERE type='index' AND name='TranHoju_nk2'").Count);
		Assert.AreEqual(26_10_05_02, _db.Fetch<SysUpdateDb>().Max(x => x.DbVersion));
		await UpdateDb.WriteVersionInfoAsync(_db);
		Assert.AreEqual(2, _db.Fetch<SysUpdateDb>().Count, "再起動でmigrationを重複しない");
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Initialize_NewDatabaseCreatesReplenishmentTablesAndBatchIndex() {
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(db, false));
		foreach (var table in new[] { "MasterAutoReplenishStock", "MasterAutoReplenishExclude", "TranAutoReplenishBatch", "TranHoju" })
			Assert.AreEqual(1, db.Fetch<string>("SELECT name FROM sqlite_master WHERE type='table' AND name=@0", table).Count, table);
		Assert.AreEqual(1, db.Fetch<string>("SELECT name FROM sqlite_master WHERE type='index' AND name='TranHoju_nk2'").Count);
		Assert.AreEqual(26_10_05_02, db.Fetch<SysUpdateDb>().Max(x => x.DbVersion));
	}

	[TestMethod]
	public void SaveStockSetting_InvalidSkuStoreAndStaleVersionAreRejected() {
		var setting = _db.Fetch<MasterAutoReplenishStock>().Single();
		var param = Param(AutoReplenishOperation.SaveStockSetting) with { StockSetting = setting, ExpectedVdu = setting.Vdu };
		setting.Id_Siz = 99999;
		Assert.ThrowsExactly<ArgumentException>(() => Replenish.SaveStockSetting(param));
		setting.Id_Siz = 2;
		setting.Id_Tenpo = _soko;
		Assert.ThrowsExactly<ArgumentException>(() => Replenish.SaveStockSetting(param));
		setting.Id_Tenpo = _store;
		setting.TargetSu = 12;
		var updated = Replenish.SaveStockSetting(param).StockSettings.Single();
		Assert.AreEqual(12, _db.Fetch<MasterAutoReplenishStock>().Single().TargetSu);
		Assert.ThrowsExactly<AutoReplenishConflictException>(() => Replenish.SaveStockSetting(param with { StockSetting = updated }));
		Assert.AreEqual(12, _db.Fetch<MasterAutoReplenishStock>().Single().TargetSu);
	}

	private AutoReplenishResult SaveDraft(out AutoReplenishParam param) {
		var preview = Replenish.Preview(Param(AutoReplenishOperation.Preview));
		param = Param(AutoReplenishOperation.Save) with { ExecutionKey = Guid.NewGuid().ToString(), Fingerprint = preview.Fingerprint };
		return Replenish.Save(param);
	}
	private AutoReplenishParam BatchParam(AutoReplenishResult saved, AutoReplenishOperation operation) =>
		Param(operation) with { Id_Batch = saved.Batch!.Id, ExpectedVdu = saved.Batch.Vdu, ExecutionKey = saved.Batch.ExecutionKey };
	private AutoReplenishParam Param(AutoReplenishOperation operation) => new(_soko, Day, operation);
	private long AddStore(string code) {
		var id = Insert(new MasterTokui { Code = code, Name = code, TenType = 6 });
		Insert(new Tran60TanaDate { Id_Shop = id, AutoHoju = 127 });
		return id;
	}
	private long AddSetting(long store, int target) => Insert(new MasterAutoReplenishStock {
		Id_Soko = _soko, Id_Tenpo = store, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2,
		TargetSu = target, Enabled = 1,
	});
	private long Insert(BaseDbClass row) { _db.Insert(row); return row.Id; }
	private Tran99Meisai Meisai(int no, int su) => new() {
		No = no, Id_Shohin = _product, Id_Col = 1, Id_Siz = 2, Su = su,
		Tanka = 100, Kingaku = su * 100L, Jodai = 200, Gedai = 100,
	};
}
