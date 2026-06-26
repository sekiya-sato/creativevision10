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
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TymeSolarDay = tyme.solar.SolarDay;

namespace CvWpfclient.ViewModels;

public partial class MainMenuViewModel : ObservableObject {
	private const double MoonIconSize = 24.0;
	private const string DefaultJmaWeatherAreaCode = "130000";
	private const string JmaWeatherOverviewBaseUrl = "https://www.jma.go.jp/bosai/forecast/data/overview_forecast/";
	private static readonly TimeSpan WeatherGrpcTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan JmaWeatherTimeout = TimeSpan.FromSeconds(15);

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
	private string subTitle = ".NET10, gRPC, HTTP/2.0";

	[ObservableProperty]
	private string holidayName = "";

	[ObservableProperty]
	private string solarTerm = "";

	[ObservableProperty]
	private bool isMenuReady;

	[ObservableProperty]
	private string? currentDate; // yy/MM/dd 用

	[ObservableProperty]
	private string? currentTime; // HH:mm:ss 用

	[ObservableProperty]
	private string? currentTimeDay; // 曜日部分

	[ObservableProperty]
	private string? currentTimeClock; // 時刻部分

	[ObservableProperty]
	private SolidColorBrush? currentTimeDayForeground; // 曜日色

	[ObservableProperty]
	private string? kyureki; // 旧暦表示用

	[ObservableProperty]
	private double moonShadowOffset; // 月アイコンの暗い円をずらして満ち欠け形状にする

	[ObservableProperty]
	private string moonPhaseToolTip = "旧暦";

	[ObservableProperty]
	private string functionToolTip = "バージョンアップ(F9) 環境設定(F10) リフレッシュトークン(F11) ログイン(F12)";

	private DispatcherTimer? _timer;
	private string[] _forecastLabels = [];
	private double[] _forecastTemperatures = [];

