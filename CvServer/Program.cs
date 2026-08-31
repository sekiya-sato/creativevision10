// ファイル概要:
// - CvServer のエントリーポイント。gRPC ホストを構成し、サービスと中間ウェアを登録します。
// - Kestrel 制限、圧縮、ForwardedHeaders などランタイム設定を集中管理します。
// 依存関係:
// - ASP.NET Core gRPC スタック、ProtoBuf.Grpc.Server、NLog。
// 変更ポリシー:
// - builder.Services への登録を変更する際は DI スコープや configure 順序に注意し、複数環境設定(appsettings)を整合させます。
// - ログや中間ウェアを追加する前にパフォーマンス/セキュリティへの影響を確認してください。
// COPILOT: 新しいサービスをマップする場合は .MapGrpcService<> とルートハンドラーを適切に配置し、ヘルスチェックやメトリクスの露出も検討すること。

using CvBase;
using CvBase.Share;
using CvServer;
using CvServer.Services;
using Grpc.Net.Compression;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using NLog.Web;
using ProtoBuf.Grpc.Server;
using System.IO.Compression;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddNLogWeb(); // Nlog側で Consoleログの出力をコントロール


builder.Services.AddCodeFirstGrpc((options => {
	// CompressionLevel は用途に応じて調整 (Fastest, Optimal 等)
	options.CompressionProviders.Add(new GzipCompressionProvider(CompressionLevel.Fastest));
	// サーバーから圧縮済みレスポンスを返す際に使うアルゴリズム名
	options.ResponseCompressionAlgorithm = "gzip";
	options.EnableDetailedErrors = true;
	options.MaxReceiveMessageSize = 1024 * 1024 * 1024; // 1 GB
	options.MaxSendMessageSize = 1024 * 1024 * 1024; // 1 GB
	options.Interceptors.Add<ErrorInterceptor>();
}));

builder.WebHost.ConfigureKestrel(serverOptions => {
	// Product: Kestrel デフォルトのオプションは必要に応じて追加する(2024/08/15)
	serverOptions.Limits.MaxRequestBodySize = 838_860_800; // 800 MB
	serverOptions.Limits.MaxConcurrentConnections = 100; // 最大同時接続数 [Maximum number of simultaneous connections]
	serverOptions.Limits.Http2.MaxStreamsPerConnection = 100; // 最大ストリーム数 [Maximum number of streams]
	serverOptions.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
	serverOptions.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20); // Timeout設定
});
builder.Services.AddHttpContextAccessor(); // HttpContextを取得可能にする [Make HttpContext accessible]

#region 認証関係の処理 ================================================== 
builder.Services.AddAuthorization(options => {
	options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy => {
		policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
		policy.RequireClaim(ClaimTypes.Name);
	});
});

builder.Services.AddAuthentication(options => { })
.AddScheme<AuthenticationSchemeOptions, CvServer.Handlers.CustomJwtAuthHandler>(JwtBearerDefaults.AuthenticationScheme, options => { });

// appsettings.json から設定を取得する [Retrieve settings from appsettings.json]
var jwtSettings = new JwtSettings(builder.Configuration);
builder.Services.AddSingleton(jwtSettings);
builder.Services.Configure<JwtBearerOptions>(options =>
	options.TokenValidationParameters = jwtSettings.CreateTokenValidationParameters());
#endregion

#region スケジューラの処理 ================================================== [Processing of the scheduler]
var schedulerSection = builder.Configuration.GetSection("NCrontab.Scheduler");
builder.Services.AddHostedScheduler(schedulerSection);
/* 
builder.Services.AddSingleton<IScheduledTask, NightlyTask>();
builder.Services.AddSingleton<IAsyncScheduledTask, NightlyAsyncTask>();

 * cronの書式 [Cron format]
* * * * *
| | | | +----- day of week (0 - 6) (Sunday=0)
| | | +------- month (1 - 12)
| | +--------- day of month (1 - 31)
| +----------- hour (0 - 23)
+------------- min (0 - 59)
分 時 日  月 曜日
例) 0 0 * * * = 毎日0時0分// 30 12 * * * 毎日12:30// 1,5 * * * * 毎時間1分と5分の2回//* *／6 * * * 6時間ごと
コントローラからタスクを追加する場合は以下のようにする [When adding a task from the controller, do it as follows]
https://github.com/thomasgalliker/NCrontab.Scheduler/blob/develop/Samples/NCrontab.Scheduler.AspNetCoreSample/Controllers/SchedulerDemoController.cs
 */
#endregion

/*
// Other(if need) : MCVコントローラの処理
builder.Services.AddControllers();
 */
