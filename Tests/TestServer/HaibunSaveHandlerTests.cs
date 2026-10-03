using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 配分の洗い替え保存（<see cref="HaibunSaveParam"/>）を <see cref="CoreService"/> のハンドラ層越しに叩く単体テスト。
/// <para>
/// 削除・登録・引当の引き直しが1トランザクションで行われ、競合・修正不可・入力違反が1件でもあれば
/// 何も書かれないことを固定する。仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 4.2。
/// <see cref="ManualLockHandlerTests"/> と同じ作法でフェイク依存の <see cref="CoreService"/> を直接作る。
/// </para>
/// </summary>
[TestClass]
public class HaibunSaveHandlerTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private CoreService? _service;
	private ServiceProvider? _scopeFactoryProvider;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"HaibunSaveHandlerTests-{Guid.NewGuid():N}";
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
		Db.CreateTable(typeof(SysSequence), true, false);
		Db.CreateTable(typeof(SysHistAutoexec), true, false);
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.Insert(new MasterSysman { ShimeBi = 99 });
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryRealStock), true, false);
		Db.CreateTable(typeof(TranHaibun), true, false);
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");

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

	private async Task<CvMsg> SaveAsync(TranHaibun[] replace, TranHaibun[] insert) {
		var param = new HaibunSaveParam([.. replace.Select(x => new DeleteBulkRow(x.Id, x.Vdu))], insert);
		return await Service.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(HaibunSaveParam),
			DataMsg = Common.SerializeObject(param),
		});
	}

	private static TranHaibun NewRow(int su, long idSiz = 1000, EnumHaibun kubun = EnumHaibun.Zaiko) => new() {
		DenDay = "20260815",
		Id_Soko = 1,
		Id_Tenpo = 2,
		Id_Shohin = 10,
		Id_Col = 100,
		Id_Siz = idSiz,
		Su = su,
		Kubun = (int)kubun,
	};

	private int RealReserve(long idSiz = 1000) =>
		Db.Fetch<SummaryRealStock>("where Id_Soko=1 and Id_Shohin=10 and Id_Col=100 and Id_Siz=@0", idSiz)
			.Sum(x => x.ReserveQty);

	private TranHaibun[] AllRows() => [.. Db.Fetch<TranHaibun>("order by Id")];

	[TestMethod]
	public async Task Save_ReplacesRowsAndRecalculatesReserveInOneCall() {
		var first = await SaveAsync([], [NewRow(5)]);
		Assert.AreEqual(0, first.Code, first.Option);
		Assert.AreEqual(5, RealReserve());

		var loaded = AllRows();
		var reply = await SaveAsync(loaded, [NewRow(3), NewRow(4, idSiz: 1001)]);

		Assert.AreEqual(0, reply.Code, reply.Option);
		var result = (HaibunSaveResult)Common.DeserializeObject(reply.DataMsg, typeof(HaibunSaveResult))!;
		Assert.AreEqual(1, result.DeletedCount);
		Assert.AreEqual(2, result.InsertedCount);
		Assert.AreEqual(2, AllRows().Length, "旧行は消え、新しい2行だけが残る");
		Assert.AreEqual(3, RealReserve(), "削除と登録の両方の引当キーが引き直される");
		Assert.AreEqual(4, RealReserve(1001));
	}

	[TestMethod]
	public async Task Save_NormalizesStateColumnsOfNewRows() {
		var row = NewRow(5);
		row.KakuteiDay = "20260801";
		row.EndFlag = 1;
		row.JitsuSu = 5;
		row.RelateNo2 = 99;
		row.SendFlg = 2;

		var reply = await SaveAsync([], [row]);

		Assert.AreEqual(0, reply.Code, reply.Option);
		var saved = AllRows().Single();
		Assert.AreEqual("", saved.KakuteiDay);
		Assert.AreEqual(0, saved.EndFlag);
		Assert.AreEqual(0, saved.JitsuSu);
		Assert.AreEqual(0, saved.RelateNo2);
		Assert.AreEqual(0, saved.SendFlg);
		Assert.IsTrue(saved.Vdc > 0 && saved.Vdu > 0, "監査値はサーバが採番する");
		Assert.AreEqual(5, RealReserve(), "未確定として引当に入る");
	}

	[TestMethod]
	public async Task Save_NotEditableRow_WritesNothing() {
		await SaveAsync([], [NewRow(5)]);
		// 確定済みの行は洗い替えで消せない（P4: 店舗配分入力で確定済み・未送信の指示を消していた）
		Db.Execute("update TranHaibun set KakuteiDay='20260816'");
		var loaded = AllRows();

		var reply = await SaveAsync(loaded, [NewRow(7)]);

		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, reply.Code);
		var rows = AllRows();
		Assert.AreEqual(1, rows.Length, "削除も登録もされない");
		Assert.AreEqual(5, rows[0].Su);
	}

	[TestMethod]
	public async Task Save_ConcurrencyConflict_WritesNothing() {
		await SaveAsync([], [NewRow(5)]);
		var stale = AllRows();
		stale[0].Vdu -= 1;

		var reply = await SaveAsync(stale, [NewRow(7)]);

		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, reply.Code);
		Assert.AreEqual(5, AllRows().Single().Su, "何も書かれない");
		Assert.AreEqual(5, RealReserve());
	}

	[TestMethod]
	public async Task Save_InvalidNewRow_WritesNothing() {
		await SaveAsync([], [NewRow(5)]);
		var loaded = AllRows();

		var reply = await SaveAsync(loaded, [NewRow(3), NewRow(2, kubun: EnumHaibun.IdoShiji)]);

		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, reply.Code);
		Assert.AreEqual(5, AllRows().Single().Su, "廃止区分を含むと何も書かれない");
	}

	[TestMethod]
	public async Task Save_ReceiptAllocationWithoutOrder_IsRejected() {
		var row = NewRow(3, kubun: EnumHaibun.Hatsukai);
		row.RelateNo1 = 0;

		var reply = await SaveAsync([], [row]);

		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, reply.Code, "仕入配分は発注Id必須（Step 4）");
		Assert.AreEqual(0, AllRows().Length);
	}

	[TestMethod]
	public async Task Save_EmptyNewRows_DeletesOnly() {
		await SaveAsync([], [NewRow(5)]);

		var reply = await SaveAsync(AllRows(), []);

		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual(0, AllRows().Length);
		Assert.AreEqual(0, RealReserve());
	}
}
