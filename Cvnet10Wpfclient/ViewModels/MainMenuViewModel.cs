using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MainMenuViewModel : ObservableObject {

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

	[ObservableProperty]
	private string serverStatus = string.Empty;

	[ObservableProperty]
	private string clientStatus = string.Empty;


	[RelayCommand]
	private void Init() {
		if (IsMenuReady) {
			return;
		}

		MenuItems = MenuData.CreateDefault();
		ExpireDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
		StartClock();
		SetSubTitle();
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
	}

	void SetSubTitle() {
		var renewstr = $"接続先: {AppGlobal.Config.GetSection("ConnectionStrings")?["Url"]} 開始:{_subStartTime.ToString("MM/dd HH:mm")}";
		SubTitle = $"{_subTitle} {renewstr}";
		StatusMessage = $"メニューを選択してください。 \nF12でログイン画面、F11でトークンリフレッシュ画面";
		ServerStatus = $"接続先 {AppGlobal.Config.GetSection("ConnectionStrings")?["Url"]} \nサーバ開始時間(継続時間)\nOS-version\nDOTNET-Version";
		ClientStatus = $"アプリ開始時間 {_subStartTime.ToString("MM/dd HH:mm")}\nOS-version\nDOTNET-Version\nユーザ名 ABC\nLogin時間\nExpire時間";
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
				window.Left = miniRect.X;
				window.Top = miniRect.Y;
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
			if (view.DataContext is _00System.LoginViewModel vm) {
				ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
				_subStartTime = DateTime.Now;
				SetSubTitle();
			}
			else if (view.DataContext is _00System.SysSetConfigViewModel) {
				SetSubTitle();
			}
		}
	}
	void afterLogin(_00System.LoginViewModel vm) {
		ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
		_subStartTime = DateTime.Now;
		SetSubTitle();
	}



	/// <summary>ショートカットでログイン画面を呼び出す</summary>
	[RelayCommand]
	private void ShowLogin() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.LoginView { Title = "ログイン" };
		if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true
			&& view.DataContext is _00System.LoginViewModel vm)
			afterLogin(vm);
	}
	/// <summary>ショートカットでリフレッシュ画面を呼び出す</summary>
	[RelayCommand]
	private void ShowRefresh() {
		ClientLib.ExitAllWithoutMe(this);
		var view = new Views._00System.LoginView { Title = "ログイントークンリフレッシュ" };
		if (view.DataContext is _00System.LoginViewModel vm) {
			vm.InitParam = 1;
			if (ClientLib.ShowDialogView(view, this, IsDialog: true) == true)
				afterLogin(vm);
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