var databaseProvider = builder.Configuration["Database:Provider"]?.Trim() ?? nameof(EnumSqlDialect.Sqlite);
var (connectionStringName, isSqlite) = databaseProvider.ToUpperInvariant() switch {
	"SQLITE" => ("sqlite", true),
	"POSTGRES" => ("postgres", false),
	"MARIADB" => ("mariadb", false),
	_ => throw new InvalidOperationException(
		$"Database:Provider '{databaseProvider}' is invalid. Use Sqlite, Postgre, or MariaDb.")
};
var connStr = builder.Configuration.GetConnectionString(connectionStringName)
	?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured.");
builder.Services.AddScoped<ExDatabase>(sp => {
	return databaseProvider.ToUpperInvariant() switch {
		"SQLITE" => CvBaseSqlite.ExDatabaseSqlite.GetDbConn(connStr),
		"POSTGRES" => CvBasePostgre.ExDatabasePostgre.GetDbConn(connStr),
		"MARIADB" => CvBaseMariadb.ExDatabaseMaria.GetDbConn(connStr),
		_ => throw new InvalidOperationException($"Database:Provider '{databaseProvider}' is invalid.")
	};
});
// SQL方言変換の動作モード。Auto=変換して未対応構文は警告 / Strict=未対応構文で例外 / Off=変換しない
// Off は障害時の退避用で、全プロバイダーが恒等変換に落ちる。
// 設計は `.omo/2026-08-25_sql_dialect_translator_detail_design.md` を参照する。
CvBase.Sql.SqlDialectOptions.Mode =
	CvBase.Sql.SqlDialectOptions.ParseMode(builder.Configuration["Database:SqlTranslation"]);
// ルール A04(PostgreSQL の ORDER BY へ NULLS FIRST を付ける) は既定で無効。
// ORDER BY 句へ手を入れる唯一のルールなので、3DB差分テストで必要性を確認してから有効化する。
CvBase.Sql.SqlDialectOptions.EnableNullsFirst =
	builder.Configuration.GetValue<bool>("Database:SqlRules:A04-NullsOrder");
builder.Services.AddSingleton(AppGlobal.Shared);
builder.Services.AddSingleton<SchedulerService>();
var serverVersion = builder.Configuration.GetSection("ServerVersion").Value ?? "0.0.0";
var app = builder.Build();
var logger = app.Logger;
var enableDetailedRequestLogging = builder.Configuration.GetValue<bool>("Diagnostics:EnableDetailedRequestLogging");
logger.LogDebug("Application Start ------------------------------------");
// 相関 ID を要求スコープへ設定し、詳細ログが有効なときだけ従来のヘッダ出力を行う。
app.Use(async (context, next) => {
	var logger = app.Logger;
	var correlationId = RequestCorrelation.Resolve(context);
	context.Response.Headers[RequestCorrelation.HeaderName] = correlationId;
	using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId })) {
		if (enableDetailedRequestLogging) {
			logger.LogInformation("Incoming request path: {Path}", context.Request.Path);
			foreach (var h in context.Request.Headers) {
				if (h.Key != "Authorization") {
					logger.LogInformation("REQ HDR: {Key} = {Value}", h.Key, h.Value.ToString());
				}
			}
		}
		await next();

		if (enableDetailedRequestLogging) {
			// レスポンスヘッダ（トレーラはここで見えない場合あり）
			foreach (var h in context.Response.Headers)
				logger.LogInformation("_RES HDR: {Key} = {Value}", h.Key, h.Value.ToString());
		}
	}
});

