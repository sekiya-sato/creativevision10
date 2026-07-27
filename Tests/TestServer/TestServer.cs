using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvServer;
using CvServer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Tests.CvServer;

public class FakeWebHostEnvironment : IWebHostEnvironment {
	public string ApplicationName { get; set; } = "CvTests";
	public string EnvironmentName { get; set; } = "Development";
	public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	public string WebRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

[TestClass]
public class CoreServiceTests {
	private ExDatabaseSqlite? _db;
	private CoreService? _service;
	private NCrontab.Scheduler.Scheduler? _scheduler;
	private SchedulerService? _schedulerService;
	private ServiceProvider? _serviceProvider;

	[TestInitialize]
	public void Initialize() {
		// In-memory SQLite を準備
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);

		// 必要な依存をダミーで作成
		var logger = NullLogger<CoreService>.Instance;
		var schedulerLogger = NullLogger<SchedulerService>.Instance;
		var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
		var env = new FakeWebHostEnvironment();
		var httpAccessor = new HttpContextAccessor();
		var appInit = new AppGlobal();
		appInit.InitAsync(_db).Wait();
		// サービスを作成
		_service = new CoreService(logger, config, env, httpAccessor, _db);
		_scheduler = new NCrontab.Scheduler.Scheduler(NullLogger<NCrontab.Scheduler.Scheduler>.Instance);
		_serviceProvider = new ServiceCollection()
			.AddSingleton<ExDatabase>(_db)
			.BuildServiceProvider();
		_schedulerService = new SchedulerService(schedulerLogger, _scheduler, _serviceProvider.GetRequiredService<IServiceScopeFactory>(), config, env);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_scheduler?.Dispose();
		_serviceProvider?.Dispose();
	}

	[TestMethod]
	public async Task CopyReply_ReturnsSamePayload() {
		var request = new CvMsg {
			Flag = CvFlag.Msg001_CopyReply,
			Code = 0,
			DataType = typeof(string),
			DataMsg = "hello-copy"
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}
		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(request.DataType, result.DataType);
		Assert.AreEqual(request.DataMsg, result.DataMsg);
	}

	[TestMethod]
	public async Task GetVersion_ReturnsVersionInfoSerialized() {
		var request = new CvMsg {
			Flag = CvFlag.Msg002_GetVersion,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(InfoServer), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
		// JSON 解析は不要だが、空でないことを確認
	}

	[TestMethod]
	public async Task GetEnv_ReturnsDictionarySerialized() {
		var request = new CvMsg {
			Flag = CvFlag.Msg003_GetEnv,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(Dictionary<string, string>), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
	}

	/// <summary>
	/// Phase3: HandleUpdate のV*列伝播フックが gRPC 経路で実際に動作することを確認する
	/// (名称マスタの改名 → 参照している商品マスタの VBrand が現行名称になる)
	/// </summary>
	[TestMethod]
	public async Task Update_MasterMeishoRename_CascadesToReferencingMasterVColumn() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		db.CreateTable(typeof(MasterMeisho), true, false);
		db.CreateTable(typeof(MasterShohin), true, false);

		var brand = new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		db.Insert(brand);
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "サンプル商品",
			Id_Brand = brand.Id,
			VBrand = new CodeNameView { Sid = brand.Id, Cd = "01", Mei = "旧ブランド" },
			Vdc = 1,
			Vdu = 1,
		};
		db.Insert(shohin);

		// クライアントと同じ手順: DBから取得 → Name変更 → UpdateParam で送信
		var edit = db.SingleById<MasterMeisho>(brand.Id);
		edit.Name = "新ブランド";
		var request = new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			Code = 0,
			DataType = typeof(UpdateParam),
			DataMsg = Common.SerializeObject(new UpdateParam(typeof(MasterMeisho), Common.SerializeObject(edit)))
		};

		var result = await service.QueryMsgAsync(request);

		Assert.AreEqual(0, result.Code, $"更新が成功する: {result.Option} {result.DataMsg}");
		Assert.AreEqual("新ブランド", db.SingleById<MasterMeisho>(brand.Id).Name, "名称マスタ自体が更新される");
		var after = db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("新ブランド", after.VBrand.Mei, "商品マスタのVBrandへ伝播している");
		Assert.AreEqual("01", after.VBrand.Cd, "VBrand.Cd");
		Assert.AreEqual(brand.Id, after.VBrand.Sid, "VBrand.Sid");

