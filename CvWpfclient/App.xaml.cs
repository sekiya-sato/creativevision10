using CodeShare;
using CvAsset;
using CvWpfclient.Helpers;
using CvWpfclient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using ProtoBuf.Grpc.ClientFactory;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Velopack;

namespace CvWpfclient;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
	public static IHost? AppHost { get; private set; }
	public static ThemeService ThemeService { get; } = new();
	private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

	[STAThread]
	public static void Main(string[] args) {
		VelopackApp.Build().Run();

		var app = new App();
		app.InitializeComponent();
		app.Run();
	}

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
		_ = CheckForUpdatesOnStartupAsync();
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
		var language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
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

		_logger.Error("Unhandled exception (AppDomain.UnhandledException): {ExceptionObject}", e.ExceptionObject);
		ShowUnhandledExceptionMessage(new Exception("予期しないエラーが発生しました。"));
	}

	private static void HandleUnhandledException(Exception exception, string source) {
		_logger.Error(exception, "Unhandled exception: {Source}", source);
		ShowUnhandledExceptionMessage(exception);
	}

	private static void ShowUnhandledExceptionMessage(Exception exception) {
		var dispatcher = Current?.Dispatcher;
		if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
			return;
		}

		var message = $"予期しないエラーが発生しました。\n\n{exception.Message}";
		//		void Show() => MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
		void Show() => MessageEx.ShowErrorDialog(message, appendedMessage: exception.StackTrace ?? "",
			owner: Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive));


		if (dispatcher.CheckAccess()) {
			Show();
		}
		else {
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
				builder.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true);
				builder.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
				builder.AddJsonFile(SystemSettingsStore.SettingsFilePath, optional: true, reloadOnChange: true);
			})
		.ConfigureLogging((context, logging) => {
			logging.ClearProviders(); // 既定のログプロバイダーをクリア
			logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
			logging.AddNLog(context.Configuration); // ILogger<T> → NLog へルーティング
		})
			.ConfigureServices((context, services) => {
				// 1. ハンドラーと通信設定の登録
				services.AddTransient<JwtAuthorizationHandler>();

				var url = context.Configuration.GetConnectionString("Url")
					?? throw new InvalidOperationException("Connection string 'Url' is missing.");
				var subPath = Common.ExtractSubPath(url);
				if (!string.IsNullOrEmpty(subPath))
					services.AddTransient<GrpcSubPathHandler>(_ => new GrpcSubPathHandler(subPath));

				services.AddSingleton<SocketsHttpHandler>(_ => new SocketsHttpHandler {
					PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
					KeepAlivePingDelay = TimeSpan.FromSeconds(60), // サーバーの設定より長くするのが一般的
					KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
					EnableMultipleHttp2Connections = true,
					KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always, // 通信がない時でもPingを送る
				});
				// 2. 統合されたクライアント構成ロジック
				void ConfigureClient<TService>(IServiceCollection srvs, string targetUrl, string path) where TService : class {
					var builder = srvs.AddCodeFirstGrpcClient<TService>((sp, options) => options.Address = new Uri(targetUrl))
						.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<SocketsHttpHandler>())
						.AddHttpMessageHandler<JwtAuthorizationHandler>();
					// サブパスが定義されている時だけパイプラインに追加
					if (!string.IsNullOrEmpty(path))
						builder.AddHttpMessageHandler<GrpcSubPathHandler>();
					builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
				}

				void ConfigureJapanPostBizClient(IServiceProvider serviceProvider, HttpClient client) {
					var options = context.Configuration.GetSection("JapanPostBiz").Get<JapanPostBizOptions>() ?? new();
					client.BaseAddress = new Uri(options.BaseUrl);
					client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
					client.DefaultRequestHeaders.UserAgent.Clear();
					client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
				}
				// 3. サービスの登録
				services.AddSingleton<IUpdateService, UpdateService>();
				services.AddHttpClient<IWeatherService, WeatherService>();
				services.AddHttpClient<IJapanPostBizTokenProvider, JapanPostBizTokenProvider>(ConfigureJapanPostBizClient);
				services.AddHttpClient<IPostalAddressService, JapanPostBizPostalAddressService>(ConfigureJapanPostBizClient);
					ConfigureClient<ILoginService>(services, url, subPath);
				ConfigureClient<ICvnetCoreService>(services, url, subPath);
			});
	}

	private async Task CheckForUpdatesOnStartupAsync() {
		try {
			if (AppHost == null) {
				return;
			}

			await Task.Delay(1500);
			var updateService = AppHost.Services.GetService<IUpdateService>();
			if (updateService == null) {
				return;
			}

			var checkResult = await updateService.CheckForUpdateAsync().ConfigureAwait(false);
			if (!checkResult.IsUpdateAvailable) {
				_logger.Info("起動時更新確認: {Message}", checkResult.Message);
				return;
			}

			var owner = await Dispatcher.InvokeAsync(() =>
				Current?.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive) ?? Current?.MainWindow);
			var answer = await Dispatcher.InvokeAsync(() =>
				MessageEx.ShowQuestionDialog($"{checkResult.Message}\n\n今すぐ更新しますか？", owner: owner));

			if (answer != MessageBoxResult.Yes) {
				_logger.Info("起動時更新確認: 更新を見つけましたが、ユーザーが延期しました。");
				return;
			}

			var executeResult = await updateService.PerformUpdateAsync().ConfigureAwait(false);
			if (!executeResult.IsSuccess) {
				await Dispatcher.InvokeAsync(() => MessageEx.ShowErrorDialog(executeResult.Message, owner: owner));
			}
		}
		catch (Exception ex) {
			_logger.Error(ex, "起動時の更新確認でエラーが発生しました。");
		}
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