app.UseForwardedHeaders(new ForwardedHeadersOptions {
	ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
/*
// Other(if need) : MVCコントローラの処理
app.MapControllers();
 */
app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
app.MapGrpcService<LoginService>();
app.MapGrpcService<CoreService>();
app.MapGrpcService<PointOfSaleService>();
app.MapGrpcService<SchedulerService>();
app.MapGrpcService<SearchByPostalCodeService>();
app.MapGrpcService<WeatherService>();
var appInit = app.Services.GetRequiredService<AppGlobal>();
using (var scope = app.Services.CreateScope()) {
	var database = scope.ServiceProvider.GetRequiredService<ExDatabase>();
	// DIスコープから ExDatabase を取得してサーバ起動時に必要な初期化を実行
	await appInit.InitAsync(database, app.Environment.ApplicationName, serverVersion, app.Lifetime.ApplicationStopping);
	var isPdfReady = await appInit.PdfInitAsync(builder.Configuration.GetSection("PrintServer"));
	if (!isPdfReady)
		logger.LogWarning("PdfInitAsync Error");
	logger.LogDebug("appInit.Init() Server={ServerVersion}, Provider={DatabaseProvider}, DB={DatabaseVersion}",
		serverVersion, databaseProvider, database.Version);
	// DBのバージョン・必須機能・スキーマ前提(照合順序)を検証する。
	// SQLiteは現行運用を止めないため警告のみ、他DBは起動失敗にする
	var dialectIssues = database.Dialect.Validate(database.Version)
		.Concat(database.ValidateSchema())
		.ToList();
	if (dialectIssues.Count > 0) {
		var detail = string.Join(" / ", dialectIssues);
		if (isSqlite) {
			logger.LogWarning("DBバージョン検証 方言={Dialect} {Detail}", database.Dialect.Name, detail);
		}
		else {
			throw new InvalidOperationException($"DBバージョン検証に失敗しました。方言={database.Dialect.Name} {detail}");
		}
	}
	logger.LogInformation("SQL方言 方言={Dialect} モード={Mode}",
		database.Dialect.Name, CvBase.Sql.SqlDialectOptions.Mode);
}
var appStartTime = DateTime.Now;

app.Lifetime.ApplicationStarted.Register(() => {
	try {
		var schedulerService = app.Services.GetRequiredService<SchedulerService>();
		if (isSqlite)
			schedulerService.RegisterDailySqliteWalCheckpointTask();
		schedulerService.RegisterWorkFileCleanupTask();
		schedulerService.RegisterMonthlyResummaryTask();
		schedulerService.RegisterJodaiPurgeTask();
	}
	catch (Exception ex) {
		logger.LogError(ex, "スケジューラ定期実行登録中に例外が発生しました。");
	}
});

app.Lifetime.ApplicationStopping.Register(() => {
	RunShutdownStep("DB shutdown 処理", () => {
		using var scope = app.Services.CreateScope();
		var database = scope.ServiceProvider.GetRequiredService<ExDatabase>();

		if (isSqlite) {
			RunShutdownStep("SQLite shutdown checkpoint の実行", () => {
				var checkpointResult = database.RawExecCmd("PRAGMA wal_checkpoint(TRUNCATE);").FirstOrDefault();
				if (checkpointResult?.TryGetValue("Error", out var checkpointError) == true) {
					logger.LogWarning("SQLite shutdown checkpoint でエラーが返されました: {Error}", checkpointError);
				}
				else if (checkpointResult != null) {
					logger.LogInformation(
						"SQLite shutdown checkpoint が完了しました。 Busy={Busy}, Log={Log}, Checkpointed={Checkpointed}",
						checkpointResult.TryGetValue("busy", out var busy) ? busy : 0,
						checkpointResult.TryGetValue("log", out var log) ? log : 0,
						checkpointResult.TryGetValue("checkpointed", out var checkpointed) ? checkpointed : 0);
				}
			});
		}

		RunShutdownStep("DB 接続の shutdown close", database.Close);
	});
	if (isSqlite)
		RunShutdownStep("SQLite pool cleanup の shutdown 実行", () => CvBaseSqlite.ExDatabaseOption.ClearPools(connStr));
});

void RunShutdownStep(string operation, Action action) {
	try {
		action();
	}
	catch (Exception ex) {
		logger.LogWarning(ex, "{Operation} に失敗しました。", operation);
	}
}



app.MapGet("/", () =>
$"""
CvServer Ver.{serverVersion} is running. ({appStartTime} - {DateTime.Now})
Communication with gRPC endpoints must be made through a gRPC client. 

"""
);

// 公開するディレクトリとリクエストパスの定義
(string Directory, string RequestPath)[] staticPaths = [
	("wrk", "/wrk"),
	("img", "/img")
];

// 共通の準備処理（ディレクトリ作成とStaticFileOptionsの生成）
foreach (var pathInfo in staticPaths) {
	var fullPath = Path.Combine(Directory.GetCurrentDirectory(), pathInfo.Directory);
	if (!Directory.Exists(fullPath)) {
		Directory.CreateDirectory(fullPath);
	}
	app.UseStaticFiles(new StaticFileOptions {
		FileProvider = new PhysicalFileProvider(fullPath),
		RequestPath = pathInfo.RequestPath,
		OnPrepareResponse = ctx => {
			// セキュリティ・キャッシュ制御ヘッダーの共通設定
			var headers = ctx.Context.Response.Headers;
			headers.CacheControl = "no-cache, no-store, must-revalidate";
			headers.Pragma = "no-cache";
			headers.Expires = "0";
		}
	});
}

try {
	app.Run();
}
catch (Exception ex) {
	// 起動失敗時のログ記録
	new NLogExtender<Program>().LogCritical(ex, "Stopped program because of exception");
	throw;
}
finally {
	// 全ての非同期ログをフラッシュし、リソースを解放する
	var log = new NLogExtender<Program>();
	log.LogInformation("Application is shutting down...");
	log.Shutdown();
}
