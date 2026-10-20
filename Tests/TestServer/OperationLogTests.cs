using CodeShare;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.CvServer;

/// <summary>
/// 操作履歴（Doc/spec/2026-10-20_メニュー利用履歴_最小変更設計案.md）のサーバ側テスト。
/// </summary>
[TestClass]
public class OperationLogTests {
	private ExDatabaseSqlite? _db;
	private ServiceProvider? _serviceProvider;
	private readonly HttpContextAccessor _httpContextAccessor = new();
	private long LoginId;
	private const long ShainId = 77;

	[TestInitialize]
	public void Initialize() {
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
		new AppGlobal().InitAsync(_db).Wait();
		_serviceProvider = new ServiceCollection().AddSingleton<ExDatabase>(_db).BuildServiceProvider();
		var login = new SysLogin { Id_Shain = ShainId, LoginId = "oplog" };
		_db.Insert(login);
		LoginId = login.Id;
		var identity = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.SerialNumber, LoginId.ToString())]);
		_httpContextAccessor.HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity) };
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_serviceProvider?.Dispose();
	}

	ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Db not initialized");

	CoreService CreateService(int level = 3, bool skipEmptyQuery = true) {
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
			["OperationLog:Level"] = level.ToString(),
			["OperationLog:SkipEmptyQuery"] = skipEmptyQuery ? "true" : "false",
		}).Build();
		return new CoreService(NullLogger<CoreService>.Instance, config, new FakeWebHostEnvironment(), _httpContextAccessor, Db,
			_serviceProvider!.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(Db, NullLogger<PointOfSaleService>.Instance));
	}

	List<SysHistOperation> Rows() => Db.Fetch<SysHistOperation>("order by Id");

	static CvMsg Msg(CvFlag flag, object param) => new() { Flag = flag, DataType = param.GetType(), DataMsg = Common.SerializeObject(param) };

	/// <summary>照会は条件・件数・利用者（JWTからサーバ解決）とともに記録される</summary>
	[TestMethod]
	public async Task Query_RecordsConditionCountAndServerResolvedUser() {
		var service = CreateService();
		var reply = await service.QueryMsgAsync(Msg(CvFlag.Msg101_Op_Query,
			new QueryListParam(typeof(SysLogin), "LoginId = @0", null, ["oplog"])));
		Assert.AreEqual(0, reply.Code, reply.Option);

		var row = Rows().Single();
		Assert.AreEqual(OperationKind.Query, row.Kind);
		Assert.AreEqual(LoginId, row.Id_Login);
		Assert.AreEqual(ShainId, row.Id_Shain);
		Assert.AreEqual(nameof(CvFlag.Msg101_Op_Query), row.Op);
		Assert.AreEqual(nameof(QueryListParam), row.DataType);
		Assert.AreEqual(1, row.ResultCount);
		Assert.AreEqual(17, row.OpTime.Length);
		var cond = JObject.Parse(row.Condition);
		Assert.AreEqual("LoginId = @0", (string?)cond["Where"]);
		Assert.AreEqual("oplog", (string?)cond["Parameters"]?[0]);
		Assert.AreEqual(nameof(SysLogin), (string?)cond["ItemType"]);
	}

	/// <summary>条件もパラメータも無い照会は既定では記録しない。設定で記録できる</summary>
	[TestMethod]
	public async Task EmptyQuery_SkippedByDefault() {
		await CreateService().QueryMsgAsync(Msg(CvFlag.Msg101_Op_Query, new QueryListParam(typeof(SysLogin))));
		Assert.HasCount(0, Rows());

		await CreateService(skipEmptyQuery: false).QueryMsgAsync(Msg(CvFlag.Msg101_Op_Query, new QueryListParam(typeof(SysLogin))));
		Assert.HasCount(1, Rows());
	}

	/// <summary>汎用追加はレコード本体を残さず、型・件数・Idだけを残す（H3）</summary>
	[TestMethod]
	public async Task GenericInsert_RecordsTypeCountIdsWithoutBody() {
		var service = CreateService();
		var item = new MasterShain { Code = "OPLOG01", Name = "個人情報テスト太郎" };
		var reply = await service.QueryMsgAsync(Msg(CvFlag.Msg201_Op_Execute, new InsertParam(typeof(MasterShain), Common.SerializeObject(item))));
		Assert.AreEqual(0, reply.Code, reply.Option);

		var row = Rows().Single();
		Assert.AreEqual(OperationKind.Update, row.Kind);
		Assert.DoesNotContain("個人情報テスト太郎", row.Condition);
		Assert.DoesNotContain("OPLOG01", row.Condition);
		var cond = JObject.Parse(row.Condition);
		Assert.AreEqual(nameof(MasterShain), (string?)cond["type"]);
		Assert.AreEqual(1, (int?)cond["count"]);
	}

	/// <summary>操作履歴への汎用の追加・更新・削除は拒否する（記録はサーバだけ）</summary>
	[TestMethod]
	public async Task GenericWriteToOperationLog_IsRejected() {
		var service = CreateService(level: 0);
		var fake = new SysHistOperation { Id = 1, Kind = OperationKind.MenuOpen, OpTime = "20260101000000000" };
		var json = Common.SerializeObject(fake);
		foreach (object param in new object[] {
			new InsertParam(typeof(SysHistOperation), json),
			new InsertBulkParam(typeof(SysHistOperation), $"[{json}]"),
			new UpdateParam(typeof(SysHistOperation), json),
			new DeleteParam(typeof(SysHistOperation), json),
			new DeleteByIdParam(typeof(SysHistOperation), 1, 0),
			new DeleteBulkParam(typeof(SysHistOperation), [new DeleteBulkRow(1, 0)]),
			new PartialUpdateParam(typeof(SysHistOperation), ["Kind"], [new PartialUpdateRow(1, 0, ["2"])]),
		}) {
			var reply = await service.QueryMsgAsync(Msg(CvFlag.Msg201_Op_Execute, param));
			Assert.AreEqual(CvMsgErrorCode.InvalidParameter, reply.Code, param.GetType().Name);
		}
		Assert.HasCount(0, Rows());
	}

	/// <summary>画面の起動・終了（Msg093）は利用者・IPをサーバで解決し、画面・機能は本文の値で残す</summary>
	[TestMethod]
	public async Task MenuOpenClose_RecordsSessionAndUser() {
		var service = CreateService(level: 1);
		var session = Guid.NewGuid().ToString("D");
		var open = await service.QueryMsgAsync(Msg(CvFlag.Msg093_OperationLog,
			new OperationLogParam(OperationKind.MenuOpen, session, "23AiQuery.AiQuery", "■ 管理 › AI照会（CSV）", "{\"initParam\":2}")));
		var close = await service.QueryMsgAsync(Msg(CvFlag.Msg093_OperationLog,
			new OperationLogParam(OperationKind.MenuClose, session, "23AiQuery.AiQuery", string.Empty, string.Empty)));
		Assert.AreEqual(0, open.Code);
		Assert.AreEqual(0, close.Code);

		var rows = Rows();
		Assert.HasCount(2, rows, "Msg093自身の受信は二重に記録しない");
		Assert.AreEqual(OperationKind.MenuOpen, rows[0].Kind);
		Assert.AreEqual(OperationKind.MenuClose, rows[1].Kind);
		Assert.IsTrue(rows.All(r => r.ScreenSession == session && r.FunctionId == "23AiQuery.AiQuery" && r.Id_Shain == ShainId));
		Assert.AreEqual("■ 管理 › AI照会（CSV）", rows[0].MenuPath);
		Assert.AreEqual(2, (int?)JObject.Parse(rows[0].Condition)["initParam"]);
	}

	/// <summary>Msg093 の種類が起動・終了以外なら拒否し、記録しない</summary>
	[TestMethod]
	public async Task MenuLog_InvalidKind_IsRejected() {
		var reply = await CreateService().QueryMsgAsync(Msg(CvFlag.Msg093_OperationLog,
			new OperationLogParam(OperationKind.Update, "s", "f", "", "")));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, reply.Code);
		Assert.HasCount(0, Rows());
	}

	/// <summary>記録レベル 0:なし 1:起動終了のみ 2:照会を除く 3:すべて</summary>
	[TestMethod]
	public async Task Level_FiltersKinds() {
		var query = Msg(CvFlag.Msg101_Op_Query, new QueryListParam(typeof(SysLogin), "Id = @0", null, ["1"]));
		var menu = Msg(CvFlag.Msg093_OperationLog, new OperationLogParam(OperationKind.MenuOpen, "s", "f", "", ""));
		var insert = Msg(CvFlag.Msg201_Op_Execute, new InsertParam(typeof(MasterShain), Common.SerializeObject(new MasterShain { Code = "L" })));

		foreach (var (level, expected) in new[] { (0, 0), (1, 1), (2, 2), (3, 3) }) {
			Db.Execute("DELETE FROM SysHistOperation");
			Db.Execute("DELETE FROM MasterShain WHERE Code = 'L'");
			var service = CreateService(level);
			await service.QueryMsgAsync(query);
			await service.QueryMsgAsync(menu);
			await service.QueryMsgAsync(insert);
			Assert.HasCount(expected, Rows(), $"Level={level}");
		}
	}

	/// <summary>記録に失敗しても業務の応答は変わらない（H5）</summary>
	[TestMethod]
	public async Task RecordFailure_DoesNotChangeReply() {
		Db.Execute("DROP TABLE SysHistOperation");
		var reply = await CreateService().QueryMsgAsync(Msg(CvFlag.Msg101_Op_Query,
			new QueryListParam(typeof(SysLogin), "LoginId = @0", null, ["oplog"])));
		Assert.AreEqual(0, reply.Code, reply.Option);
		var menu = await CreateService().QueryMsgAsync(Msg(CvFlag.Msg093_OperationLog, new OperationLogParam(OperationKind.MenuOpen, "s", "f", "", "")));
		Assert.AreEqual(0, menu.Code);
	}

	/// <summary>ストリーム処理は終了時に1行記録し、エラーコードを残す</summary>
	[TestMethod]
	public async Task Stream_RecordsOnCompletion() {
		var service = CreateService();
		var count = 0;
		await foreach (var _ in service.QueryMsgStreamAsync(new CvMsg { Flag = CvFlag.Msg002_GetVersion, DataType = typeof(string), DataMsg = "" })) {
			count++;
		}
		Assert.AreEqual(1, count);
		var row = Rows().Single();
		Assert.AreEqual(OperationKind.Execute, row.Kind);
		Assert.AreEqual(nameof(CvFlag.Msg002_GetVersion), row.Op);
		Assert.AreEqual(-1, row.ResultCode);
	}

	/// <summary>最大文字数を超える条件は {"truncated":true,"head":…} に包み、常に有効なJSONで列長に収める</summary>
	[TestMethod]
	public void BuildCondition_TruncatesToValidJson() {
		var where = string.Join(" or ", Enumerable.Range(0, 2000).Select(i => $"Name = \"x{i}\""));
		foreach (var max in new[] { 200, 1000 }) {
			var cond = OperationLogDb.BuildCondition(new QueryListParam(typeof(SysLogin), where), max);
			Assert.IsLessThanOrEqualTo(max, cond.Length);
			var token = JObject.Parse(cond);
			Assert.IsTrue((bool?)token["truncated"]);
		}
		Assert.IsLessThanOrEqualTo(4000, OperationLogDb.BuildCondition(new QueryListParam(typeof(SysLogin), where), 99999).Length);
	}

	/// <summary>CSV印刷の本体は残さず行数だけ。AI照会は最後の入力だけを残す</summary>
	[TestMethod]
	public void BuildCondition_PrintCsvAndAiChat() {
		var csv = JObject.Parse(OperationLogDb.BuildCondition(new PrintByCsvParam("a,b\n秘密,1\n")));
		Assert.AreEqual(2, (int?)csv["csvLines"]);
		Assert.HasCount(1, csv.Properties());

		var chat = new AiQueryChatParam {
			ConversationId = "c1",
			Messages = [
				new() { Role = EnumAiQueryRole.User, Text = "先月の売上" },
				new() { Role = EnumAiQueryRole.Assistant, Text = "AIの応答" },
				new() { Role = EnumAiQueryRole.User, Text = "店舗別に" },
			],
		};
		var ai = OperationLogDb.BuildCondition(chat);
		Assert.AreEqual("店舗別に", (string?)JObject.Parse(ai)["input"]);
		Assert.DoesNotContain("AIの応答", ai);
	}

	/// <summary>保持月数より古い行だけを削除する</summary>
	[TestMethod]
	public void Purge_DeletesOlderThanKeepMonths() {
		var logDb = new OperationLogDb(Db);
		logDb.Insert(new SysHistOperation { OpTime = "20250920235959999", Kind = 1 });
		logDb.Insert(new SysHistOperation { OpTime = "20250921000000000", Kind = 1 });
		logDb.Insert(new SysHistOperation { OpTime = "20261020120000000", Kind = 1 });

		Assert.AreEqual(0, logDb.Purge(new DateTime(2026, 10, 21), 0), "0以下は削除しない");
		Assert.AreEqual(1, logDb.Purge(new DateTime(2026, 10, 21), 13));
		CollectionAssert.AreEqual(new[] { "20250921000000000", "20261020120000000" }, Rows().Select(r => r.OpTime).ToArray());
	}

	[TestMethod]
	public void CountResultRows_CountsTopLevelArray() {
		Assert.AreEqual(0, OperationLogDb.CountResultRows("[]"));
		Assert.AreEqual(3, OperationLogDb.CountResultRows("[{\"a\":[1,2]},{\"b\":{}},3]"));
		Assert.AreEqual(-1, OperationLogDb.CountResultRows("{\"Id\":1}"));
		Assert.AreEqual(-1, OperationLogDb.CountResultRows("エラー"));
	}

	[TestMethod]
	public void ParseScreenHeader_SplitsSessionAndFunction() {
		Assert.AreEqual(("s1", "06Uriage.ShopUriageInput"), CoreService.ParseScreenHeader("s1|06Uriage.ShopUriageInput"));
		Assert.AreEqual(("", "MainMenu"), CoreService.ParseScreenHeader("|MainMenu"));
		Assert.AreEqual(("", ""), CoreService.ParseScreenHeader(null));
	}
}
