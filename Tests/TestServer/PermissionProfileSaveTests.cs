using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
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
/// 権限プロファイルの保存（<see cref="PermissionProfileSaveParam"/>）を <see cref="CoreService"/> のハンドラ層越しに叩く単体テスト。
/// <para>
/// View明細の洗い替えが1トランザクションで行われ、競合・入力違反では何も書かれないこと、
/// Edit明細とFullAccessの明細に触れないことを固定する（<see cref="HaibunSaveHandlerTests"/> と同じ作法）。
/// </para>
/// </summary>
[TestClass]
public class PermissionProfileSaveTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private CoreService? _service;
	private ServiceProvider? _scopeFactoryProvider;

	[TestInitialize]
	public void Initialize() {
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = $"PermissionProfileSaveTests-{Guid.NewGuid():N}",
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		Db.CreateTable(typeof(SysPermissionProfile), true, false);
		Db.CreateTable(typeof(SysPermissionProfileDetail), true, false);
		SysPermissionProfile.CreateDefaultData(Db);

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

	private async Task<CvMsg> SaveAsync(SysPermissionProfile profile, params (string FunctionId, bool IsAllowed)[] rows) {
		var param = new PermissionProfileSaveParam(profile,
			[.. rows.Select(r => new SysPermissionProfileDetail { FunctionId = r.FunctionId, IsAllowed = r.IsAllowed })]);
		return await Service.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(PermissionProfileSaveParam),
			DataMsg = Common.SerializeObject(param),
		});
	}

	private SysPermissionProfile Profile(long id) => Db.Fetch<SysPermissionProfile>("where Id=@0", id).Single();

	private SysPermissionProfileDetail[] Details(long id, EnumPermissionType type) =>
		[.. Db.Fetch<SysPermissionProfileDetail>("where Id_PermissionProfile=@0 and PermissionType=@1 order by FunctionId", id, (int)type)];

	[TestMethod]
	public async Task Save_ReplacesViewRowsOnly_AndKeepsEditRows() {
		var profile = Profile(2);
		var editBefore = Details(2, EnumPermissionType.Edit).Length;
		Assert.IsTrue(editBefore > 0, "初期データにEdit明細がある前提");

		profile.Name = "店舗スタッフ";
		var reply = await SaveAsync(profile, ("08Zaiko.ZaikoQuery", true), ("01Master.MasterShainMente", false));

		Assert.AreEqual(0, reply.Code, reply.Option);
		var saved = (SysPermissionProfile)Common.DeserializeObject(reply.DataMsg, typeof(SysPermissionProfile))!;
		Assert.AreNotEqual(profile.Vdu, saved.Vdu, "Vduが進む");
		Assert.AreEqual("店舗スタッフ", Profile(2).Name);
		var view = Details(2, EnumPermissionType.View);
		CollectionAssert.AreEqual(new[] { "01Master.MasterShainMente", "08Zaiko.ZaikoQuery" }, view.Select(x => x.FunctionId).ToArray());
		Assert.IsFalse(view[0].IsAllowed);
		Assert.IsTrue(view[1].IsAllowed);
		Assert.AreEqual(editBefore, Details(2, EnumPermissionType.Edit).Length, "Edit明細は変えない");
	}

	[TestMethod]
	public async Task Save_NewProfile_InsertsWithRows() {
		var reply = await SaveAsync(new SysPermissionProfile { Code = "Keiri", Name = "経理", IsActive = true }, ("31Monthly.Dummy", true));

		Assert.AreEqual(0, reply.Code, reply.Option);
		var saved = (SysPermissionProfile)Common.DeserializeObject(reply.DataMsg, typeof(SysPermissionProfile))!;
		Assert.IsTrue(saved.Id > 4);
		Assert.HasCount(1, Details(saved.Id, EnumPermissionType.View));
	}

	[TestMethod]
	public async Task Save_StaleVdu_ReturnsConflictAndWritesNothing() {
		var stale = Profile(2);
		var first = await SaveAsync(Profile(2), ("08Zaiko.ZaikoQuery", true));
		Assert.AreEqual(0, first.Code, first.Option);
		var afterFirst = Details(2, EnumPermissionType.View);

		stale.Name = "古い読込";
		var reply = await SaveAsync(stale, ("06Uriage.ShopUriageInput", false));

		Assert.AreEqual(CvMsgErrorCode.ConcurrentUpdate, reply.Code);
		Assert.AreNotEqual("古い読込", Profile(2).Name);
		CollectionAssert.AreEqual(afterFirst.Select(x => x.Id).ToArray(), Details(2, EnumPermissionType.View).Select(x => x.Id).ToArray());
	}

	[TestMethod]
	public async Task Save_FullAccess_RejectsRowsAndStaysActive() {
		var full = Profile(SysPermissionProfile.FullAccessId);
		var rows = Details(SysPermissionProfile.FullAccessId, EnumPermissionType.View).Length;

		var rejected = await SaveAsync(full, ("08Zaiko.ZaikoQuery", false));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, rejected.Code);

		full.IsActive = false;
		full.Name = "管理者";
		var reply = await SaveAsync(full);
		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.IsTrue(Profile(SysPermissionProfile.FullAccessId).IsActive, "FullAccessは無効にしない");
		Assert.AreEqual("管理者", Profile(SysPermissionProfile.FullAccessId).Name);
		Assert.AreEqual(rows, Details(SysPermissionProfile.FullAccessId, EnumPermissionType.View).Length, "FullAccessの明細は変えない");
	}

	[TestMethod]
	public async Task Save_InvalidInput_WritesNothing() {
		var before = Details(2, EnumPermissionType.View).Select(x => x.Id).ToArray();

		var duplicated = await SaveAsync(Profile(2), ("08Zaiko.ZaikoQuery", true), ("08Zaiko.ZaikoQuery", false));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, duplicated.Code);

		var p = Profile(2);
		p.Code = "FullAccess";
		var sameCode = await SaveAsync(p, ("08Zaiko.ZaikoQuery", true));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, sameCode.Code);

		var noName = Profile(2);
		noName.Name = " ";
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, (await SaveAsync(noName)).Code);

		// 正規の画面からは来ない null 明細も、例外ではなく入力違反で返す
		var nullRow = await Service.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(PermissionProfileSaveParam),
			DataMsg = Common.SerializeObject(new PermissionProfileSaveParam(Profile(2), [null!])),
		});
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, nullRow.Code);

		CollectionAssert.AreEqual(before, Details(2, EnumPermissionType.View).Select(x => x.Id).ToArray());
		Assert.AreEqual("StoreOperationStandard", Profile(2).Code);
	}
}
