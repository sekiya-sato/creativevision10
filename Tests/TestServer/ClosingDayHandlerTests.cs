using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
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

/// <summary>締日(取引先の締日1/2/3・自社締日)の検査が汎用保存の各経路で迂回されないことを実SQLite上で検証する(AGENTS 7.4)。</summary>
[TestClass]
public sealed class ClosingDayHandlerTests {
	private ExDatabaseSqlite _db = null!;
	private SqliteConnection _connection = null!;
	private SqliteConnection _anchor = null!;
	private ServiceProvider _provider = null!;
	private CoreService _service = null!;

	[TestInitialize]
	public void Initialize() {
		var connectionString = $"Data Source=Closing-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
		_anchor = new SqliteConnection(connectionString);
		_anchor.Open();
		_connection = new SqliteConnection(connectionString);
		_connection.Open();
		_db = new ExDatabaseSqlite(_connection) { KeepConnectionAlive = true };
		// 取引先マスタの更新はV*列の伝播(MasterCascadeDb)を伴うため、伝播先テーブルも作っておく
		foreach (var type in MasterCascadeDb.VRules.Select(r => r.Target).Concat(MasterCascadeDb.VRules.Select(r => r.Source)).Append(typeof(MasterSysman)).Distinct()) _db.CreateTable(type, true, false);
		_provider = new ServiceCollection().BuildServiceProvider();
		_service = new CoreService(NullLogger<CoreService>.Instance, new ConfigurationBuilder().Build(), new FakeWebHostEnvironment(), new HttpContextAccessor(), _db, _provider.GetRequiredService<IServiceScopeFactory>(), new PointOfSaleService(_db, NullLogger<PointOfSaleService>.Instance));
	}

	[TestCleanup]
	public void Cleanup() { _db.Close(); _connection.Dispose(); _anchor.Dispose(); _provider.Dispose(); }
	private Task<CvMsg> Send(object param) => _service.QueryMsgAsync(new CvMsg { Flag = CvFlag.Msg201_Op_Execute, DataType = param.GetType(), DataMsg = Common.SerializeObject(param) });
	private T Row<T>(long id) => _db.Fetch<T>("WHERE Id=@0", id).Single();

	[TestMethod]
	public async Task Insert_RejectsInvalidClosingDaysOnEveryInsertPath() {
		var invalid = new MasterTokui { Code = "T01", Name = "降順", Shime1 = 20, Shime2 = 10 };
		Assert.AreNotEqual(0, (await Send(new InsertParam(typeof(MasterTokui), Common.SerializeObject(invalid)))).Code);
		var collide = new MasterShiire { Code = "S01", Name = "28日と末日", Shime1 = 28, Shime2 = 99 };
		Assert.AreNotEqual(0, (await Send(new InsertBulkParam(typeof(MasterShiire), Common.SerializeObject(new[] { collide })))).Code);
		Assert.AreNotEqual(0, (await Send(new InsertParam(typeof(MasterSysman), Common.SerializeObject(new MasterSysman { ShimeBi = 0 })))).Code);

		Assert.AreEqual(0, _db.Fetch<MasterTokui>("").Count);
		Assert.AreEqual(0, _db.Fetch<MasterShiire>("").Count);
		Assert.AreEqual(0, _db.Fetch<MasterSysman>("").Count);

		var valid = new MasterTokui { Code = "T02", Name = "複数締日", Shime1 = 10, Shime2 = 20, Shime3 = 99 };
		var reply = await Send(new InsertParam(typeof(MasterTokui), Common.SerializeObject(valid)));
		Assert.AreEqual(0, reply.Code, reply.Option);
	}

	[TestMethod]
	public async Task Update_RejectsInvalidClosingDayChangeButKeepsUnrelatedUpdates() {
		var tokui = new MasterTokui { Code = "T01", Name = "得意先", Shime1 = 99, Vdu = 1 };
		_db.Insert(tokui);
		var sysman = new MasterSysman { ShimeBi = 99, Vdu = 1 };
		_db.Insert(sysman);

		var changed = Row<MasterTokui>(tokui.Id);
		changed.Shime1 = 0;
		changed.Shime2 = 20;
		Assert.AreNotEqual(0, (await Send(new UpdateParam(typeof(MasterTokui), Common.SerializeObject(changed)))).Code);
		Assert.AreEqual((99, 0), (Row<MasterTokui>(tokui.Id).Shime1, Row<MasterTokui>(tokui.Id).Shime2), "不正な締日は保存されない");

		var own = Row<MasterSysman>(sysman.Id);
		own.ShimeBi = 0;
		Assert.AreNotEqual(0, (await Send(new UpdateParam(typeof(MasterSysman), Common.SerializeObject(own)))).Code);
		Assert.AreEqual(99, Row<MasterSysman>(sysman.Id).ShimeBi, "自社締日を未使用(0)へは変更できない");

		var renamed = Row<MasterTokui>(tokui.Id);
		renamed.Name = "名称変更";
		var reply = await Send(new UpdateParam(typeof(MasterTokui), Common.SerializeObject(renamed)));
		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual("名称変更", Row<MasterTokui>(tokui.Id).Name);
	}

	[TestMethod]
	public async Task PartialUpdate_RejectsClosingDayColumns() {
		var tokui = new MasterTokui { Code = "T01", Name = "得意先", Shime1 = 99, Vdu = 1 };
		_db.Insert(tokui);
		var sysman = new MasterSysman { ShimeBi = 99, Vdu = 1 };
		_db.Insert(sysman);

		Assert.AreNotEqual(0, (await Send(new PartialUpdateParam(typeof(MasterTokui), ["Shime2"], [new(tokui.Id, tokui.Vdu, ["10"])]))).Code);
		Assert.AreNotEqual(0, (await Send(new PartialUpdateParam(typeof(MasterSysman), ["ShimeBi"], [new(sysman.Id, sysman.Vdu, ["0"])]))).Code);
		Assert.AreEqual(0, Row<MasterTokui>(tokui.Id).Shime2);
		Assert.AreEqual(99, Row<MasterSysman>(sysman.Id).ShimeBi);
	}
}
