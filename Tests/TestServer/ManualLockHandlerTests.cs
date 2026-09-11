using System;
using System.Linq;
using System.Security.Claims;
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
/// マニュアル排他制御の強制クリア（<c>Msg061_ManualLockStatus</c>/<c>Msg062_ManualLockClear</c>）を
/// <see cref="CoreService"/>のハンドラ層越しに叩く単体テスト（Step T3）。
/// <para>
/// 正典は `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` E-03（サーバー側）・E-09（サーバー側）、
/// `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §2.5.3。
/// </para>
/// <para>
/// <see cref="ManualLockClearTests"/>は<see cref="ManualLockDb"/>を直接呼ぶテストであり、
/// <c>HandlerClass.HandleManualLockStatus</c>/<c>HandleManualLockClear</c>が持つ
/// <c>ResolveLoginShainId</c>（JWTから実行社員を解決する、クライアント申告値を使わないための処理）は
/// 未検証だった。本クラスはそこを埋める。
/// </para>
/// <para>
/// ハンドラは<c>private</c>だが、<c>CoreService</c>自体は<see cref="CoreServiceCostHandlersTests"/>と同じ作法で
/// フェイク依存で直接インスタンス化し、<c>QueryMsgAsync</c>(public)経由でハンドラへ到達する。
/// リフレクションは<c>_handlers</c>の存在確認以外には使わない（既存作法の範囲）。
/// </para>
/// </summary>
[TestClass]
public class ManualLockHandlerTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private CoreService? _service;
	private HttpContextAccessor? _httpContextAccessor;
	private ServiceProvider? _scopeFactoryProvider;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockHandlerTests-{Guid.NewGuid():N}";
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
		Db.CreateTable(typeof(SysLogin), true, false);
		Db.CreateTable(typeof(SysHistJwt), true, false);

		_httpContextAccessor = new HttpContextAccessor();
		_scopeFactoryProvider = new ServiceCollection().BuildServiceProvider();
		_service = new CoreService(
			NullLogger<CoreService>.Instance,
			new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
			new FakeWebHostEnvironment(),
			_httpContextAccessor,
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

	/// <summary>
	/// <paramref name="loginId"/>を<c>SysLogin.Id</c>、<paramref name="idShain"/>を<c>SysLogin.Id_Shain</c>として
	/// 行を1件投入し、<c>ClaimTypes.SerialNumber</c>に<paramref name="loginId"/>を積んだ
	/// <see cref="ClaimsPrincipal"/>を<c>HttpContext.User</c>へセットする。
	/// <see cref="HandlerClass.ResolveLoginShainId"/>がここからJWT経由で実行社員を解決する。
	/// </summary>
	private void SetLoggedInUser(long loginId, long idShain) {
		Db.Insert(new SysLogin {
			Id_Shain = idShain,
			LoginId = $"login{loginId}",
			CryptPassword = "dummy",
		});
		// SysLoginはAutoIncrementのIdを持つため、テストで指定したloginIdへ強制的に合わせる。
		Db.Execute($"UPDATE {nameof(SysLogin)} SET Id=@0 WHERE Id_Shain=@1", loginId, idShain);

		var identity = new ClaimsIdentity([new Claim(ClaimTypes.SerialNumber, loginId.ToString())]);
		SetHttpContextUser(new ClaimsPrincipal(identity));
	}

	private void SetHttpContextUser(ClaimsPrincipal principal) {
		_httpContextAccessor!.HttpContext = new DefaultHttpContext { User = principal };
	}

	/// <summary>
	/// <see cref="HandlerClass.ResolveDeclaredDeviceInfo"/>がリモートIPを読めるよう、
	/// <c>HttpContext</c>の<c>Connection.RemoteIpAddress</c>を設定する。
	/// </summary>
	private void SetRemoteIpAddress(string ip) {
		var context = _httpContextAccessor!.HttpContext ?? new DefaultHttpContext();
		context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
		_httpContextAccessor.HttpContext = context;
	}

	/// <summary>
	/// <paramref name="loginId"/>（<c>SysHistJwt.Id_Login</c>）向けに、ログイン時のクライアント申告値を
	/// 積んだ<see cref="SysHistJwt"/>行を1件投入する。<see cref="HandlerClass.ResolveDeclaredDeviceInfo"/>が
	/// ここから端末情報(申告値)を読む。
	/// </summary>
	private void InsertJwtHistory(long loginId, string machine, string user, string osVer, string macAddress) {
		Db.Insert(new SysHistJwt {
			Id_Login = loginId,
			Jsub = new SysHistJwtSub { Machine = machine, User = user, OsVer = osVer, MacAddress = macAddress },
		});
	}

	private System.Collections.Generic.List<SysSequence> FetchAllSequences() =>
		Db.Fetch<SysSequence>($"SELECT * FROM {nameof(SysSequence)} ORDER BY Id");

	private System.Collections.Generic.List<SysHistAutoexec> FetchAllHistories() =>
		Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}");

	private async System.Threading.Tasks.Task<CvMsg> InvokeAsync(CvFlag flag, string dataMsg = "") {
		var request = new CvMsg { Flag = flag, DataType = typeof(string), DataMsg = dataMsg };
		return await Service.QueryMsgAsync(request);
	}

	// ==================================================================
	// Msg061_ManualLockStatus（状態照会）
	// ==================================================================

	[TestMethod]
	public async System.Threading.Tasks.Task Msg061_排他行が0件なら空のRowsとHasLikelyAlive_falseを返す() {
		SetLoggedInUser(loginId: 1, idShain: 111);

		var response = await InvokeAsync(CvFlag.Msg061_ManualLockStatus);

		Assert.AreEqual(0, response.Code);
		var status = Common.DeserializeObject<ManualLockStatus>(response.DataMsg);
		Assert.IsNotNull(status);
		Assert.AreEqual(0, status!.Rows.Count);
		Assert.IsFalse(status.HasLikelyAlive);

		// 照会のみでDBが変更されないこと。
		Assert.AreEqual(0, FetchAllSequences().Count);
		Assert.AreEqual(0, FetchAllHistories().Count);
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg061_排他行が1件あるとVduと経過時間とIsLikelyAliveを含めて返す() {
		SetLoggedInUser(loginId: 1, idShain: 111);
		var lockDb = new ManualLockDb(Db);
		var begun = lockDb.TryBegin("在庫・掛再集計", "買掛集計", expectedDurationSeconds: 600, memo: "実行中メモ");
		Assert.IsTrue(begun.IsAcquired);

		var response = await InvokeAsync(CvFlag.Msg061_ManualLockStatus);

		Assert.AreEqual(0, response.Code);
		var status = Common.DeserializeObject<ManualLockStatus>(response.DataMsg);
		Assert.IsNotNull(status);
		Assert.AreEqual(1, status!.Rows.Count);
		var row = status.Rows[0];
		Assert.AreEqual("在庫・掛再集計", row.TableName);
		Assert.AreEqual("買掛集計", row.ColumnName);
		Assert.AreEqual(1, row.SeqNo);
		Assert.AreEqual(600, row.ExpectedDuration);
		Assert.IsTrue(row.Memo.Contains("実行中メモ"));
		Assert.IsTrue(row.Vdu > 0);
		Assert.IsTrue(row.ElapsedSecondsSinceVdu >= 0);
		// 開始直後なので閾値(15分下限)未満のはず。
		Assert.IsTrue(row.IsLikelyAlive);
		Assert.IsTrue(status.HasLikelyAlive);
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg061_SysSeqType0の行は返さない() {
		SetLoggedInUser(loginId: 1, idShain: 111);
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterShohin", ColumnName = "Code", SeqNo = 1 });

		var response = await InvokeAsync(CvFlag.Msg061_ManualLockStatus);

		var status = Common.DeserializeObject<ManualLockStatus>(response.DataMsg);
		Assert.IsNotNull(status);
		Assert.AreEqual(0, status!.Rows.Count);
	}

	// ==================================================================
	// Msg062_ManualLockClear（強制クリア）
	// ==================================================================

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_SysSeqType1の行が全件消えSysSeqType0の行は残る() {
		SetLoggedInUser(loginId: 1, idShain: 111);
		// TryBeginは排他そのもの(単一勝者)のため2行同時には残らない。
		// ManualLockClearTestsに合わせ、2件同時に排他が立っている状態はDirect Insertで模す。
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "在庫・掛再集計",
			ColumnName = "買掛集計",
			SeqNo = 1,
			Memo = "1件目",
		});
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "現在庫再集計",
			ColumnName = "現在庫集計",
			SeqNo = 1,
			Memo = "2件目",
		});
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterShohin", ColumnName = "Code", SeqNo = 1 });

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var deletedCount = Common.DeserializeObject<int>(response.DataMsg);
		Assert.AreEqual(2, deletedCount);

		var remaining = FetchAllSequences();
		Assert.AreEqual(1, remaining.Count);
		Assert.IsFalse(remaining.Any(r => r.SysSeqType == (int)EmSysSeqType.ManualLock));
		Assert.AreEqual((int)EmSysSeqType.TableSeq, remaining[0].SysSeqType);
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_履歴へSysHistType1とTaskNameとJWT由来の実行社員を記録する() {
		// SysLogin.Id と Id_Shain をわざと別の値にする。記録されるべきは Id_Shain(4321) であり、
		// もし実装が誤って Id(7) をそのまま記録していたらこのテストで検出できる。
		SetLoggedInUser(loginId: 7, idShain: 4321);
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var deletedCount = Common.DeserializeObject<int>(response.DataMsg);
		Assert.AreEqual(1, deletedCount);

		var histories = FetchAllHistories();
		Assert.AreEqual(1, histories.Count);
		var history = histories[0];
		Assert.AreEqual((int)EmSysHistType.ManualExec, history.SysHistType);
		Assert.AreEqual(ManualLockDb.ManualLockClearTaskName, history.TaskName);
		Assert.AreEqual(deletedCount, history.Count);
		Assert.IsTrue(history.Memo.Contains("在庫・掛再集計"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("買掛集計"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("実行社員Id=4321"), $"Memo={history.Memo} (Id=7が誤って記録されていないか)");
		Assert.IsFalse(history.Memo.Contains("実行社員Id=7"), "SysLogin.Idではなく Id_Shain が記録されるべき");
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_クライアントは実行社員を申告できない() {
		// CvMsg.DataMsg はハンドラ(HandleManualLockClear)側で一切参照されない
		// (CvServer/Services/HandlerClass.cs の該当メソッドは request からIDを読み取らず、
		// ResolveLoginShainId() のみで実行社員を決めている)。
		// 要求DTOには社員Idを積む余地が構造上無いため、代わりにJWTを差し替えると
		// 記録される実行社員が変わることを確認する。
		SetLoggedInUser(loginId: 1, idShain: 100);
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "1回目");

		// リクエストのDataMsgに社員Idらしき値を積んでみても、ハンドラはこれを読まない。
		var request = new CvMsg {
			Flag = CvFlag.Msg062_ManualLockClear,
			DataType = typeof(string),
			DataMsg = Common.SerializeObject(new { Id_Shain = 999999 }),
		};
		await Service.QueryMsgAsync(request);

		var firstHistory = FetchAllHistories().Single();
		Assert.IsTrue(firstHistory.Memo.Contains("実行社員Id=100"));
		Assert.IsFalse(firstHistory.Memo.Contains("999999"), "DataMsgに積んだ値は記録に使われないこと");

		// JWTを差し替えると、同じリクエスト形のままでも記録される実行社員が変わること。
		SetLoggedInUser(loginId: 2, idShain: 200);
		lockDb.TryBegin("現在庫再集計", "現在庫集計", 300, "2回目");
		await Service.QueryMsgAsync(request);

		var histories = FetchAllHistories();
		Assert.AreEqual(2, histories.Count);
		Assert.IsTrue(histories[1].Memo.Contains("実行社員Id=200"));
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_JWTのclaimが無い場合は実行社員0で記録される() {
		SetHttpContextUser(new ClaimsPrincipal(new ClaimsIdentity()));
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var deletedCount = Common.DeserializeObject<int>(response.DataMsg);
		Assert.AreEqual(1, deletedCount);
		var history = FetchAllHistories().Single();
		Assert.IsTrue(history.Memo.Contains("実行社員Id=0"), $"Memo={history.Memo}");
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_排他行が0件のときは履歴が増えない() {
		SetLoggedInUser(loginId: 1, idShain: 111);

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var deletedCount = Common.DeserializeObject<int>(response.DataMsg);
		Assert.AreEqual(0, deletedCount);
		Assert.AreEqual(0, FetchAllHistories().Count);
	}

	// ==================================================================
	// Msg062_ManualLockClear: 端末情報の記録（テスト計画§6.1、詳細設計§2.5.3 Step T8）
	// ==================================================================

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_実行社員が判明していても端末情報がIPと申告値として記録される() {
		SetLoggedInUser(loginId: 7, idShain: 4321);
		SetRemoteIpAddress("192.168.1.10");
		InsertJwtHistory(loginId: 7, machine: "PC-SALES01", user: "yamada", osVer: "Windows 11", macAddress: "00-11-22-33-44-55");
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var history = FetchAllHistories().Single();
		// 実行社員が判明していても常に端末情報を記録すること(設計書§2.5.3の理由3点)。
		Assert.IsTrue(history.Memo.Contains("実行社員Id=4321"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("IP=192.168.1.10"), $"Memo={history.Memo}");
		// マシン名・ユーザー名・OSバージョン・MACアドレスは申告値であることが分かる形で記録すること。
		Assert.IsTrue(history.Memo.Contains("申告Machine=PC-SALES01"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告User=yamada"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告OsVer=Windows 11"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告MacAddress=00-11-22-33-44-55"), $"Memo={history.Memo}");
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_JWTのclaimが無い場合でもIPが取れれば端末情報が記録される() {
		// 実行社員0(claim無し)の場合、Id_Loginが解決できないため申告値(Machine等)は「不明」になるが、
		// IPはサーバー由来のためJWTの有無に関係なく取れる。
		SetHttpContextUser(new ClaimsPrincipal(new ClaimsIdentity()));
		SetRemoteIpAddress("10.0.0.5");
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var history = FetchAllHistories().Single();
		Assert.IsTrue(history.Memo.Contains("実行社員Id=0"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("IP=10.0.0.5"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告Machine=不明"), $"Memo={history.Memo}");
	}

	[TestMethod]
	public async System.Threading.Tasks.Task Msg062_IPも申告値も取れない場合でも例外にならず不明で記録される() {
		// DefaultHttpContextはConnection.RemoteIpAddress未設定だとnullのままであり、
		// SysHistJwt行も無いため、すべて「不明」で埋まること。落ちないことの確認が主眼。
		SetLoggedInUser(loginId: 1, idShain: 111);
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var response = await InvokeAsync(CvFlag.Msg062_ManualLockClear);

		Assert.AreEqual(0, response.Code);
		var history = FetchAllHistories().Single();
		Assert.IsTrue(history.Memo.Contains("実行社員Id=111"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("IP=不明"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告Machine=不明"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告User=不明"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告OsVer=不明"), $"Memo={history.Memo}");
		Assert.IsTrue(history.Memo.Contains("申告MacAddress=不明"), $"Memo={history.Memo}");
	}
}