		// 名称以外の変更では伝播しない(無駄なUPDATEを流さない)
		var edit2 = db.SingleById<MasterMeisho>(brand.Id);
		var vduBefore = after.Vdu;
		edit2.Ryaku = "略称のみ変更";
		var request2 = new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			Code = 0,
			DataType = typeof(UpdateParam),
			DataMsg = Common.SerializeObject(new UpdateParam(typeof(MasterMeisho), Common.SerializeObject(edit2)))
		};
		var result2 = await service.QueryMsgAsync(request2);

		Assert.AreEqual(0, result2.Code, "略称のみの更新も成功する");
		Assert.AreEqual(vduBefore, db.SingleById<MasterShohin>(shohin.Id).Vdu, "Code/Name以外の変更では参照側のVduが動かない");
	}

	/// <summary>
	/// Phase5: Msg047 でV*列とJSON内スナップショットが現在のマスタ内容へ再同期される
	/// </summary>
	[TestMethod]
	public async Task MasterVColumnResync_SyncsStaleSnapshotsAndIsIdempotent() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		var service = _service ?? throw new AssertFailedException("Service not initialized");
		foreach (var t in new[] { typeof(MasterMeisho), typeof(MasterShohin), typeof(MasterTokui), typeof(DerivedShohinColSiz) }) {
			db.CreateTable(t, true, false);
		}

		var brand = new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "現ブランド", Vdc = 1, Vdu = 1 };
		db.Insert(brand);
		var soko = new MasterTokui { Code = "9001", Name = "現倉庫", TenType = 0, Vdc = 1, Vdu = 1 };
		db.Insert(soko);
		// V*列とJSON内スナップショットが古い/空の状態を作る
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "商品",
			Id_Brand = brand.Id,
			VBrand = new CodeNameView { Sid = brand.Id, Cd = "01", Mei = "旧ブランド" },
			Id_Soko = soko.Id,
			Jsub = [new MasterGeneralMeisho { Kb = "B01", Kbname = "区分", Sid = brand.Id, Cd = "01", Mei = "旧ブランド" }],
			Vdc = 1,
			Vdu = 1,
		};
		db.Insert(shohin);
		db.Execute("update MasterShohin set VSoko = ''");

		var request = new CvMsg { Flag = CvFlag.Msg047_MasterVColumnResync, Code = 0, DataType = typeof(string), DataMsg = string.Empty };
		var result = await service.QueryMsgAsync(request);

		Assert.AreEqual(0, result.Code, $"再同期が成功する: {result.Option} {result.DataMsg}");
		// 実行結果メッセージに開始/終了/所要時間が含まれること
		StringAssert.Contains(result.DataMsg, "開始 ", "開始時刻が含まれる");
		StringAssert.Contains(result.DataMsg, "終了 ", "終了時刻が含まれる");
		StringAssert.Contains(result.DataMsg, "所要 ", "所要時間が含まれる");
		var after = db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("現ブランド", after.VBrand.Mei, "VBrandが現行名称になる");
		Assert.AreEqual("現倉庫", after.VSoko.Mei, "空だったVSokoが埋まる");
		Assert.AreEqual(soko.Id, after.VSoko.Sid, "VSoko.Sid");
		Assert.AreEqual("現ブランド", after.Jsub![0].Mei, "Jsub内のMeiも現行名称になる");

		// 2回目は更新0件(冪等)
		var result2 = await service.QueryMsgAsync(request);
		Assert.AreEqual(0, result2.Code, "2回目も成功する");
		StringAssert.StartsWith(result2.DataMsg, "更新行数=0", "2回目は更新0件");
	}

	[TestMethod]
	public void RegisterDailySqliteWalCheckpointTask_RegistersTwoAmSchedule() {
		var schedulerService = _schedulerService ?? throw new AssertFailedException("SchedulerService not initialized");

		var result = schedulerService.RegisterDailySqliteWalCheckpointTask();
		var taskId = Guid.Parse(result.TaskId);
		var scheduledTask = _scheduler!.GetTaskById(taskId);

		Assert.AreEqual(0, result.Result);
		Assert.IsNotNull(scheduledTask);

		var next = scheduledTask!.CrontabSchedule.GetNextOccurrence(new DateTime(2026, 5, 23, 1, 0, 0));
		Assert.AreEqual(new DateTime(2026, 5, 23, 2, 0, 0), next);
	}

	[TestMethod]
	public void ExecuteSqliteWalCheckpoint_ReturnsCheckpointRow() {
		var dbPath = Path.Combine(Path.GetTempPath(), $"sqlite-wal-checkpoint-{Guid.NewGuid():N}.db");
		ExDatabaseSqlite? db = null;
		try {
			db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute("CREATE TABLE IF NOT EXISTS Sample(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
			db.Execute("INSERT INTO Sample(Name) VALUES (@0);", "checkpoint-test");

			var result = SchedulerService.ExecuteSqliteWalCheckpoint(db);
			Assert.IsNotNull(result);
			Assert.IsTrue(result.ContainsKey("busy"));
			Assert.IsTrue(result.ContainsKey("log"));
			Assert.IsTrue(result.ContainsKey("checkpointed"));
		}
		finally {
			db?.Close();
			db?.Dispose();
			(db?.Connection as SqliteConnection)?.Close();
			SqliteConnection.ClearAllPools();
			DeleteFileWithRetry(dbPath);
			DeleteFileWithRetry(dbPath + "-wal");
			DeleteFileWithRetry(dbPath + "-shm");
		}
	}

	[TestMethod]
	public void ExDatabaseOptionClearPools_RemovesWalSidecarFiles() {
		var dbPath = Path.Combine(Path.GetTempPath(), $"sqlite-clear-pools-{Guid.NewGuid():N}.db");
		ExDatabaseSqlite? db = null;
		try {
			db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute("CREATE TABLE IF NOT EXISTS Sample(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
			db.Execute("INSERT INTO Sample(Name) VALUES (@0);", "clear-pools-test");

			Assert.IsTrue(File.Exists(dbPath));
			Assert.IsTrue(
				File.Exists(dbPath + "-wal") || File.Exists(dbPath + "-shm"),
				"WAL sidecar file was not created before cleanup.");

			db.Close();
			db.Dispose();
			ExDatabaseOption.ClearPools(dbPath);

			Assert.IsFalse(File.Exists(dbPath + "-wal"));
			Assert.IsFalse(File.Exists(dbPath + "-shm"));
		}
		finally {
			db?.Close();
			db?.Dispose();
			ExDatabaseOption.ClearPools(dbPath);
			DeleteFileWithRetry(dbPath);
		}
	}

	static void DeleteFileWithRetry(string path) {
		for (int i = 0; i < 5; i++) {
			if (!File.Exists(path)) {
				return;
			}

			try {
				File.Delete(path);
				return;
			}
			catch (IOException) when (i < 4) {
				SqliteConnection.ClearAllPools();
				System.Threading.Thread.Sleep(100);
			}
		}

		if (File.Exists(path)) {
			File.Delete(path);
		}
	}
}
