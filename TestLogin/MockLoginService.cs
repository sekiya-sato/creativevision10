using Cvnet10Base;
using Cvnet10DomainLogic;
using Cvnet10Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace TestLogin;

sealed class LoginServiceTestContext : IDisposable {
	readonly SqliteConnection _connection;
	public ExDatabase Database { get; }
	public IConfiguration Configuration { get; }
	public IHttpContextAccessor HttpContextAccessor { get; }
	/// <summary>
	/// テスト用の LoginService インスタンス。
	/// </summary>
	public LoginService Service { get; }

	public LoginServiceTestContext() {
		_connection = new SqliteConnection("DataSource=:memory:");
		_connection.Open();
		Database = new ExDatabase(_connection);
		InitializeSchema();
		Configuration = BuildConfiguration();
		HttpContextAccessor = CreateAccessor();
		Service = new LoginService(NullLogger<CvnetCoreService>.Instance, BuildConfiguration(), new TestWebHostEnvironment(), CreateAccessor(), Database);
	}
	/// <summary>
	/// IWebHostEnvironment を模擬するためのクラス。
	/// </summary>
	sealed class TestWebHostEnvironment : IWebHostEnvironment {
		public string ApplicationName { get; set; } = "TestHost";
		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
		public string WebRootPath { get; set; } = ".";
		public string EnvironmentName { get; set; } = "Development";
		public string ContentRootPath { get; set; } = ".";
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}
	/// <summary>
	/// IConfiguration を構築します。
	/// </summary>
	/// <returns></returns>
	static IConfiguration BuildConfiguration() {
		var settings = new Dictionary<string, string?> {
			["WebAuthJwt:Issuer"] = "UnitTestIssuer",
			["WebAuthJwt:Lifetime"] = "60",
			["WebAuthJwt:Refreshtime"] = "30",
			["WebAuthJwt:SecretKey"] = "unit-test-secret-key-0123456789-abcdef-xyz",
		};
		return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
	}
	/// <summary>
	/// HttpContextAccessor を作成します。
	/// </summary>
	/// <returns></returns>

	static IHttpContextAccessor CreateAccessor() {
		var httpContext = new DefaultHttpContext();
		httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
		return new HttpContextAccessor { HttpContext = httpContext };
	}

	void InitializeSchema() {
		Database.CreateTable<SysLogin>();
		Database.CreateTable<SysHistJwt>();
	}

	public void Dispose() {
		Database.Dispose();
		_connection.Dispose();
	}
}



