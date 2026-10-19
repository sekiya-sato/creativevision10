using CodeShare;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvServer;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using CvServer.Services.AiQuery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.CvServer;

/// <summary>
/// AI照会（Doc/spec/2026-10-19_AI-query_最小変更設計案.md）のサーバ側テスト。
/// </summary>
[TestClass]
public class AiQueryTests {
	private ExDatabaseSqlite? _db;
	private CoreService? _service;
	private ServiceProvider? _serviceProvider;
	private readonly HttpContextAccessor _httpContextAccessor = new();

	[TestInitialize]
	public void Initialize() {
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
		new AppGlobal().InitAsync(_db).Wait();
		_serviceProvider = new ServiceCollection().AddSingleton<ExDatabase>(_db).BuildServiceProvider();
		_service = CreateService(enabled: false, llm: null);
	}

	CoreService CreateService(bool enabled, IAiQueryLlm? llm) {
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
			["AiQuery:Enabled"] = enabled ? "true" : "false",
			["AiQuery:MaxRepair"] = "2",
			["AiQuery:MaxToolRounds"] = "6",
		}).Build();
		return new CoreService(NullLogger<CoreService>.Instance, config, new FakeWebHostEnvironment(), _httpContextAccessor, _db!,
			_serviceProvider!.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(_db!, NullLogger<PointOfSaleService>.Instance), aiQueryLlm: llm);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_serviceProvider?.Dispose();
	}

	CoreService Service => _service ?? throw new AssertFailedException("Service not initialized");

	/// <summary>
	/// Step 0: 既存 Msg101 + typeof(object) が列名付きの動的行を返し、列順・NULL・0がどう届くかを確認する。
	/// </summary>
	[TestMethod]
	public async Task QueryListSql_DynamicRows_KeepColumnOrderAndValues() {
		var request = new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(object),
				"SELECT 'x' AS Zcol, 2 AS Acol, NULL AS Ncol, 0 AS Zero, 1.5 AS Dcol, @0 AS P UNION ALL SELECT 'y', 3, 'n', 1, 2.5, @0",
				["20260901"]))
		};
		var result = await Service.QueryMsgAsync(request);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(typeof(List<object>), result.DataType);
		// クライアントは列名→値の辞書として読む。列順・NULL・0・パラメータが保たれること
		var rows = Common.DeserializeObject<List<Dictionary<string, object?>>>(result.DataMsg ?? "[]");
		Assert.IsNotNull(rows);
		Assert.HasCount(2, rows);
		CollectionAssert.AreEqual(new[] { "Zcol", "Acol", "Ncol", "Zero", "Dcol", "P" }, rows[0].Keys.ToArray());
		Assert.IsNull(rows[0]["Ncol"]);
		Assert.AreEqual(0L, rows[0]["Zero"]);
		Assert.AreEqual(1.5, rows[0]["Dcol"]);
		Assert.AreEqual("20260901", rows[1]["P"]);
	}

	void SetLogin(long loginId) {
		var identity = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.SerialNumber, loginId.ToString())]);
		_httpContextAccessor.HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity) };
	}

	Task<CvMsg> Execute(object param) => Service.QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg201_Op_Execute, DataType = param.GetType(), DataMsg = Common.SerializeObject(param)
	});

	static SysAiQuery NewSaved() => new() {
		SaveDate = "19990101",
		Title = "先月の店舗別売上",
		SqlText = "SELECT 1 WHERE @0 = @0",
		SqlParams = Common.SerializeObject(new List<AiQueryParam> {
			new() { Name = "from", Label = "期間開始", Type = EnumAiQueryParamType.Date, Value = "20260901" },
			new() { Name = "zero", Label = "ゼロ", Type = EnumAiQueryParamType.Text, Value = "0", AskOnRun = false },
			new() { Name = "n", Label = "数値", Type = EnumAiQueryParamType.Integer, Value = "", AskOnRun = true },
		}),
		Id_Login = 999,
		Description = "説明",
	};

	/// <summary>
	/// Step 1: 共有保存は既存の汎用追加・更新で保存でき、保存日と保存者はサーバが打刻する。
	/// </summary>
	[TestMethod]
	public async Task SaveSysAiQuery_StampsDateAndLoginOnServer() {
		SetLogin(7);
		var insert = await Execute(new InsertParam(typeof(SysAiQuery), Common.SerializeObject(NewSaved())));
		Assert.AreEqual(0, insert.Code, insert.Option);
		var saved = _db!.Fetch<SysAiQuery>("where Title=@0", "先月の店舗別売上").Single();
		Assert.AreEqual(DateTime.Now.ToString("yyyyMMdd"), saved.SaveDate);
		Assert.AreEqual(7L, saved.Id_Login);

		// パラメータの型・"0"・空文字・AskOnRun・順序が往復で変わらない
		var parameters = Common.DeserializeObject<List<AiQueryParam>>(saved.SqlParams)!;
		CollectionAssert.AreEqual(new[] { "from", "zero", "n" }, parameters.Select(x => x.Name).ToArray());
		Assert.AreEqual(EnumAiQueryParamType.Text, parameters[1].Type);
		Assert.AreEqual("0", parameters[1].Value);
		Assert.IsFalse(parameters[1].AskOnRun);
		Assert.AreEqual(string.Empty, parameters[2].Value);
		Assert.IsTrue(parameters[2].AskOnRun);

		// 別ユーザが上書きすると保存者が置き換わる（権限制御には使わない）
		SetLogin(8);
		saved.Title = "変更後";
		saved.Id_Login = 1;
		var update = await Execute(new UpdateParam(typeof(SysAiQuery), Common.SerializeObject(saved)));
		Assert.AreEqual(0, update.Code, update.Option);
		var updated = _db!.Fetch<SysAiQuery>("where Id=@0", saved.Id).Single();
		Assert.AreEqual("変更後", updated.Title);
		Assert.AreEqual(8L, updated.Id_Login);
	}

	/// <summary>
	/// Step 1: 打刻を通らない一括追加・部分更新は拒否する。
	/// </summary>
	[TestMethod]
	public async Task SaveSysAiQuery_RejectsBulkWrites() {
		var bulk = await Execute(new InsertBulkParam(typeof(SysAiQuery), Common.SerializeObject(new List<SysAiQuery> { NewSaved() })));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, bulk.Code);
		var partial = await Execute(new PartialUpdateParam(typeof(SysAiQuery), [nameof(SysAiQuery.Id_Login)], []));
		Assert.AreEqual(CvMsgErrorCode.InvalidParameter, partial.Code);
		Assert.AreEqual(0, _db!.Fetch<SysAiQuery>("").Count);
	}

	static AiQueryParam P(EnumAiQueryParamType type, string value, string label = "p") => new() { Type = type, Value = value, Label = label };

	AiQueryValidateResult Validate(string sql, params AiQueryParam[] parameters) =>
		new CvDomainLogic.AiQuery.AiQuerySqlGuard(_db!).Validate(sql, parameters);

	/// <summary>Step 2: 単一の照会SQLは通り、参照表が返る。</summary>
	[TestMethod]
	public void Validate_AcceptsSingleSelect() {
		var ok = Validate("""
			SELECT h.DenDay, COUNT(*) AS cnt -- 件数; 日別
			  FROM Tran00Uriage h
			 WHERE h.DenDay BETWEEN @0 AND @1 AND h.Id_Soko = CAST(@2 AS INTEGER) AND ';' <> ''
			 GROUP BY h.DenDay ORDER BY h.DenDay;
			""", P(EnumAiQueryParamType.Date, "20260901"), P(EnumAiQueryParamType.Date, "20260930"), P(EnumAiQueryParamType.Integer, "1"));
		Assert.IsTrue(ok.IsValid, string.Join(" / ", ok.Errors));
		CollectionAssert.Contains(ok.Tables, "Tran00Uriage");

		var cte = Validate("WITH x AS (SELECT DenDay FROM Tran00Uriage) SELECT * FROM x");
		Assert.IsTrue(cte.IsValid, string.Join(" / ", cte.Errors));
	}

	/// <summary>Step 2: 更新・複数文・照会以外・システム表・名前解決できないSQLは拒否する。</summary>
	[TestMethod]
	[DataRow("SELECT 1; DELETE FROM Tran00Uriage", "複数")]
	[DataRow("DELETE FROM Tran00Uriage", "SELECT")]
	[DataRow("UPDATE Tran00Uriage SET DenDay = ''", "SELECT")]
	[DataRow("WITH x AS (SELECT 1) DELETE FROM Tran00Uriage", "CTE")]
	[DataRow("PRAGMA table_info(Tran00Uriage)", "SELECT")]
	[DataRow("VALUES (1)", "SELECT")]
	[DataRow("EXPLAIN SELECT 1", "SELECT")]
	[DataRow("ATTACH DATABASE 'x.db' AS x", "SELECT")]
	[DataRow("SELECT * FROM SysLogin", "システムテーブル")]
	[DataRow("SELECT u.DenDay FROM Tran00Uriage u JOIN SysLogin l ON l.Id = u.Id", "システムテーブル")]
	[DataRow("SELECT name FROM sqlite_master", "sqlite_schema")]
	[DataRow("SELECT NoSuchColumn FROM Tran00Uriage", "SQLエラー")]
	[DataRow("SELECT * FROM NoSuchTable", "SQLエラー")]
	[DataRow("SELECT ?", "番号")]
	[DataRow("SELECT @0", "パラメータ数")]
	public void Validate_RejectsUnsafeOrInvalidSql(string sql, string expected) {
		var result = Validate(sql);
		Assert.IsFalse(result.IsValid, sql);
		Assert.IsTrue(result.Errors.Any(e => e.Contains(expected)), $"{sql} → {string.Join(" / ", result.Errors)}");
	}

	/// <summary>Step 2: 数値パラメータは CAST 必須（文字列バインドによる比較ずれを防ぐ）。</summary>
	[TestMethod]
	public void Validate_RequiresCastForNumericParameters() {
		var bare = Validate("SELECT * FROM Tran00Uriage WHERE Id_Soko = @0", P(EnumAiQueryParamType.Integer, "1", "倉庫"));
		Assert.IsFalse(bare.IsValid);
		StringAssert.Contains(bare.Errors[0], "CAST(@0");
		var casted = Validate("SELECT * FROM Tran00Uriage WHERE Id_Soko = CAST(@0 AS INTEGER)", P(EnumAiQueryParamType.Integer, "1", "倉庫"));
		Assert.IsTrue(casted.IsValid, string.Join(" / ", casted.Errors));
	}

	/// <summary>Step 2: Msg092 は検査結果を業務応答として返す。</summary>
	[TestMethod]
	public async Task Msg092_ReturnsValidateResult() {
		async Task<AiQueryValidateResult> Send(string sql) {
			var reply = await Service.QueryMsgAsync(new CvMsg {
				Flag = CvFlag.Msg092_AiQueryValidate,
				DataType = typeof(AiQueryValidateParam),
				DataMsg = Common.SerializeObject(new AiQueryValidateParam { Sql = sql }),
			});
			Assert.AreEqual(0, reply.Code, reply.Option);
			return Common.DeserializeObject<AiQueryValidateResult>(reply.DataMsg ?? "{}")!;
		}
		Assert.IsTrue((await Send("SELECT COUNT(*) FROM Tran00Uriage")).IsValid);
		var ng = await Send("SELECT 1; DELETE FROM Tran00Uriage");
		Assert.IsFalse(ng.IsValid);
		Assert.HasCount(1, ng.Errors);
	}

	/// <summary>Step 2: スキーマ説明は Sys* を出さず、列とJSON内項目に業務説明を付ける。</summary>
	[TestMethod]
	public void SchemaCatalog_DescribesTablesWithComments() {
		var catalog = new CvDomainLogic.AiQuery.AiQuerySchemaCatalog(_db!);
		var tables = catalog.ListTables();
		Assert.IsFalse(tables.Any(t => t.Name.StartsWith("Sys", StringComparison.OrdinalIgnoreCase)));
		Assert.IsTrue(tables.Any(t => t.Name == "Tran00Uriage" && t.Comment.Length > 0));

		var text = catalog.DescribeTable("tran00uriage");
		StringAssert.Contains(text, "テーブル Tran00Uriage");
		StringAssert.Contains(text, "- DenDay ");
		StringAssert.Contains(text, "要素型 Tran99Meisai");
		StringAssert.Contains(text, "$.Id_Shohin (integer) : 商品ユニークキー");
		StringAssert.Contains(catalog.DescribeTable("Tran01Tenuri"), "- Id_Tenpo NUMBER NOT NULL : 店舗キー [参照: MasterTokui.Id（MasterTokui.TenType=6）]");

		StringAssert.Contains(catalog.DescribeTable("SysLogin"), "参照できません");
		StringAssert.Contains(catalog.DescribeTable("NoSuchTable"), "存在しません");
		StringAssert.Contains(catalog.DescribeTable("x' OR '1'='1"), "存在しません");
	}

	/// <summary>
	/// 社内LLM（appsettings の AiQuery:Endpoint）への実接続確認。通常のテストでは実行しない。
	/// 環境変数 CV_AIQUERY_LIVE=1 のときだけ動かす（例: http://127.0.0.1:1234/v1 / ornith-1.5-9b）。
	/// </summary>
	[TestMethod]
	public async Task Live_LocalLlm_AsksThenGeneratesValidSql() {
		if (Environment.GetEnvironmentVariable("CV_AIQUERY_LIVE") != "1") {
			Assert.Inconclusive("CV_AIQUERY_LIVE=1 のときだけ実行する");
		}
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
			["AiQuery:Enabled"] = "true",
			["AiQuery:Endpoint"] = Environment.GetEnvironmentVariable("CV_AIQUERY_ENDPOINT") ?? "http://127.0.0.1:1234/v1",
			["AiQuery:Model"] = Environment.GetEnvironmentVariable("CV_AIQUERY_MODEL") ?? "ornith-1.5-9b",
			["AiQuery:DictionaryPath"] = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../CvServer/AiQueryDictionary.md")),
		}).Build();
		var llm = new OpenAiCompatibleLlm(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, config, NullLogger<OpenAiCompatibleLlm>.Instance);
		var service = new CoreService(NullLogger<CoreService>.Instance, config, new FakeWebHostEnvironment(), _httpContextAccessor, _db!,
			_serviceProvider!.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(_db!, NullLogger<PointOfSaleService>.Instance), aiQueryLlm: llm);

		var watch = System.Diagnostics.Stopwatch.StartNew();
		var ask = await Chat(service, EnumAiQueryMode.Chat, null, "先月の店舗別売上をCSVで欲しい");
		Console.WriteLine($"[Chat {watch.Elapsed.TotalSeconds:F1}s] {ask.Status} {ask.AssistantText}\nKnownTables={string.Join(",", ask.KnownTables)}\nSpec={Common.SerializeObject(ask.Spec ?? new())}");
		Assert.AreNotEqual(EnumAiQueryStatus.Failed, ask.Status, ask.AssistantText);

		watch.Restart();
		var spec = new AiQuerySpec {
			Target = "店舗売上 Tran01Tenuri", Metrics = ["純売上金額(税抜、返品・値引控除)", "伝票数"], GroupBy = ["店舗"],
			DateBasis = "計上日 DenDay", PeriodFrom = "20260901", PeriodTo = "20260930", TaxMode = "税抜", ReturnMode = "返品・値引を控除",
			OutputColumns = ["店舗コード", "店舗名", "伝票数", "純売上金額"], OrderBy = ["店舗コード"],
		};
		var param = new AiQueryChatParam {
			ConversationId = "live", Revision = 2, BaseDate = "20261019", Mode = EnumAiQueryMode.Generate, Spec = spec, KnownTables = [.. ask.KnownTables],
			Messages = [new() { Text = "先月の店舗別売上をCSVで欲しい" }, new() { Role = EnumAiQueryRole.Assistant, Text = ask.AssistantText },
				new() { Text = "店舗売上、計上日、税抜、返品・値引は控除、伝票数も。店舗コード順で。" }],
		};
		var reply = await service.QueryMsgAsync(new CvMsg { Flag = CvFlag.Msg091_AiQueryChat, DataType = typeof(AiQueryChatParam), DataMsg = Common.SerializeObject(param) });
		var gen = Common.DeserializeObject<AiQueryChatResult>(reply.DataMsg ?? "{}")!;
		Console.WriteLine($"[Generate {watch.Elapsed.TotalSeconds:F1}s] {gen.Status} {gen.Draft?.Title}\n{gen.Draft?.Sql}\nParams={Common.SerializeObject(gen.Draft?.Parameters ?? [])}\nDesc={gen.Draft?.Description}\nErrors={string.Join(" / ", gen.Errors)}");
		Assert.AreEqual(EnumAiQueryStatus.Generated, gen.Status, string.Join(" / ", gen.Errors));
	}

	/// <summary>LLMの固定応答。受け取った messages を記録する。</summary>
	sealed class FakeLlm(params Func<JArray, JObject>[] replies) : IAiQueryLlm {
		int _index;
		public List<JArray> Requests { get; } = [];
		public List<JArray> ToolSets { get; } = [];
		public Task<JObject> ChatAsync(JArray messages, JArray tools, CancellationToken ct) {
			Requests.Add((JArray)messages.DeepClone());
			ToolSets.Add(tools);
			if (_index >= replies.Length) {
				throw new AssertFailedException("想定より多くLLMが呼ばれました");
			}
			return Task.FromResult(replies[_index++](messages));
		}
	}

	static Func<JArray, JObject> Call(string name, object args) => _ => new JObject {
		["role"] = "assistant",
		["content"] = "",
		["tool_calls"] = new JArray(new JObject {
			["id"] = Guid.NewGuid().ToString("N"),
			["type"] = "function",
			["function"] = new JObject { ["name"] = name, ["arguments"] = args as string ?? JObject.FromObject(args).ToString() },
		}),
	};

	static Func<JArray, JObject> Text(string text) => _ => new JObject { ["role"] = "assistant", ["content"] = text };

	async Task<AiQueryChatResult> Chat(CoreService service, EnumAiQueryMode mode, AiQuerySpec? spec = null, params string[] userTexts) {
		var param = new AiQueryChatParam {
			ConversationId = "conv-1",
			Revision = 3,
			BaseDate = "20261019",
			Mode = mode,
			Spec = spec,
			Messages = [.. userTexts.Select(t => new AiQueryMessage { Role = EnumAiQueryRole.User, Text = t })],
		};
		var reply = await service.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg091_AiQueryChat, DataType = typeof(AiQueryChatParam), DataMsg = Common.SerializeObject(param),
		});
		Assert.AreEqual(0, reply.Code, reply.Option);
		return Common.DeserializeObject<AiQueryChatResult>(reply.DataMsg ?? "{}")!;
	}

	static readonly object AskArgs = new {
		action = "ask", message = "店舗売上と本部売上のどちらですか？",
		spec = new { Target = "売上", Metrics = "売上金額", GroupBy = new[] { "店舗" } },
		unresolved = new[] { "対象伝票" },
	};

	/// <summary>Step 3: describe_table をサーバが実行して結果を返し、reply(ask) で質問になる。</summary>
	[TestMethod]
	public async Task Chat_RunsInfoToolThenAsks() {
		var llm = new FakeLlm(Call("describe_table", new { table = "Tran01Tenuri" }), Text("ツールを使わない応答"), Call("reply", AskArgs));
		var result = await Chat(CreateService(true, llm), EnumAiQueryMode.Chat, null, "先月の店舗別売上が欲しい");

		Assert.AreEqual(EnumAiQueryStatus.Asking, result.Status);
		Assert.AreEqual("conv-1", result.ConversationId);
		Assert.AreEqual(3, result.Revision);
		StringAssert.Contains(result.AssistantText, "どちら");
		CollectionAssert.Contains(result.KnownTables, "Tran01Tenuri");
		CollectionAssert.AreEqual(new[] { "売上金額" }, result.Spec!.Metrics);
		CollectionAssert.AreEqual(new[] { "対象伝票" }, result.Spec.Unresolved);

		// 2回目の要求にツール結果（列説明）が、3回目にツールで返すよう促す文が入っている
		var toolMessage = llm.Requests[1].Last();
		Assert.AreEqual("tool", toolMessage.Value<string>("role"));
		StringAssert.Contains(toolMessage.Value<string>("content"), "テーブル Tran01Tenuri");
		StringAssert.Contains(llm.Requests[2].Last().Value<string>("content"), "ツールで返して");
		// システムプロンプトに基準日から求めた先月が入り、Chat では submit_sql を渡さない
		StringAssert.Contains(llm.Requests[0][0].Value<string>("content"), "先月: 20260901〜20260930");
		Assert.IsFalse(llm.ToolSets[0].ToString().Contains("submit_sql"));
	}

	/// <summary>Step 3: 未解決事項が残る confirm は質問扱い。解決済みなら条件確認待ち。</summary>
	[TestMethod]
	public async Task Chat_ConfirmOnlyWhenNothingUnresolved() {
		var pending = await Chat(CreateService(true, new FakeLlm(Call("reply", new { action = "confirm", message = "m", spec = new { }, unresolved = new[] { "税" } }))),
			EnumAiQueryMode.Chat, null, "q");
		Assert.AreEqual(EnumAiQueryStatus.Asking, pending.Status);
		var done = await Chat(CreateService(true, new FakeLlm(Call("reply", new { action = "confirm", message = "要約", spec = new { PeriodFrom = "20260901" }, unresolved = Array.Empty<string>() }))),
			EnumAiQueryMode.Chat, null, "q");
		Assert.AreEqual(EnumAiQueryStatus.Confirming, done.Status);
		Assert.AreEqual("20260901", done.Spec!.PeriodFrom);
	}

	static object Submit(string sql, params object[] parameters) => new {
		sql, parameters, columns = new[] { "日付", "件数" }, title = "日別件数", description = "店舗売上の日別件数", reason = "r",
	};

	/// <summary>Step 3: 検査に通らないSQLはエラーを返して修正させ、通ったら Generated。</summary>
	[TestMethod]
	public async Task Generate_RepairsInvalidSqlThenSucceeds() {
		var llm = new FakeLlm(
			Call("submit_sql", Submit("DELETE FROM Tran01Tenuri")),
			Call("submit_sql", Submit("SELECT h.DenDay AS 日付, COUNT(*) AS 件数 FROM Tran01Tenuri h WHERE h.DenDay BETWEEN @0 AND @1 GROUP BY h.DenDay",
				new { name = "from", label = "期間開始", type = "Date", value = "20260901" },
				new { name = "to", label = "期間終了", type = "Date", value = "20260930" })));
		var result = await Chat(CreateService(true, llm), EnumAiQueryMode.Generate, new AiQuerySpec { PeriodFrom = "20260901" }, "店舗売上の日別件数");

		Assert.AreEqual(EnumAiQueryStatus.Generated, result.Status, string.Join(" / ", result.Errors));
		Assert.HasCount(2, result.Draft!.Parameters);
		Assert.AreEqual(EnumAiQueryParamType.Date, result.Draft.Parameters[0].Type);
		Assert.AreEqual("日別件数", result.Draft.Title);
		Assert.HasCount(0, result.Errors);
		StringAssert.Contains(llm.Requests[1].Last().Value<string>("content"), "検査に通りませんでした");
		StringAssert.Contains(llm.Requests[0].Last().Value<string>("content"), "submit_sql で提出");
		Assert.IsFalse(llm.ToolSets[0].ToString().Contains("\"reply\""));
	}

	/// <summary>Step 3: 修正回数を超えたら Failed。最後のSQLとエラーは返す。</summary>
	[TestMethod]
	public async Task Generate_FailsAfterMaxRepair() {
		var bad = Call("submit_sql", Submit("SELECT NoSuchColumn FROM Tran01Tenuri"));
		var result = await Chat(CreateService(true, new FakeLlm(bad, bad, bad)), EnumAiQueryMode.Generate, new AiQuerySpec(), "x");
		Assert.AreEqual(EnumAiQueryStatus.Failed, result.Status);
		StringAssert.Contains(result.Draft!.Sql, "NoSuchColumn");
		Assert.HasCount(1, result.Errors);
		StringAssert.Contains(result.Errors[0], "SQLエラー");
	}

	/// <summary>Step 3: 未確定事項・無効設定・LLM障害・不正な引数の扱い。</summary>
	[TestMethod]
	public async Task Chat_FailureCases() {
		var unresolved = await Chat(CreateService(true, new FakeLlm()), EnumAiQueryMode.Generate, new AiQuerySpec { Unresolved = ["税"] }, "x");
		Assert.AreEqual(EnumAiQueryStatus.Failed, unresolved.Status);
		CollectionAssert.AreEqual(new[] { "税" }, unresolved.Errors);

		var disabled = await Chat(CreateService(false, new FakeLlm()), EnumAiQueryMode.Chat, null, "x");
		Assert.AreEqual(EnumAiQueryStatus.Failed, disabled.Status);
		StringAssert.Contains(disabled.AssistantText, "設定されていません");

		var down = await Chat(CreateService(true, new FakeLlm(_ => throw new HttpRequestException("接続拒否"))), EnumAiQueryMode.Chat, null, "x");
		Assert.AreEqual(EnumAiQueryStatus.Failed, down.Status);
		StringAssert.Contains(down.AssistantText, "接続拒否");

		var llm = new FakeLlm(Call("reply", "{not json"), Call("reply", AskArgs));
		var recovered = await Chat(CreateService(true, llm), EnumAiQueryMode.Chat, null, "x");
		Assert.AreEqual(EnumAiQueryStatus.Asking, recovered.Status);
		StringAssert.Contains(llm.Requests[1].Last().Value<string>("content"), "解析できません");
	}
}
