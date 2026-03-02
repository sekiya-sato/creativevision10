using CodeShare;
using Cvnet10Base.Share;
using Cvnet10Base.Sqlite;
using Cvnet10Server;
using Cvnet10Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tests.Cvnet10Server;

public class FakeWebHostEnvironment : IWebHostEnvironment {
	public string ApplicationName { get; set; } = "Cvnet10Tests";
	public string EnvironmentName { get; set; } = "Development";
	public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	public string WebRootPath { get; set; } = AppContext.BaseDirectory;
	public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

[TestClass]
public class CvnetCoreServiceTests {
	private ExDatabaseSqlite? _db;
	private CvnetCoreService? _service;

	[TestInitialize]
	public void Initialize() {
		// In-memory SQLite を準備
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);

		// 必要な依存をダミーで作成
		var logger = NullLogger<CvnetCoreService>.Instance;
		var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
		var env = new FakeWebHostEnvironment();
		var httpAccessor = new HttpContextAccessor();
		var appInit = new AppInit(config);
		appInit.Init(_db);
		// サービスを作成
		_service = new CvnetCoreService(logger, config, env, httpAccessor, _db);
	}

	[TestCleanup]
	public void Cleanup() {
		try {
			_db?.Close();
			(_db?.Connection as SqliteConnection)?.Close();
		}
		catch { }
	}

	[TestMethod]
	public async Task CopyReply_ReturnsSamePayload() {
		var request = new CvnetMsg {
			Flag = CvnetFlag.Msg001_CopyReply,
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
		var request = new CvnetMsg {
			Flag = CvnetFlag.Msg002_GetVersion,
		};
		if (_service == null) {
			Assert.Fail("Service not initialized");
			return;
		}

		var result = await _service.QueryMsgAsync(request);

		Assert.IsNotNull(result);
		Assert.AreEqual(0, result.Code);
		Assert.AreEqual(request.Flag, result.Flag);
		Assert.AreEqual(typeof(VersionInfo), result.DataType);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.DataMsg ?? ""));
		// JSON 解析は不要だが、空でないことを確認
	}

	[TestMethod]
	public async Task GetEnv_ReturnsDictionarySerialized() {
		var request = new CvnetMsg {
			Flag = CvnetFlag.Msg003_GetEnv,
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
}
