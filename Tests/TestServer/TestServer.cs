using CodeShare;
using CvBase.Share;
using CvBaseSqlite;
using CvServer;
using CvServer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
public class CvnetCoreServiceTests {
	private ExDatabaseSqlite? _db;
	private CoreService? _service;
	private NCrontab.Scheduler.Scheduler? _scheduler;
	private SchedulerService? _schedulerService;

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
		appInit.Init(_db);
		// サービスを作成
		_service = new CoreService(logger, config, env, httpAccessor, _db);
		_scheduler = new NCrontab.Scheduler.Scheduler(NullLogger<NCrontab.Scheduler.Scheduler>.Instance);
		_schedulerService = new SchedulerService(schedulerLogger, _scheduler, _db, config, env);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_scheduler?.Dispose();
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
		try {
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute("CREATE TABLE IF NOT EXISTS Sample(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
			db.Execute("INSERT INTO Sample(Name) VALUES (@0);", "checkpoint-test");

			var result = SchedulerService.ExecuteSqliteWalCheckpoint(db);
			Assert.IsNotNull(result);
			Assert.IsTrue(result.ContainsKey("busy"));
			Assert.IsTrue(result.ContainsKey("log"));
			Assert.IsTrue(result.ContainsKey("checkpointed"));
		}
		finally {
			if (File.Exists(dbPath)) {
				File.Delete(dbPath);
			}
			if (File.Exists(dbPath + "-wal")) {
				File.Delete(dbPath + "-wal");
			}
			if (File.Exists(dbPath + "-shm")) {
				File.Delete(dbPath + "-shm");
			}
		}
	}
}
