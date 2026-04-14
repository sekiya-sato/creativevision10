using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace CvWpfclient.ViewModels;

public partial class MainMenuViewModel : ObservableObject {
	private readonly ILogger<MainMenuViewModel> _logger;

	[ObservableProperty]
	ObservableCollection<MenuData> menuItems = [];

	[ObservableProperty]
	private MenuData? selectedMenu;

	[ObservableProperty]
	private string? selectedMenuParentHeader;

	partial void OnSelectedMenuChanged(MenuData? value) {
		SelectedMenuParentHeader = FindParentHeader(MenuItems, value);
	}

	[ObservableProperty]
	private string? statusMessage;

	[ObservableProperty]
	private string? expireDate;

	[ObservableProperty]
	private string headerTitle = "Creative Vision 10";


	private DateTime _subStartTime = DateTime.Now;
	[ObservableProperty]
	private string subTitle = ".net10, gRPC, HTTP/2.0 Model";

	[ObservableProperty]
	private bool isMenuReady;

	[ObservableProperty]
	private string? currentDate; // yy/MM/dd 用

	[ObservableProperty]
	private string? currentTime; // HH:mm:ss 用

	[ObservableProperty]
	private string? kyureki; // 旧暦表示用

	private DispatcherTimer? _timer;

	private DateTime checkDate = DateTime.MinValue;

	private System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo("ja-JP");

	[ObservableProperty]
	private string serverStatus = string.Empty;

	[ObservableProperty]
	private string clientStatus = string.Empty;


	[ObservableProperty]
	InfoUser infolocalUser = new InfoUser();
	[ObservableProperty]
	InfoServer infolocalServer = new InfoServer();

	public MainMenuViewModel() {
		_logger = App.AppHost!.Services.GetRequiredService<ILoggerFactory>().CreateLogger<MainMenuViewModel>();
	}

	partial void OnInfolocalUserChanged(InfoUser value) {
		AppGlobal.StaticInfoUser = value;
		// ここに追加処理を書く
	}
	partial void OnInfolocalServerChanged(InfoServer value) {
		AppGlobal.StaticInfoServer = value;
	}
	[RelayCommand]
	private void Init() {
		if (IsMenuReady) {
			return;
		}

		MenuItems = MenuData.CreateDefault();
		IsMenuReady = true;
		var window = ClientLib.GetActiveView(this);
		if (window != null) {
			startRect = window.RestoreBounds;
			miniRect = new Rect() {
				X = startRect.X + startRect.Width - 280,
				Y = startRect.Y,
				Width = 280,
				Height = 700
			};
		}
		StartClock();
		StartWeatherAndCalendar();
		ExpireDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
		Kyureki = $"旧暦 {DateTime.Now.ToSimpleLunisolarStr()}";

		InfolocalUser.OsVer = Environment.OSVersion.ToString();
		InfolocalUser.DotnetVer = Environment.Version.ToString();
		InfolocalUser.ComputerName = Environment.MachineName;
		InfolocalUser.UserName = Environment.UserName;
		InfolocalServer.Url = AppGlobal.Config["ConnectionStrings:Url"] ?? "";
		ClientStatus = $"アプリ開始時間 {_subStartTime.ToString("yyyy/MM/dd HH:mm")}\n{InfolocalUser.OsVer ?? "OS-version"}\nDOTNET {InfolocalUser.DotnetVer ?? "DOTNET-Version"}\nローカル名 {InfolocalUser.ComputerName} {InfolocalUser.UserName}\nLogin時間 {InfolocalUser.LoginTime ?? "??:??:??"}\nExpire時間 {InfolocalUser.ExpireTime ?? "??:??:??"}";
		// Velopack のバージョンを表示する
		SubTitle += $"  Client Ver {App.AppHost?.Services.GetRequiredService<IUpdateService>()?.GetCurrentVersion()}";
		SetSubMessage();

	}