	private DateTime checkDate = DateTime.MinValue;
	private Dictionary<string, string>? _holidays;

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
		App.ThemeService.ThemeChanged += OnThemeChanged;
		App.MainThemeService.MainThemeChanged += OnMainThemeChanged;
		ApplyForecastTheme();
		UpdateMainThemeButtonLabel(App.MainThemeService.CurrentTheme);
	}

	private void OnThemeChanged(object? sender, AppTheme theme) {
		ApplyForecastTheme();
	}

	private void OnMainThemeChanged(object? sender, MainTheme theme) {
		UpdateMainThemeButtonLabel(theme);
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
			var width = 360;
			var width2 = 740;
			menuonlyRect = new Rect() {
				Width = width,
				Height = 590,
				X = startRect.X + startRect.Width - width,
				Y = startRect.Y
			};
			smallRect = new Rect() {
				Width = width2,
				Height = 700,
				X = startRect.X + startRect.Width - width2,
				Y = startRect.Y
			};
		}
		StartClock();
		StartWeatherAndCalendar();
		_ = LoadHolidaysAsync();
		var now = DateTime.Now;
		ExpireDate = now.ToString("yyyy/MM/dd HH:mm");
		UpdateKyureki(now);

		InfolocalUser.OsVer = Environment.OSVersion.ToString();
		InfolocalUser.DotnetVer = Environment.Version.ToString();
		InfolocalUser.ComputerName = Environment.MachineName;
		InfolocalUser.UserName = Environment.UserName;
		InfolocalServer.Url = AppGlobal.Url;
		ClientStatus = $"アプリ開始時間 {_subStartTime.ToString("yyyy/MM/dd HH:mm")}\n{InfolocalUser.OsVer ?? "OS-version"}\nDOTNET {InfolocalUser.DotnetVer ?? "DOTNET-Version"}\nローカル名 {InfolocalUser.ComputerName} {InfolocalUser.UserName}\nLogin時間 {InfolocalUser.LoginTime ?? "??:??:??"}\nExpire時間 {InfolocalUser.ExpireTime ?? "??:??:??"}";
		// Velopack のバージョンを表示する
		var version = App.AppHost?.Services.GetRequiredService<IUpdateService>()?.GetFileVersion() ?? "Unknown";
		if (version == "1.0.0.0") { // GetFileVersionで取得できない場合GetCurrentVersionで取得
			version = App.AppHost?.Services.GetRequiredService<IUpdateService>()?.GetCurrentVersion() ?? "Unknown";
		}
		SubTitle += $"  Client Ver {version}";
		SetSubMessage();
	}

	void SetSubMessage() {
		var renewstr = $"接続先: {AppGlobal.Url} 開始:{_subStartTime.ToString("MM/dd HH:mm")}";
		StatusMessage = $"左側のメニューリストから選択し、ダブルクリックまたはEnterで実行してください。";
		ServerStatus = $"接続先 {AppGlobal.Url} \n製品名 {InfolocalServer.Product ?? "product"} {InfolocalServer.Version ?? "Version"}\nビルド日時 {InfolocalServer.BuildDate}\nサーバ開始 {InfolocalServer.StartTime}\nベースDir {InfolocalServer.BaseDir}\n{InfolocalServer.OsVersion ?? "OS-version"}\nDOTNET {InfolocalServer.DotNetVersion ?? "DOTNET-Version"}\nローカル名 {InfolocalServer.MachineName}";
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
	Rect menuonlyRect = new Rect();
	Rect smallRect = new Rect();


	[RelayCommand]
	private void WinMenuOnly() {
		var window = ClientLib.GetActiveView(this);
		if (window != null && window.WindowState == WindowState.Normal) {
			if (window.Width <= menuonlyRect.Width) {
				(window.Left, window.Top, window.Width, window.Height) =
					(startRect.X, startRect.Y, startRect.Width, startRect.Height);
			}
			else {
				string fitPosition = AppGlobal.FitPosition;

				window.Left = fitPosition.Contains("Right")
					? SystemParameters.WorkArea.Width - menuonlyRect.Width
					: fitPosition.Contains("Left") ? 0 : menuonlyRect.X;

				window.Top = fitPosition.Contains("Bottom")
					? SystemParameters.WorkArea.Height - menuonlyRect.Height
					: fitPosition.Contains("Top") ? 0 : menuonlyRect.Y;

				window.Width = menuonlyRect.Width;
				window.Height = menuonlyRect.Height;
			}
		}
	}
	[RelayCommand]
	private void WinSmall() {
		var window = ClientLib.GetActiveView(this);
		if (window != null && window.WindowState == WindowState.Normal) {
			if (window.Width <= smallRect.Width) {
				(window.Left, window.Top, window.Width, window.Height) =
					(startRect.X, startRect.Y, startRect.Width, startRect.Height);
			}
			else {
				string fitPosition = AppGlobal.FitPosition;

				window.Left = fitPosition.Contains("Right")
					? SystemParameters.WorkArea.Width - smallRect.Width
					: fitPosition.Contains("Left") ? 0 : smallRect.X;

				window.Top = fitPosition.Contains("Bottom")
					? SystemParameters.WorkArea.Height - smallRect.Height
					: fitPosition.Contains("Top") ? 0 : smallRect.Y;

				window.Width = smallRect.Width;
				window.Height = smallRect.Height;
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
		// Product : ログインしてないときはログイン画面を出す etc リリース前に実装
		if (InfolocalServer == null) {
			await afterLogin(new _00System.LoginViewModel());
		}
		if (SelectedMenu.IsDialog)
			ClientLib.ExitAllWithoutMe(this);

		if (SelectedMenu.ViewType == typeof(Views._00System.SysGeneralMenteView)) {
			var selectTableView = new Views.Sub.SelectServerTableView {
				Title = "テーブル選択"
			};
			if (ClientLib.ShowDialogView(selectTableView, null, IsDialog: true) != true) {
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

			var targetRet = ClientLib.ShowDialogView(targetView, null, IsDialog: SelectedMenu.IsDialog);
			if (targetRet == true) {
				if (targetView.DataContext is _00System.LoginViewModel vm) {
					ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
					await afterLogin(vm);
				}
				else if (targetView.DataContext is _00System.SysSetConfigViewModel) {
					SetSubMessage();
					await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());
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
		var ret = ClientLib.ShowDialogView(view, null, IsDialog: SelectedMenu.IsDialog);
		if (ret == true) {
			if (view.DataContext is _00System.LoginViewModel vm) {
				ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
				await afterLogin(vm);
			}
			else if (view.DataContext is _00System.SysSetConfigViewModel) {
				SetSubMessage();
				await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());
			}
		}
	}
	async Task afterLogin(_00System.LoginViewModel vm) {
		if (vm?.LoginData != null) {
			ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
			InfolocalUser.LoginTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
			InfolocalUser.ExpireTime = ExpireDate;
			var serverVer = Common.DeserializeObject<InfoServer>(vm.LoginData?.InfoPayload ?? "");
			if (serverVer != null && !string.IsNullOrEmpty(serverVer.Product)) {
				InfolocalServer = serverVer;
				InfolocalServer.Url = AppGlobal.Url;
			}
			await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());
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
			if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true) {
				SetSubMessage();
				await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());
			}
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
		//App.SaveThemePreference(App.ThemeService.CurrentTheme);
	}

	[RelayCommand]
	private void ToggleMainTheme() {
		App.MainThemeService.ToggleMainTheme();
		//App.SaveMainThemePreference(App.MainThemeService.CurrentTheme);
	}

	[ObservableProperty]
	private string mainThemeButtonLabel = "テーマ切替(Default)";

	private void UpdateMainThemeButtonLabel(MainTheme theme) {
		MainThemeButtonLabel = $"テーマ切替({theme})";
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
	private string jmaWeatherOverviewToolTip = "気象庁概要予報を取得中...";


	[ObservableProperty]
	private ISeries[] forecastSeries = [];

	[ObservableProperty]
	private LiveChartsCore.Measure.Margin? forecastMargin = new LiveChartsCore.Measure.Margin(0, 0, 0, 0);

	[ObservableProperty]
	private Axis[] forecastXAxes = [new Axis { Labels = [], TextSize = 11 }];

	[ObservableProperty]
	private Axis[] forecastYAxes = [new Axis { Name = "", TextSize = 11, MinLimit = null, MaxLimit = null }]; // ℃

	private DispatcherTimer? _weatherTimer;

	private async void StartWeatherAndCalendar() {
		await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());

		// 天気は30分おきに更新
		_weatherTimer = new DispatcherTimer {
			Interval = TimeSpan.FromMinutes(30)
		};
		_weatherTimer.Tick += async (s, e) => await RefreshWeatherDashboardAsync(App.GetHostLifetimeToken());
		_weatherTimer.Start();
	}

	private async Task RefreshWeatherDashboardAsync(CancellationToken cancellationToken) {
		await Task.WhenAll(
			RefreshWeatherServerAsync(cancellationToken),
			RefreshJmaWeatherOverviewAsync(cancellationToken));
	}

	private async Task RefreshWeatherServerAsync(CancellationToken cancellationToken) {
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var weatherService = AppGlobal.GetGrpcService<IWeatherService>();
			var reagion = AppGlobal.WeatherRegion;
			var weather = await weatherService.GetCurrentWeatherAsync(reagion, AppGlobal.GetDefaultCallContext(cancellationToken, WeatherGrpcTimeout));
			cancellationToken.ThrowIfCancellationRequested();
			if (weather != null) {
				CurrentWeather = weather;
				WeatherIconKind = weather.IconKind;
				WeatherTemperature = $"{weather.Temperature:F0}℃";
				WeatherDescription = weather.Description;
				WeatherLocation = weather.Location;
				Sunrise = $"日の出 {weather.Sunrise:HH:mm}";
				Sunset = $"日の入 {weather.Sunset:HH:mm}";
				Humidity = $"湿度 {weather.Humidity}%";
				WindSpeed = $"風速 {weather.WindSpeed}m/s";
			}
			var forecasts = await weatherService.GetHourlyForecastAsync(reagion, AppGlobal.GetDefaultCallContext(cancellationToken, WeatherGrpcTimeout));
			cancellationToken.ThrowIfCancellationRequested();
			if (forecasts.Count > 0) {
				_forecastLabels = forecasts.Select(f => f.TimeLabel).ToArray();
				_forecastTemperatures = forecasts.Select(f => f.Temperature).ToArray();
				ApplyForecastTheme();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			return;
		}
		catch {
			WeatherDescription = "天気情報を取得できませんでした";
		}
	}

	private async Task RefreshJmaWeatherOverviewAsync(CancellationToken cancellationToken) {
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var areaCode = NormalizeJmaWeatherAreaCode(AppGlobal.JmaWeatherAreaCode);
			using var client = new HttpClient { Timeout = JmaWeatherTimeout };
			using var response = await client.GetAsync($"{JmaWeatherOverviewBaseUrl}{areaCode}.json", cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (!response.IsSuccessStatusCode) {
				JmaWeatherOverviewToolTip = $"気象庁概要予報を取得できませんでした ({areaCode})";
				return;
			}

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			var overview = await JsonSerializer.DeserializeAsync<JmaOverviewForecast>(stream, cancellationToken: cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			JmaWeatherOverviewToolTip = overview == null
				? $"気象庁概要予報を取得できませんでした ({areaCode})"
				: FormatJmaWeatherOverview(overview, areaCode);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			return;
		}
		catch {
			JmaWeatherOverviewToolTip = "気象庁概要予報を取得できませんでした";
		}
	}

	private static string NormalizeJmaWeatherAreaCode(string? areaCode) {
		var normalized = string.IsNullOrWhiteSpace(areaCode) ? DefaultJmaWeatherAreaCode : areaCode.Trim();
		return normalized.Length == 6 && normalized.All(char.IsDigit)
			? normalized
			: DefaultJmaWeatherAreaCode;
	}

	private static string FormatJmaWeatherOverview(JmaOverviewForecast overview, string areaCode) {
		var lines = new List<string> {
			$"{(string.IsNullOrWhiteSpace(overview.PublishingOffice) ? "気象庁" : overview.PublishingOffice)} {FormatJmaReportDateTime(overview.ReportDatetime)}",
			string.IsNullOrWhiteSpace(overview.TargetArea) ? $"予報区 {areaCode}" : $"{overview.TargetArea} ({areaCode})",
		};

		var headline = NormalizeJmaText(overview.HeadlineText);
		if (!string.IsNullOrWhiteSpace(headline)) {
			lines.Add(string.Empty);
			lines.Add(headline);
		}

		var text = NormalizeJmaText(overview.Text);
		if (!string.IsNullOrWhiteSpace(text)) {
			lines.Add(string.Empty);
			lines.Add(text);
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string FormatJmaReportDateTime(string? reportDatetime) {
		if (DateTimeOffset.TryParse(reportDatetime, out var parsed)) {
			return $"{parsed.ToOffset(TimeSpan.FromHours(9)):yyyy/MM/dd HH:mm} 発表";
		}

		return "発表時刻不明";
	}

	private static string NormalizeJmaText(string? text) {
		return string.IsNullOrWhiteSpace(text)
			? string.Empty
			: text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
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

	private async Task LoadHolidaysAsync() {
		try {
			using var client = new HttpClient();
			using var response = await client.GetAsync("https://holidays-jp.github.io/api/v1/date.json");
			if (!response.IsSuccessStatusCode) {
				_holidays = null;
				return;
			}
			var json = await response.Content.ReadAsStringAsync();
			if (string.IsNullOrWhiteSpace(json)) {
				_holidays = null;
				return;
			}
			_holidays = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
			Application.Current?.Dispatcher.Invoke(UpdateDateTime);
		}
		catch {
			_holidays = null;
		}
	}

	private void UpdateDateTime() {
		var now = DateTime.Now;
		if (now.Date != checkDate) {
			culture.DateTimeFormat.Calendar = new System.Globalization.JapaneseCalendar();
			CurrentDate = $"{now:yy/MM/dd} {now.ToString("gy", culture)}";
			UpdateKyureki(now);
			checkDate = now.Date;
		}
		CurrentTime = now.ToString("ddd HH:mm:ss");
		CurrentTimeDay = $"{now:ddd} ";
		CurrentTimeClock = now.ToString("HH:mm:ss");

		var dateKey = now.ToString("yyyy-MM-dd");
		if (_holidays?.TryGetValue(dateKey, out var holiday) == true) {
			CurrentTimeDayForeground = Brushes.Red;
			HolidayName = $" {holiday}";
		}
		else {
			HolidayName = "";
			CurrentTimeDayForeground = now.DayOfWeek switch {
				DayOfWeek.Saturday => Brushes.LightBlue,
				DayOfWeek.Sunday => Brushes.Red,
				_ => FindResource("TitleColor") as SolidColorBrush ?? Brushes.White
			};
		}
	}

	private static System.Globalization.JapaneseLunisolarCalendar luna = new System.Globalization.JapaneseLunisolarCalendar();
	private void UpdateKyureki(DateTime now) {
		int kyurekiDay = luna.GetDayOfMonth(now);
		// 添付画像の並びに合わせ、29日と30日は28日と同じ表示にする。
		var moonDay = Math.Min(Math.Clamp(kyurekiDay, 1, 30), 28);
		var isWaxing = moonDay <= 15;
		var lightRatio = moonDay <= 15
			? (moonDay - 1) / 14.0
			: (28 - moonDay) / 13.0;
		lightRatio = Math.Clamp(lightRatio, 0.0, 1.0);

		var shadowOffset = MoonIconSize * lightRatio;
		// 日本では満ちていく月は右側、欠けていく月は左側を明るく表示する。
		Kyureki = $"旧: {now.ToSimpleLunisolarStr()}";
		MoonShadowOffset = isWaxing ? -shadowOffset : shadowOffset;
		var moonPhaseName = moonDay switch {
			<= 1 => "新月",
			<= 4 => "三日月",
			<= 7 => "上弦前",
			8 => "上弦",
			<= 14 => "上弦後",
			15 => "満月",
			16 => "十六夜",
			17 => "立待月",
			18 => "居待月",
			19 => "寝待月",
			<= 21 => "下弦前",
			22 => "下弦",
			< 29 => "下弦後",
			_ => "今夜の月"
		};
		MoonPhaseToolTip = $"{moonPhaseName}：旧暦 {now.ToSimpleLunisolarStrDigit()}";
		SolarTerm = GetSolarTermName(now);
	}

	private static string GetSolarTermName(DateTime date) {
		var japanSolarTerms = new Dictionary<string, string>
		{ { "芒种", "芒種" },{ "处暑", "処暑" }, { "惊蛰", "啓蟄" }, { "谷雨", "穀雨" }, { "小满", "小満" } };

		try {
			var solarDay = TymeSolarDay.FromYmd(date.Year, date.Month, date.Day);
			var term = solarDay.Term;
			var chinaTerm = term?.GetName() ?? "";
			var japanTerm = japanSolarTerms.TryGetValue(chinaTerm, out var jp) ? jp : chinaTerm;
			return japanTerm;
		}
		catch {
			return "";
		}
	}

	private void ApplyForecastTheme() {
		// ForecastYAxes = [new Axis { Name = "℃", TextSize = 10,	}];

		if (_forecastTemperatures.Length == 0) {
			ForecastSeries = [];
			return;
		}

		var lineColor = ToSkColor(GetResourceColor("MainMenuChartLineColor", Color.FromRgb(33, 150, 243)));
		var fillColor = ToSkColor(GetResourceColor("MainMenuChartFillColor", Color.FromArgb(80, 33, 150, 243)));
		var textColor = ToSkColor(GetResourceColor("MainMenuChartTextColor", Color.FromRgb(0, 0, 0)));
		var separatorColor = textColor.WithAlpha(51); // 20% opacity

		var values = _forecastTemperatures
			.Select((temperature, index) => new ObservablePoint(index, temperature))
			.ToArray();
		// 件数が増えた場合にラベルが重ならないよう、表示ラベルを間引き、回転して配置する
		const int MaxVisibleLabels = 36;
		var labelStep = (int)Math.Ceiling((double)_forecastLabels.Length / MaxVisibleLabels);
		var displayLabels = _forecastLabels
			.Select((label, index) => index % labelStep == 0 ? label : string.Empty)
			.ToArray();
		var isDense = _forecastLabels.Length > MaxVisibleLabels;
		ForecastXAxes = [new Axis {
				Labels = displayLabels,
				TextSize = isDense ? 9 : 11,
				LabelsRotation = isDense ? 45 : 0,
				LabelsPaint = new SolidColorPaint(textColor),
				SeparatorsPaint = new SolidColorPaint(separatorColor)
			}];
		// 縦軸: 5℃刻み、最小・最大をデータに合わせて少しパディング
		var minTemp = _forecastTemperatures.Min();
		var maxTemp = _forecastTemperatures.Max();
		ForecastYAxes = [new Axis {
			TextSize = 10,
			MinStep = 5,                              // ← 5刻みに
			ForceStepToMin = true,                    // ← 自動調整ではなく5刻みを強制
			MinLimit = Math.Floor(minTemp / 5) * 5,  // ← 下限を5の倍数に揃える
			MaxLimit = Math.Ceiling(maxTemp / 5) * 5, // ← 上限を5の倍数に揃える
			LabelsPaint = new SolidColorPaint(textColor),
			SeparatorsPaint = new SolidColorPaint(separatorColor)
		}];
		ForecastMargin = new LiveChartsCore.Measure.Margin(4, 8, 4, 8);
		ForecastSeries = [
			new LineSeries<ObservablePoint> {
					Values = values,
					Fill = new SolidColorPaint(fillColor),
					Stroke = new SolidColorPaint(lineColor) { StrokeThickness = 2 },
					GeometryFill = new SolidColorPaint(lineColor),
					GeometryStroke = new SolidColorPaint(lineColor),
					GeometrySize = 6,
					LineSmoothness = 0.3
				}
		];
	}

	private static Color GetResourceColor(string key, Color fallback) {
		var resource = FindResource(key);
		return resource switch {
			SolidColorBrush brush => brush.Color,
			Color color => color,
			_ => fallback,
		};
	}

	private static object? FindResource(string key) {
		var resources = Application.Current?.Resources;
		if (resources == null) {
			return null;
		}

		if (resources.Contains(key)) {
			return resources[key];
		}

		for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--) {
			var dictionary = resources.MergedDictionaries[i];
			if (dictionary.Contains(key)) {
				return dictionary[key];
			}
		}

		return null;
	}

	private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
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

	private sealed class JmaOverviewForecast {
		[JsonPropertyName("publishingOffice")]
		public string? PublishingOffice { get; set; }

		[JsonPropertyName("reportDatetime")]
		public string? ReportDatetime { get; set; }

		[JsonPropertyName("targetArea")]
		public string? TargetArea { get; set; }

		[JsonPropertyName("headlineText")]
		public string? HeadlineText { get; set; }

		[JsonPropertyName("text")]
		public string? Text { get; set; }
	}
}
