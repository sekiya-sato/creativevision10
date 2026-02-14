using CodeShare;
using Cvnet10Asset;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.Services;
using Cvnet10Wpfclient.ViewServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using ProtoBuf.Grpc.ClientFactory;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Cvnet10Wpfclient;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
	public static IHost? AppHost { get; private set; }
	public static ThemeService ThemeService { get; } = new();
	private static readonly Logger AppLogger = LogManager.GetCurrentClassLogger();

	public App() {
		InitializeLanguage();
		RegisterGlobalExceptionHandlers();
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
		// 現在のスレッドのカルチャを取得し、WPFの言語設定に適用
		var culture = Thread.CurrentThread.CurrentCulture;
		XmlLanguage language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
		FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
	}

	private void RegisterGlobalExceptionHandlers() {
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
		HandleUnhandledException(e.Exception, "DispatcherUnhandledException");
		e.Handled = true;
	}

	private static void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
		HandleUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
		e.SetObserved();
	}

	private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
		if (e.ExceptionObject is Exception exception) {
			HandleUnhandledException(exception, "AppDomain.UnhandledException");
			return;
		}

		AppLogger.Error("Unhandled exception (AppDomain.UnhandledException): {ExceptionObject}", e.ExceptionObject);
		ShowUnhandledExceptionMessage(new Exception("予期しないエラーが発生しました。"));
	}

	private static void HandleUnhandledException(Exception exception, string source) {
		AppLogger.Error(exception, "Unhandled exception: {Source}", source);
		ShowUnhandledExceptionMessage(exception);
	}

	private static void ShowUnhandledExceptionMessage(Exception exception) {
		var dispatcher = Current?.Dispatcher;
		if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
			return;
		}

		var message = $"予期しないエラーが発生しました。\n\n{exception.Message}";
//		void Show() => MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
		void Show() => MessageEx.ShowErrorDialog(message,appendedMessage:exception.StackTrace??"" ,
			owner: Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive));


		if (dispatcher.CheckAccess()) {
			Show();
		} else {
			dispatcher.Invoke(Show);
		}
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
				// 各設定ファイルの読み込み
				builder.SetBasePath(Directory.GetCurrentDirectory());
				builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
				builder.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
				// ユーザー設定ファイルの追加 (最後に適用したほうが優先順位が高い)
				var userSettingsPath = SystemSettingsStore.SettingsFilePath;
				if (!string.IsNullOrWhiteSpace(userSettingsPath)) {
					var directory = Path.GetDirectoryName(userSettingsPath);
					var fileName = Path.GetFileName(userSettingsPath);
					if (!string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName)) {
						builder.AddJsonFile(options => {
							options.Path = fileName;
							options.Optional = true;
							options.ReloadOnChange = true;
							options.FileProvider = new PhysicalFileProvider(directory);
						});
					}
				}
			})
			.ConfigureLogging((context, logging) => {
				logging.ClearProviders(); // 既定のログプロバイダーをクリア
				logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
				// これにより appsettings.json の "NLog" セクションが読み込まれます
				LogManager.Configuration = new NLogLoggingConfiguration(context.Configuration.GetSection("NLog"));
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

		AppGlobal.Init(configuration, host.Services);
	}
}