	void SetSubMessage() {
		var renewstr = $"接続先: {AppGlobal.Config["ConnectionStrings:Url"]} 開始:{_subStartTime.ToString("MM/dd HH:mm")}";
		StatusMessage = $"左側のメニューリストから選択し、ダブルクリックまたはEnterで実行してください。";
		ServerStatus = $"接続先 {AppGlobal.Config.GetSection("ConnectionStrings")?["Url"]} \n製品名 {InfolocalServer.Product ?? "product"} {InfolocalServer.Version ?? "Version"}\nビルド日時 {InfolocalServer.BuildDate}\nサーバ開始 {InfolocalServer.StartTime}\nベースDir {InfolocalServer.BaseDir}";
		ClientStatus = $"アプリ開始時間 {_subStartTime.ToString("yyyy/MM/dd HH:mm")}\n{InfolocalUser.OsVer ?? "OS-version"}\nDOTNET {InfolocalUser.DotnetVer ?? "DOTNET-Version"}\nローカル名   {InfolocalUser.ComputerName} {InfolocalUser.UserName}\nLogin 時間 {InfolocalUser.LoginTime ?? "??:??:??"}\nExpire時間 {InfolocalUser.ExpireTime ?? "??:??:??"}";
	}

	[RelayCommand]
	private void Exit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ClientLib.Exit(this);
		}
	}

	[RelayCommand]
	private void WinMinimize() {
		var window = ClientLib.GetActiveView(this);
		if (window != null) {
			window.WindowState = WindowState.Minimized;
		}
	}
	[RelayCommand]
	private void WinMaximize() {
		var window = ClientLib.GetActiveView(this);
		if (window != null) {
			if (window.WindowState == WindowState.Maximized)
				window.WindowState = WindowState.Normal;
			else
				window.WindowState = WindowState.Maximized;
		}
	}
	Rect startRect = new Rect();
	Rect miniRect = new Rect();


	[RelayCommand]
	private void WinMenuOnly() {
		var window = ClientLib.GetActiveView(this);
		if (window != null && window.WindowState == WindowState.Normal) {
			if (window.Width <= miniRect.Width) {
				window.Left = startRect.X;
				window.Top = startRect.Y;
				window.Width = startRect.Width;
				window.Height = startRect.Height;
			}
			else {
				string fitPosition = AppGlobal.Config["Application:FitPosition"] ?? "";
				if (fitPosition.Contains("Left") && fitPosition.Contains("Top")) {
					window.Left = 0;
					window.Top = 0;
				}
				else if (fitPosition.Contains("Left") && fitPosition.Contains("Bottom")) {
					window.Left = 0;
					window.Top = SystemParameters.WorkArea.Height - miniRect.Height;
				}
				else if (fitPosition.Contains("Right") && fitPosition.Contains("Top")) {
					window.Left = SystemParameters.WorkArea.Width - miniRect.Width;
					window.Top = 0;
				}
				else if (fitPosition.Contains("Right") && fitPosition.Contains("Bottom")) {
					window.Left = SystemParameters.WorkArea.Width - miniRect.Width;
					window.Top = SystemParameters.WorkArea.Height - miniRect.Height;
				}
				else {
					window.Left = miniRect.X;
					window.Top = miniRect.Y;
				}
				window.Width = miniRect.Width;
				window.Height = miniRect.Height;
			}
		}
	}

	[RelayCommand]
	private void SelectMenu(object? parameter) {
		if (parameter is MenuData menu) {
			SelectedMenu = menu;
		}

	}
	[RelayCommand]
	async private Task DoMenu() {
		if (SelectedMenu?.ViewType == null) return;
		if (!SelectedMenu.ViewType.IsSubclassOf(typeof(Window)))
			return;
		// ToDo : ログインしてないときはログイン画面を出す etc リリース時にはちゃんと実装する
		if (InfolocalServer == null) {
			await afterLogin(new _00System.LoginViewModel());
		}
		if (SelectedMenu.IsDialog)
			ClientLib.ExitAllWithoutMe(this);

		if (SelectedMenu.ViewType == typeof(Views._00System.SysGeneralMenteView)) {
			var selectTableView = new Views.Sub.SelectServerTableView {
				Title = "テーブル選択"
			};
			if (ClientLib.ShowDialogView(selectTableView, this, IsDialog: true) != true) {
				return;
			}

			if (selectTableView.DataContext is not Sub.SelectServerTableViewModel selectVm
					|| string.IsNullOrWhiteSpace(selectVm.SelectedTableName)) {
				MessageEx.ShowWarningDialog("テーブルが選択されていません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			if (Activator.CreateInstance(SelectedMenu.ViewType) is not Window targetView) {
				return;
			}

			targetView.Title = SelectedMenu.Header;
			if (targetView.DataContext is Helpers.BaseViewModel targetVm) {
				targetVm.InitParam = SelectedMenu.InitParam;
				targetVm.AddInfo = $"{selectVm.SelectedTableName}|{selectVm.SelectedRowCount}";
			}

			var targetRet = ClientLib.ShowDialogView(targetView, this, IsDialog: SelectedMenu.IsDialog);
			if (targetRet == true) {
				if (targetView.DataContext is _00System.LoginViewModel vm) {
					ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
					_subStartTime = DateTime.Now;
					await afterLogin(vm);
				}
				else if (targetView.DataContext is _00System.SysSetConfigViewModel) {
					SetSubMessage();
				}
			}
			return;
		}

		if (Activator.CreateInstance(SelectedMenu.ViewType) is not Window view) return;
		view.Title = SelectedMenu.Header;
		if (view.DataContext is Helpers.BaseViewModel vm0) {
			vm0.InitParam = SelectedMenu.InitParam;
			vm0.AddInfo = SelectedMenu.AddInfo;
		}
		var ret = ClientLib.ShowDialogView(view, this, IsDialog: SelectedMenu.IsDialog);
		if (ret == true) {
			if (view.DataContext is _00System.LoginViewModel vm) {
				ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
				_subStartTime = DateTime.Now;
				await afterLogin(vm);
			}
			else if (view.DataContext is _00System.SysSetConfigViewModel) {
				SetSubMessage();
			}
		}
	}
	async Task afterLogin(_00System.LoginViewModel vm) {
		if (vm?.LoginData != null) {
			ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
			InfolocalUser.LoginTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
			InfolocalUser.ExpireTime = ExpireDate;
		}
		_subStartTime = DateTime.Now;
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg { Flag = CvFlag.Msg002_GetVersion };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext());
			var version = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as CvBase.Share.InfoServer;
			if (version != null) {
				InfolocalServer = version;
				version.Url = AppGlobal.Config.GetSection("ConnectionStrings")?["Url"] ?? "";
			}
			await RefreshWeatherAsync();
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "サーバ情報の取得に失敗しました。");
		}
		SetSubMessage();
	}

	/// <summary>ショートカットでログイン画面を呼び出す</summary>
	[RelayCommand]
	async private Task ShowLogin() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.LoginView { Title = "ログイン" };
		if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true
			&& view.DataContext is _00System.LoginViewModel vm)
			await afterLogin(vm);
	}
	/// <summary>ショートカットでリフレッシュ画面を呼び出す</summary>
	[RelayCommand]
	async private Task ShowRefresh() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.LoginView { Title = "ログイントークンリフレッシュ" };
		if (view.DataContext is _00System.LoginViewModel vm) {
			vm.InitParam = 1;
			if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true)
				await afterLogin(vm);
		}
	}
	[RelayCommand]
	async private Task ShowSetting() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.SysSetConfigView { Title = "環境設定" };
		if (view.DataContext is _00System.SysSetConfigViewModel vm) {
			if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true)
				SetSubMessage();
		}
	}
	[RelayCommand]
	async private Task ShowUpgrade() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.SysUpgradeView { Title = "システムアップデート" };
		ClientLib.ShowDialogView(view, this, IsDialog: true);
	}

	[RelayCommand]
	private void ToggleTheme() {
		App.ThemeService.ToggleTheme();
	}

	// ── 天気ダッシュボード ──────────────────────

	[ObservableProperty]
	private WeatherInfo? currentWeather;

	[ObservableProperty]
	private string weatherIconKind = "WeatherSunny";

	[ObservableProperty]
	private string weatherTemperature = "--℃";

	[ObservableProperty]
	private string weatherDescription = "取得中...";

	[ObservableProperty]
	private string weatherLocation = "";

	[ObservableProperty]
	private string sunrise = "";
	[ObservableProperty]
	private string sunset = "";

	[ObservableProperty]
	private string humidity = "";
	[ObservableProperty]
	private string windSpeed = "";


	[ObservableProperty]
	private ISeries[] forecastSeries = [];

	[ObservableProperty]
	private Axis[] forecastXAxes = [new Axis { Labels = [], TextSize = 11 }];

	[ObservableProperty]
	private Axis[] forecastYAxes = [new Axis { Name = "", TextSize = 11, MinLimit = null, MaxLimit = null }]; // ℃

	private DispatcherTimer? _weatherTimer;

	private async void StartWeatherAndCalendar() {
		await RefreshWeatherAsync();

		// 天気は30分おきに更新
		_weatherTimer = new DispatcherTimer {
			Interval = TimeSpan.FromMinutes(30)
		};
		_weatherTimer.Tick += async (s, e) => await RefreshWeatherAsync();
		_weatherTimer.Start();
	}

	private async Task RefreshWeatherAsync() {
		try {
			var weatherService = App.AppHost?.Services.GetService<IWeatherService>();
			if (weatherService == null) return;

			var weather = await weatherService.GetCurrentWeatherAsync();
			if (weather != null) {
				CurrentWeather = weather;
				WeatherIconKind = weather.IconKind;
				WeatherTemperature = $"{weather.Temperature:F0}℃";
				WeatherDescription = weather.Description;
				WeatherLocation = weather.Location;
				Sunrise = $"日の出 {weather.SunRize:HH:mm}";
				Sunset = $"日の入 {weather.SunSet:HH:mm}";
				Humidity = $"湿度 {weather.Humidity}%";
				WindSpeed = $"風速 {weather.WindSpeed}m/s";
			}

			var forecasts = await weatherService.GetHourlyForecastAsync();
			if (forecasts.Count > 0) {
				var values = forecasts.Select(f => new ObservablePoint(0, f.Temperature)).ToArray();
				for (int i = 0; i < values.Length; i++) {
					values[i] = new ObservablePoint(i, forecasts[i].Temperature);
				}

				ForecastSeries = [
					new LineSeries<ObservablePoint> {
						Values = values,
						Fill = new SolidColorPaint(new SKColor(33, 150, 243, 80)),
						Stroke = new SolidColorPaint(new SKColor(33, 150, 243)) { StrokeThickness = 2 },
						GeometryFill = new SolidColorPaint(new SKColor(33, 150, 243)),
						GeometryStroke = new SolidColorPaint(new SKColor(33, 150, 243)),
						GeometrySize = 6,
						LineSmoothness = 0.3
					}
				];
				ForecastXAxes = [new Axis {
					Labels = forecasts.Select(f => f.TimeLabel).ToArray(),
					TextSize = 11,
					LabelsRotation = 0
				}];
				ForecastYAxes = [new Axis {
					Name = "", // ℃
					TextSize = 11
				}];
			}
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "天気ダッシュボードの更新に失敗");
		}
	}

	private async void StartClock() {
		// 1. 初回実行
		UpdateDateTime();
		// 2. 「次の秒」までのミリ秒を計算する  例: 現在 12:00:00.350 なら、残り 650ms 待機する
		int delayUntilNextSecond = 1000 - DateTime.Now.Millisecond;

		// 3. 次の秒の切り替わりまで非同期で待機
		await Task.Delay(delayUntilNextSecond);
		_timer = new DispatcherTimer {
			Interval = TimeSpan.FromSeconds(1)
		};
		_timer.Tick += (s, e) => UpdateDateTime();
		_timer.Start();
	}

	private void UpdateDateTime() {
		var now = DateTime.Now;
		if (now.Date != checkDate) {
			culture.DateTimeFormat.Calendar = new System.Globalization.JapaneseCalendar();
			CurrentDate = $"{now:yy/MM/dd} {now.ToString("gy", culture)}";
			Kyureki = $"旧暦 {now.ToSimpleLunisolarStr()}";
			checkDate = now.Date;
		}
		CurrentTime = now.ToString("ddd HH:mm:ss");
	}
	/// <summary>
	/// 指定したMenuDataの親のHeaderを再帰的に探索して返す
	/// </summary>
	private string? FindParentHeader(IEnumerable<MenuData> nodes, MenuData? target) {
		if (target == null) return null;
		foreach (var node in nodes) {
			if (node.SubItems != null && node.SubItems.Contains(target))
				return node.Header;
			if (node.SubItems != null) {
				var found = FindParentHeader(node.SubItems, target);
				if (found != null) return found;
			}
		}
		return null;
	}

}
