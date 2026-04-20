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
using CvServer;
using CvServer.Services;
using Grpc.Net.Compression;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using NLog.Web;
using ProtoBuf.Grpc.Server;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddNLogWeb(); // Nlog側で Consoleログの出力をコントロール


builder.Services.AddCodeFirstGrpc((options => {
	// CompressionLevel は用途に応じて調整 (Fastest, Optimal 等)
	options.CompressionProviders.Add(new GzipCompressionProvider(CompressionLevel.Fastest));
	// サーバーから圧縮済みレスポンスを返す際に使うアルゴリズム名
	options.ResponseCompressionAlgorithm = "gzip";
	options.EnableDetailedErrors = true;
	options.MaxReceiveMessageSize = 800 * 1024 * 1024; // 800 MB
	options.MaxSendMessageSize = 800 * 1024 * 1024; // 800 MB
	options.Interceptors.Add<ErrorInterceptor>();
}));

builder.WebHost.ConfigureKestrel(serverOptions => {
	// TODO: Kestrel デフォルトのオプションは必要に応じて追加する(2024/08/15)
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
if (builder.Configuration.GetSection("WebAuthJwt") != null) {
	var seckey = builder.Configuration.GetSection("WebAuthJwt")?.GetSection("SecretKey")?.Value ?? "veryveryhardsecurity-keys.needtoolong";
	builder.Services.Configure<JwtBearerOptions>(options => {
		options.TokenValidationParameters = new TokenValidationParameters {
			ValidateIssuer = true,
			ValidIssuer = builder.Configuration.GetSection("WebAuthJwt").GetSection("Issuer").Value, // トークン発行者 [Token issuer]
			ValidateAudience = false,
			ValidAudience = builder.Configuration.GetSection("WebAuthJwt").GetSection("Audience").Value, // トークンの受信者(検証しない) [Token recipient (not validated)]
			ValidateLifetime = true,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(seckey)),  // トークンの署名を検証するためのキー 16バイト以上 [Key to verify token signature, 16 bytes or more]
			ValidateIssuerSigningKey = true,
			ClockSkew = TimeSpan.Zero // WTのLifeTime検証の際の時間のずれを設定するという謎プロパティで、デフォルトは 5分 
									  // [A mysterious property that sets the time difference during WT Lifetime validation, with a default of 5 minutes]
		};
	});
}
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
var connStr = builder.Configuration.GetConnectionString("sqlite")
	?? throw new InvalidOperationException("Connection string 'sqlite' is not configured.");
builder.Services.AddSingleton<ExDatabase>(sp => {
	// ファクトリメソッドを使用してインスタンスを生成
	return CvBaseSqlite.ExDatabaseSqlite.GetDbConn(connStr);
});
var serverVersion = builder.Configuration.GetSection("ServerVersion").Value ?? "0.0.0";
var app = builder.Build();
var logger = app.Logger;
logger.LogDebug("Application Start ------------------------------------");
// リクエスト／レスポンスヘッダをログするミドルウェア
app.Use(async (context, next) => {
	var logger = app.Logger;
	logger.LogInformation("Incoming request path: {Path}", context.Request.Path);
	foreach (var h in context.Request.Headers)
		logger.LogInformation("REQ HDR: {Key} = {Value}", h.Key, h.Value.ToString());

	await next();

	// レスポンスヘッダ（トレーラはここで見えない場合あり）
	foreach (var h in context.Response.Headers)
		logger.LogInformation("RES HDR: {Key} = {Value}", h.Key, h.Value.ToString());
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
app.MapGrpcService<SchedulerService>();
app.MapGrpcService<SearchByPostalCodeService>();
app.MapGrpcService<WeatherService>();
var appInit = new AppGlobal();
// DIコンテナから登録済みの ExDatabase を取得してサーバ起動時に必要な初期化を実行
appInit.Init(app.Services.GetRequiredService<ExDatabase>(), app.Environment.ApplicationName, serverVersion);
var appStartTime = DateTime.Now;

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
	NLog.LogManager.GetCurrentClassLogger().Fatal(ex, "Stopped program because of exception");
	throw;
}
finally {
	// 全ての非同期ログをフラッシュし、リソースを解放する
	NLog.LogManager.Shutdown();
}
