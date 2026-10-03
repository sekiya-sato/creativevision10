using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 取置配分（区分6）の登録・売上変換・取消・期限切れを <see cref="CoreService"/> のハンドラ層と
/// <see cref="ReservationDb"/> 越しに確かめる。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step5_取置配分入力_詳細設計.md`。
/// </summary>
[TestClass]
public class ReservationTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private CoreService? _service;
	private ServiceProvider? _scopeFactoryProvider;
	private long _storeId;
	private long _otherStoreId;
	private long _customerId;
	private long _otherCustomerId;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ReservationTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		foreach (var t in new[] {
			typeof(SysSequence), typeof(SysHistAutoexec), typeof(MasterSysman), typeof(MasterTokui), typeof(MasterShohin),
			typeof(MasterEndCustomer), typeof(MasterShain), typeof(DerivedShohinColSiz), typeof(SummaryStock), typeof(SummaryRealStock),
			typeof(TranHaibun), typeof(Tran01Tenuri), typeof(Tran03Shiire),
			// 在庫の全件再集計（SummaryAllAsyncStream）が読む伝票テーブル
			typeof(Tran00Uriage), typeof(Tran05Ido), typeof(Tran10IdoOut), typeof(Tran11IdoIn), typeof(Tran60Tana), typeof(Tran61Chosei),
		}) {
			Db.CreateTable(t, true, false);
		}
		Db.Insert(new MasterSysman { ShimeBi = 99 });
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		// 店舗の消費税端数処理は切上（伝票単位で1回だけ丸めることを確かめるため）
		var store = new MasterTokui { Code = "S001", Name = "直営店A", TenType = 6, TaxRounding = (int)EnumRounding.Ceiling };
		Db.Insert(store);
		_storeId = store.Id;
		var other = new MasterTokui { Code = "S002", Name = "直営店B", TenType = 6 };
		Db.Insert(other);
		_otherStoreId = other.Id;
		var shohin = new MasterShohin { Code = "P001", Name = "テスト商品", Id_Tax = 1 };
		Db.Insert(shohin);
		Db.Execute("UPDATE MasterShohin SET Id = 10 WHERE Id = @0", shohin.Id);
		Db.Insert(new DerivedShohinColSiz { Id_Shohin = 10, Id_Col = 100, Code_Col = "01", Mei_Col = "白", Id_Siz = 1000, Code_Siz = "M", Mei_Siz = "Mサイズ" });
		var customer = new MasterEndCustomer { Code = "C001", Name = "顧客一郎" };
		Db.Insert(customer);
		_customerId = customer.Id;
		var otherCustomer = new MasterEndCustomer { Code = "C002", Name = "顧客二郎" };
		Db.Insert(otherCustomer);
		_otherCustomerId = otherCustomer.Id;

		_scopeFactoryProvider = new ServiceCollection().BuildServiceProvider();
		_service = new CoreService(
			NullLogger<CoreService>.Instance,
			new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
			new FakeWebHostEnvironment(),
			new HttpContextAccessor(),
			_db,
			_scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(_db, NullLogger<PointOfSaleService>.Instance));
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
		_scopeFactoryProvider?.Dispose();
	}

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");
	private CoreService Service => _service ?? throw new AssertFailedException("Service not initialized");

	private async Task<CvMsg> ExecuteAsync<T>(T param) where T : notnull => await Service.QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg201_Op_Execute,
		DataType = typeof(T),
		DataMsg = Common.SerializeObject(param),
	});

	private Task<CvMsg> SaveAsync(TranHaibun[] replace, TranHaibun[] insert) =>
		ExecuteAsync(new HaibunSaveParam([.. replace.Select(x => new DeleteBulkRow(x.Id, x.Vdu))], insert));

	private TranHaibun NewReservation(int su, int tanka = 1001, long? customerId = null, long? storeId = null,
		string denDay = "20261001", string? limitDay = null) {
		var store = storeId ?? _storeId;
		return new() {
			Kubun = (int)EnumHaibun.Reservation,
			DenDay = denDay,
			LimitDay = limitDay ?? AllocationRules.DefaultLimitDay(denDay),
			Id_Tenpo = store,
			Id_Soko = store,
			Id_Customer = customerId ?? _customerId,
			Id_Shohin = 10,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = su,
			Tanka = tanka,
			Jodai = tanka,
		};
	}

	/// <summary>店舗へ仕入で在庫を入れる</summary>
	private void Stock(long idSoko, int su) {
		var tran = new Tran03Shiire {
			DenDay = "20260901",
			KakeDay = "20260901",
			Id_Soko = idSoko,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = su }],
		};
		tran.EnKubun = EnumShiire.Shiire;
		Db.Insert(tran);
		new SummaryDb(Db).CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranDetail.Id_Soko), tran.Id, false);
	}

	private SummaryRealStock? Real(long idSoko) =>
		Db.Fetch<SummaryRealStock>("where Id_Soko=@0 and Id_Shohin=10 and Id_Col=100 and Id_Siz=1000", idSoko).SingleOrDefault();

	private TranHaibun Row(long id) => Db.Single<TranHaibun>("where Id=@0", id);

	private TranHaibun[] AllRows() => [.. Db.Fetch<TranHaibun>("order by Id")];

	private static ReservationRowRef Ref(TranHaibun h) => new(h.Id, h.Vdu);

	// ---------------- 登録（HaibunSaveParam） ----------------

	[TestMethod]
	public async Task Save_Reservation_KeepsCustomerAndLimitAndReservesStoreStock() {
		Stock(_storeId, 5);

		var reply = await SaveAsync([], [NewReservation(2)]);

		Assert.AreEqual(0, reply.Code, reply.Option);
		var saved = AllRows().Single();
		Assert.AreEqual(_customerId, saved.Id_Customer);
		Assert.AreEqual("20261008", saved.LimitDay, "期限日の初期値は取置日の1週間後(D11)");
		Assert.AreEqual(0, saved.EndReason);
		Assert.AreEqual(2, Real(_storeId)!.ReserveQty, "取置は店舗自身の在庫を引き当てる");
	}

	[TestMethod]
	public async Task Save_Reservation_StockShortageIsNotRejected() {
		// 判断2: 有効在庫が足りなくても保存は止めない（画面で警告する）
		var reply = await SaveAsync([], [NewReservation(3)]);

		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual(3, Real(_storeId)!.ReserveQty);
	}

	[TestMethod]
	public async Task Save_Reservation_InvalidInputWritesNothing() {
		var noCustomer = NewReservation(1);
		noCustomer.Id_Customer = 0;
		var badLimit = NewReservation(1, limitDay: "20260930");
		var otherSoko = NewReservation(1);
		otherSoko.Id_Soko = _otherStoreId;
		var noLimit = NewReservation(1, limitDay: "");

		foreach (var row in new[] { noCustomer, badLimit, otherSoko, noLimit }) {
			var reply = await SaveAsync([], [NewReservation(1), row]);
			Assert.AreEqual((int)CvMsgErrorCode.InvalidParameter, reply.Code, reply.Option);
		}
		Assert.AreEqual(0, AllRows().Length, "1件でも違反があれば何も書かない");
	}

	[TestMethod]
	public async Task Save_Reservation_ChangeQtyAndLimitByReplace() {
		await SaveAsync([], [NewReservation(2)]);
		var loaded = AllRows();
		var changed = NewReservation(4, limitDay: "20261020");

		var reply = await SaveAsync(loaded, [changed]);

		Assert.AreEqual(0, reply.Code, reply.Option);
		var saved = AllRows().Single();
		Assert.AreEqual(4, saved.Su);
		Assert.AreEqual("20261020", saved.LimitDay);
		Assert.AreEqual(4, Real(_storeId)!.ReserveQty);
	}

	// ---------------- 売上変換 ----------------

	[TestMethod]
	public async Task Convert_CreatesTenuriPerStoreAndCustomer() {
		Stock(_storeId, 10);
		Stock(_otherStoreId, 10);
		await SaveAsync([], [
			NewReservation(1, tanka: 1001),
			NewReservation(1, tanka: 1002),
			NewReservation(2, tanka: 500, customerId: _otherCustomerId),
			NewReservation(1, tanka: 300, storeId: _otherStoreId),
		]);
		var rows = AllRows();

		var reply = await ExecuteAsync(new ReservationConvertParam([.. rows.Select(Ref)], "20261005", 7));

		Assert.AreEqual(0, reply.Code, reply.Option);
		var result = (ReservationConvertResult)Common.DeserializeObject(reply.DataMsg, typeof(ReservationConvertResult))!;
		Assert.AreEqual(3, result.CreatedSlipIds.Length, "店舗×顧客ごとに1伝票");
		Assert.AreEqual(4, result.ConvertedCount);

		var slip = Db.Single<Tran01Tenuri>("where Id_Tenpo=@0 and Id_Customer=@1", _storeId, _customerId);
		Assert.AreEqual((int)EnumUri01.Uriage, slip.Kubun);
		Assert.AreEqual("20261005", slip.DenDay);
		Assert.AreEqual(_storeId, slip.Id_Soko, "店舗自身の在庫から売る");
		Assert.AreEqual(ReservationDb.ConvertMemo, slip.Memo);
		Assert.AreEqual(7, slip.Id_Shain);
		Assert.AreEqual("C001", slip.VCustomer.Cd);
		Assert.AreEqual("S001", slip.VTenpo.Cd);
		Assert.AreEqual(2, slip.SuTotal);
		Assert.AreEqual(2003, slip.KingakuTotal, "単価は取置した日の上代(判断3)");
		// 伝票単位・切上: 2003×10% = 200.3 → 201（明細単位なら 101+101=202 になる）
		Assert.AreEqual(201, slip.Tax1);
		Assert.AreEqual(2003 + 201, slip.Total);
		Assert.AreEqual("白", slip.Jmeisai![0].Mei_Col);
		Assert.AreEqual("P001", slip.Jmeisai![0].Code_Shohin);

		foreach (var h in AllRows()) {
			Assert.AreEqual(1, h.EndFlag);
			Assert.AreEqual(h.Su, h.JitsuSu);
			Assert.AreEqual(0, h.ShortSu);
			Assert.AreEqual("20261005", h.KakuteiDay);
			Assert.AreEqual((int)EnumHaibunEndReason.Converted, h.EndReason);
			Assert.IsTrue(h.RelateNo2 > 0);
		}
		Assert.AreEqual(0, Real(_storeId)!.ReserveQty, "引当を解除する");
		Assert.AreEqual(10 - 4, Real(_storeId)!.Su, "店舗売上で在庫が減る");
		Assert.AreEqual(9, Real(_otherStoreId)!.Su);

		// 全件再集計と一致する（在庫・引当とも）
		var before = Db.Fetch<SummaryRealStock>("order by Id_Soko").Select(x => $"{x.Id_Soko}:{x.Su}:{x.ReserveQty}").ToArray();
		new SummaryDb(Db).CalcReserveQtyAll();
		CollectionAssert.AreEqual(before, Db.Fetch<SummaryRealStock>("order by Id_Soko").Select(x => $"{x.Id_Soko}:{x.Su}:{x.ReserveQty}").ToArray());
	}

	[TestMethod]
	public async Task Convert_StockMatchesFullRebuild() {
		Stock(_storeId, 10);
		await SaveAsync([], [NewReservation(3), NewReservation(2, customerId: _otherCustomerId), NewReservation(1)]);
		var rows = AllRows();
		await ExecuteAsync(new ReservationConvertParam([Ref(rows[0]), Ref(rows[1])], "20261005", 1));
		await ExecuteAsync(new ReservationCancelParam([Ref(rows[2])], "20261005"));
		string[] Snapshot() => [
			.. Db.Fetch<SummaryStock>("order by SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz")
				.Select(x => $"M:{x.SumMonth}:{x.Id_Soko}:{x.Su}:{x.InQty}:{x.OutQty}:{x.ReserveQty}"),
			.. Db.Fetch<SummaryRealStock>("order by Id_Soko, Id_Shohin, Id_Col, Id_Siz")
				.Select(x => $"R:{x.Id_Soko}:{x.Su}:{x.ReserveQty}"),
		];
		var incremental = Snapshot();
		Assert.AreEqual(5, Real(_storeId)!.Su);

		await foreach (var progress in new SummaryDb(Db).SummaryAllAsyncStream(new CalcDateTermParameter("202609", "202610"))) {
			Assert.IsFalse(progress.IsError, $"{progress.StepName}: {progress.ErrorMessage}");
		}

		CollectionAssert.AreEqual(incremental, Snapshot(), "売上変換の在庫・引当は全件再集計と一致する");
	}

	[TestMethod]
	public async Task Convert_SokoDiffersFromTenpo_IsRejected() {
		// 汎用の書き込み経路から入った出庫元≠店舗の取置は変換しない（伝票の在庫拠点と引当の拠点がずれるため）
		var odd = NewReservation(1);
		odd.Id_Soko = _otherStoreId;
		Db.Insert(odd);

		var reply = await ExecuteAsync(new ReservationConvertParam([Ref(Row(odd.Id))], "20261005", 1));

		Assert.AreEqual((int)CvMsgErrorCode.InvalidParameter, reply.Code);
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>("").Count);
	}

	[TestMethod]
	public async Task Convert_InvalidDenDay_IsRejected() {
		await SaveAsync([], [NewReservation(1)]);

		var reply = await ExecuteAsync(new ReservationConvertParam([Ref(AllRows().Single())], "20261332", 1));

		Assert.AreEqual((int)CvMsgErrorCode.InvalidParameter, reply.Code);
		Assert.AreEqual(0, AllRows().Single().EndFlag);
	}

	[TestMethod]
	public async Task Convert_StaleVduOrCompletedRow_WritesNothing() {
		await SaveAsync([], [NewReservation(1), NewReservation(2)]);
		var rows = AllRows();

		var stale = await ExecuteAsync(new ReservationConvertParam([Ref(rows[0]), new ReservationRowRef(rows[1].Id, rows[1].Vdu - 1)], "20261005", 1));

		Assert.AreEqual((int)CvMsgErrorCode.ConcurrentUpdate, stale.Code);
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>("").Count);
		Assert.IsTrue(AllRows().All(x => x.EndFlag == 0));

		// 取消済みの行は変換できない
		await ExecuteAsync(new ReservationCancelParam([Ref(rows[1])], "20261003"));
		var again = await ExecuteAsync(new ReservationConvertParam([Ref(Row(rows[0].Id)), Ref(Row(rows[1].Id))], "20261005", 1));
		Assert.AreEqual((int)CvMsgErrorCode.ConcurrentUpdate, again.Code);
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>("").Count);
	}

	[TestMethod]
	public async Task Convert_NonReservationRow_IsRejected() {
		var zaiko = NewReservation(1);
		zaiko.Kubun = (int)EnumHaibun.Zaiko;
		zaiko.Id_Customer = 0;
		await SaveAsync([], [zaiko]);

		var reply = await ExecuteAsync(new ReservationConvertParam([Ref(AllRows().Single())], "20261005", 1));

		Assert.AreEqual((int)CvMsgErrorCode.InvalidParameter, reply.Code);
		Assert.AreEqual(0, AllRows().Single().EndFlag);
	}

	[TestMethod]
	public async Task Commit_ReservationRow_IsRejected() {
		await SaveAsync([], [NewReservation(1)]);
		var row = AllRows().Single();

		var reply = await ExecuteAsync(new HaibunCommitParam([new HaibunCommitRow(row.Id, row.Vdu, 1)], "20261005", 1));

		Assert.AreEqual((int)CvMsgErrorCode.InvalidParameter, reply.Code, "取置は配分確定では処理しない");
	}

	// ---------------- 取消・期限切れ ----------------

	[TestMethod]
	public async Task Cancel_CompletesAsShortageWithoutSlip() {
		Stock(_storeId, 5);
		await SaveAsync([], [NewReservation(3)]);
		var row = AllRows().Single();

		var reply = await ExecuteAsync(new ReservationCancelParam([Ref(row)], "20261003"));

		Assert.AreEqual(0, reply.Code, reply.Option);
		var after = Row(row.Id);
		Assert.AreEqual(1, after.EndFlag);
		Assert.AreEqual(0, after.JitsuSu);
		Assert.AreEqual(3, after.ShortSu);
		Assert.AreEqual("20261003", after.KakuteiDay);
		Assert.AreEqual((int)EnumHaibunEndReason.Cancelled, after.EndReason);
		Assert.AreEqual(0, after.RelateNo2);
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>("").Count);
		Assert.AreEqual(0, Real(_storeId)!.ReserveQty);
		Assert.AreEqual(5, Real(_storeId)!.Su, "在庫は動かない");
	}

	[TestMethod]
	public async Task ExpireOverdue_CancelsFromTheDayAfterLimit() {
		await SaveAsync([], [
			NewReservation(1, limitDay: "20261005"),
			NewReservation(2, limitDay: "20261006"),
		]);
		var rows = AllRows();
		var reservationDb = new ReservationDb(Db);

		Assert.AreEqual(0, reservationDb.ExpireOverdue(new DateTime(2026, 10, 5)), "期限日当日は有効");
		Assert.AreEqual(3, Real(_storeId)!.ReserveQty);

		Assert.AreEqual(1, reservationDb.ExpireOverdue(new DateTime(2026, 10, 6)));
		var expired = Row(rows[0].Id);
		Assert.AreEqual(1, expired.EndFlag);
		Assert.AreEqual((int)EnumHaibunEndReason.Expired, expired.EndReason);
		Assert.AreEqual("20261006", expired.KakuteiDay, "完了日は期限日の翌日");
		Assert.AreEqual(1, expired.ShortSu);
		Assert.AreEqual(0, Row(rows[1].Id).EndFlag);
		Assert.AreEqual(2, Real(_storeId)!.ReserveQty);

		// 実行が遅れても完了日は期限日の翌日のまま
		Assert.AreEqual(1, reservationDb.ExpireOverdue(new DateTime(2026, 10, 9)));
		Assert.AreEqual("20261007", Row(rows[1].Id).KakuteiDay);
		Assert.AreEqual(0, Real(_storeId)!.ReserveQty);
		Assert.AreEqual(0, reservationDb.ExpireOverdue(new DateTime(2026, 10, 9)), "完了済みは対象外");
	}

	// ---------------- 入力規則 ----------------

	[TestMethod]
	public void DefaultLimitDay_IsOneWeekLater() {
		Assert.AreEqual("20261008", AllocationRules.DefaultLimitDay("20261001"));
		Assert.AreEqual("20270104", AllocationRules.DefaultLimitDay("20261228"));
		Assert.AreEqual("", AllocationRules.DefaultLimitDay(""));
	}

	[TestMethod]
	public void ValidateNewRow_Reservation() {
		Assert.IsNull(AllocationRules.ValidateNewRow(NewReservation(1)));
		var sameDay = NewReservation(1, limitDay: "20261001");
		Assert.IsNull(AllocationRules.ValidateNewRow(sameDay), "期限日 = 取置日は可");
		var invalidDate = NewReservation(1, limitDay: "20261332");
		Assert.IsNotNull(AllocationRules.ValidateNewRow(invalidDate));
		var related = NewReservation(1);
		related.RelateNo1 = 5;
		Assert.IsNotNull(AllocationRules.ValidateNewRow(related));
	}

	[TestMethod]
	public void SchedulerDefinition_ReservationExpireIsEnabledDaily() {
		var def = SchedulerService.SystemJobDefinitions.Single(d => d.JobKey == SchedulerService.JobKeyReservationExpire);
		Assert.IsTrue(def.DefaultEnabled);
		Assert.AreEqual("50 0 * * *", def.DefaultCronExpression);
		Assert.IsFalse(def.CheckMinInterval);
	}
}
