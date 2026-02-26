using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Wpfclient.Models;
using Cvnet10Wpfclient.ViewServices;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.ViewModels;

public partial class ZzMainMenuViewModel : ObservableObject {

	[ObservableProperty]
	ObservableCollection<MenuData> menuItems = [];

	[ObservableProperty]
	private MenuData? selectedMenu;

	[ObservableProperty]
	private string? statusMessage;

	[ObservableProperty]
	private string? expireDate;

	[ObservableProperty]
	private string headerTitle = "Creative Vision 10";


	string _subTitle = ".net10, gRPC, HTTP/2.0 Model";
	private DateTime _subStartTime = DateTime.Now;
	[ObservableProperty]
	private string subTitle = string.Empty;

	[ObservableProperty]
	private bool isMenuReady;

	[ObservableProperty]
	private string? currentDate; // yy/MM/dd 用

	[ObservableProperty]
	private string? currentTime; // HH:mm:ss 用

	private DispatcherTimer? _timer;

	private System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo("ja-JP");


	[RelayCommand]
	private void Init() {
		if (IsMenuReady) {
			return;
		}

		MenuItems = MenuData.CreateDefault();
		StatusMessage = "メニューを選択してください。";
		ExpireDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
		StartClock();
		SetSubTitle();
		IsMenuReady = true;
		var window = ClientLib.GetActiveView(this);
		if (window != null) {
			startRect = window.RestoreBounds;
			miniRect = new Rect() {
				X = startRect.X + startRect.Width - 270,
				Y = startRect.Y,
				Width = 270,
				Height = 700
			};
		}
	}

	void SetSubTitle() {
		SubTitle = $"{_subTitle}  接続先: {AppGlobal.Config.GetSection("ConnectionStrings")?["Url"]} 開始:{_subStartTime.ToString("MM/dd HH:mm")}";
	}

	[RelayCommand]
	private void Exit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ClientLib.Exit(this);
		}
	}

	[RelayCommand]
	private void Minimize() {
		var window = ClientLib.GetActiveView(this);
		if (window != null) {
			window.WindowState = WindowState.Minimized;
		}
	}
	[RelayCommand]
	private void Maximize() {
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
	private void MenuOnly() {
		var window = ClientLib.GetActiveView(this);
		if (window != null && window.WindowState == WindowState.Normal) {
			if (window.Width <= miniRect.Width) {
				window.Left = startRect.X;
				window.Top = startRect.Y;
				window.Width = startRect.Width;
				window.Height = startRect.Height;
			}
			else {
				window.Left = miniRect.X;
				window.Top = miniRect.Y;
				window.Width = miniRect.Width;
				window.Height = miniRect.Height;
			}
		}
	}
	[RelayCommand]
	private async Task Refresh(CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(AppGlobal.LoginJwt))
			return;
		var loginService = AppGlobal.GetgRPCService<ILoginService>();
		var loginRefresh = new LoginRefresh() { Token = AppGlobal.LoginJwt };
		cancellationToken.ThrowIfCancellationRequested();
		try {
			LoginReply reply = new() { JwtMessage = string.Empty };
			var refreshToken = string.Empty;
			reply = await loginService.LoginRefleshAsync(loginRefresh, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Result == 0) {
				AppGlobal.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppGlobal.LoginJwt}");
				if (reply.JwtMessage?.Length > 10) {
					AppGlobal.LoginJwt = reply.JwtMessage;
					ExpireDate = reply.Expire.ToDtStrDateTime2();
				}
			}
			if (string.IsNullOrEmpty(reply.JwtMessage)) {
				AppGlobal.LoginJwt = string.Empty;
				MessageEx.ShowErrorDialog("ログインRefreshができませんでした", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog(ex.Message, owner: ClientLib.GetActiveView(this));
			return;
		}
	}

	[RelayCommand]
	private void SelectMenu(object? parameter) {
		if (parameter is MenuData menu) {
			SelectedMenu = menu;
			StatusMessage = menu.Header;
		}

	}
	[RelayCommand]
	private void DoMenu() {
		if (SelectedMenu?.ViewType == null) return;
		if (!SelectedMenu.ViewType.IsSubclassOf(typeof(Window)))
			return;
		if (SelectedMenu.IsDialog)
			ClientLib.ExitAllWithoutMe(this);
		if (Activator.CreateInstance(SelectedMenu.ViewType) is not Window view) return;
		view.Title = SelectedMenu.Header;
		if (view.DataContext is Helpers.BaseViewModel vm0) {
			vm0.InitParam = SelectedMenu.InitParam;
			vm0.AddInfo = SelectedMenu.AddInfo;
		}
		var ret = ClientLib.ShowDialogView(view, this, IsDialog: SelectedMenu.IsDialog);
		if (ret == true) {
			if (view.DataContext is LoginViewModel vm) {
				ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
				_subStartTime = DateTime.Now;
				SetSubTitle();
			}
			else if (view.DataContext is SettingSystemViewModel) {
				SetSubTitle();
			}
		}
	}

	[RelayCommand]
	private void ToggleTheme() {
		App.ThemeService.ToggleTheme();
	}

	private async void StartClock() {
		//UpdateDateTime(); // 初回実行
		// 2. 「次の秒」までのミリ秒を計算する
		// 例: 現在 12:00:00.350 なら、残り 650ms 待機する
		int delayUntilNextSecond = 1000 - DateTime.Now.Millisecond;

		// 3. 次の秒の切り替わりまで非同期で待機
		await Task.Delay(delayUntilNextSecond);
		UpdateDateTime();
		_timer = new DispatcherTimer {
			Interval = TimeSpan.FromSeconds(1)
		};
		_timer.Tick += (s, e) => UpdateDateTime();
		_timer.Start();
		culture.DateTimeFormat.Calendar = new System.Globalization.JapaneseCalendar();
	}

	private void UpdateDateTime() {
		var now = DateTime.Now;
		CurrentDate = $"{now:yy/MM/dd} {now.ToString("gy", culture)}";
		CurrentTime = now.ToString("ddd HH:mm:ss");
	}

}
