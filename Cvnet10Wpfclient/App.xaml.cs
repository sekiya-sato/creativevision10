using CodeShare;
using Cvnet10Asset;
using Cvnet10Wpfclient.Helper;
using Cvnet10Wpfclient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProtoBuf.Grpc.ClientFactory;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;

namespace Cvnet10Wpfclient;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
	public static IHost? AppHost { get; private set; }
	public static ThemeService ThemeService { get; } = new();

	public App() {
		InitializeLanguage();
		AppHost = CreateHostBuilder().Build();
	}

	protected override async void OnStartup(StartupEventArgs e) {
		if (AppHost != null) {
			await StartHostAsync(AppHost);
		}
		base.OnStartup(e);
	}

	protected override async void OnExit(ExitEventArgs e) {
		if (AppHost != null) {
			await AppHost.StopAsync();
			AppHost.Dispose();
		}
		base.OnExit(e);
	}

	static void InitializeLanguage() {
		if (Thread.CurrentThread.CurrentCulture == null) {
			return;
		}
		var culture = Thread.CurrentThread.CurrentCulture;
		XmlLanguage language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
		FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
	}

	/// <summary>
	/// Caution: This Logic does not rewrite !
	/// サービスはここで追加、タイムアウト指定、認証ハンドラ追加などを行う
	/// </summary>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	static IHostBuilder CreateHostBuilder() {
		var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "_";
		return Host.CreateDefaultBuilder()
			.ConfigureAppConfiguration(builder => {
				builder.SetBasePath(Directory.GetCurrentDirectory());
				builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
				builder.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
				var userSettingsPath = SystemSettingsStore.SettingsFilePath;
				if (!string.IsNullOrWhiteSpace(userSettingsPath)) {
					var directory = Path.GetDirectoryName(userSettingsPath) ?? Directory.GetCurrentDirectory();
					builder.AddJsonFile(options => {
						options.Path = userSettingsPath;
						options.Optional = true;
						options.ReloadOnChange = true;
						options.FileProvider = new PhysicalFileProvider(directory);
					});
				}
			})
			.ConfigureServices((context, services) => {
				// 1. ハンドラーと通信設定の登録
				services.AddTransient<JwtAuthorizationHandler>();

				var url = context.Configuration.GetConnectionString("Url")
					?? throw new InvalidOperationException("Connection string 'Url' is missing.");
				var subPath = Common.ExtractSubPath(url);
				if(!string.IsNullOrEmpty(subPath))
					services.AddTransient<SubPathHandler>(_ => new SubPathHandler(subPath));

				services.AddSingleton<SocketsHttpHandler>(_ => new SocketsHttpHandler {
					PooledConnectionIdleTimeout = TimeSpan.FromHours(6),
					KeepAlivePingDelay = TimeSpan.FromSeconds(120),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(15), // タイムアウト時間
					KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always, // 通信がない時でもPingを送る
					EnableMultipleHttp2Connections = true
                });
				// 2. 統合されたクライアント構成ロジック
				void ConfigureClient<TService>(IServiceCollection srvs, string targetUrl, string path) where TService : class {
					var builder = srvs.AddCodeFirstGrpcClient<TService>((sp, options) => options.Address = new Uri(targetUrl))
						.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<SocketsHttpHandler>())
						.AddHttpMessageHandler<JwtAuthorizationHandler>();
					// サブパスが定義されている時だけパイプラインに追加
					if (!string.IsNullOrEmpty(path))
						builder.AddHttpMessageHandler<SubPathHandler>();
					builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
				}
				// 3. サービスの登録
				ConfigureClient<ILoginService>(services, url, subPath);
				ConfigureClient<ICvnetCoreService>(services, url, subPath);

            });
	}

	/// <summary>
	/// 設定変更を反映するためにホストを再構築します。
	/// </summary>
	public static async Task RestartHostAsync(CancellationToken cancellationToken = default) {
		if (AppHost is not null) {
			await AppHost.StopAsync(cancellationToken).ConfigureAwait(false);
			AppHost.Dispose();
		}
		AppHost = CreateHostBuilder().Build();
		await StartHostAsync(AppHost, cancellationToken).ConfigureAwait(false);
	}

	private static async Task StartHostAsync(IHost host, CancellationToken cancellationToken = default) {
		await host.StartAsync(cancellationToken).ConfigureAwait(false);
		InitializeAppCurrent(host);
	}

	private static void InitializeAppCurrent(IHost host) {
		var configuration = host.Services.GetRequiredService<IConfiguration>() as IConfigurationRoot
			?? throw new InvalidOperationException("IConfigurationRoot is not available.");
		AppCurrent.Init(configuration, host.Services);
	}
}


public class SubPathHandler : DelegatingHandler {
	private readonly string _subPath;

	public SubPathHandler(string subPath) {
		_subPath = "/" + subPath.Trim('/');
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		// デバッグ用: 実行されているか確認 (Visual Studioの出力ウィンドウに表示)
		//System.Diagnostics.Debug.WriteLine($"SubPathHandler: Original URI = {request.RequestUri}");

		var uri = request.RequestUri!;
		var builder = new UriBuilder(uri);
		var originalPath = uri.AbsolutePath;

		// パスの連結
		builder.Path = _subPath + originalPath;
		request.RequestUri = builder.Uri;

		//System.Diagnostics.Debug.WriteLine($"SubPathHandler: Rewritten URI = {request.RequestUri}");

		return await base.SendAsync(request, cancellationToken);
	}
}