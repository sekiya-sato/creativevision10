using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Wpfclient.Models;
using Cvnet10Wpfclient.Util;
using Cvnet8client.Views.Sub;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MainMenuViewModel : ObservableObject {

    [ObservableProperty]
    ObservableCollection<MenuData> menuItems = new();

    [ObservableProperty]
    private MenuData? selectedMenu;

	[ObservableProperty]
	private string? statusMessage;

	[ObservableProperty]
	private string? expireDate;

	[ObservableProperty]
	private string headerTitle = "Creative Vision.net 10";

	[ObservableProperty]
	private string subTitle = "gRPC Server and HTTP/2.0 Model";


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
		IsMenuReady = true;
    }

    [RelayCommand]
    private void Exit() {
        if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
            ClientLib.Exit(this);
        }
    }

    [RelayCommand]
    private void Minimize() {
        var window = Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive);
        if (window != null) {
            window.WindowState = WindowState.Minimized;
        }
    }
	[RelayCommand]
	private void Maximize() {
		var window = Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive);
		if (window != null) {
            if(window.WindowState == WindowState.Maximized) 
                window.WindowState = WindowState.Normal;
            else 
                window.WindowState = WindowState.Maximized;
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
		if (SelectedMenu == null) return;
		if (SelectedMenu.ViewType == null || !SelectedMenu.ViewType.IsSubclassOf(typeof(Window)))
			return;
		if (SelectedMenu.IsDialog)
			ClientLib.ExitAllWithoutMe(this);
		var view = Activator.CreateInstance(SelectedMenu.ViewType) as Window;
		if (view == null) return;
		view.Title = SelectedMenu.Header;
		if (view.DataContext is IBaseViewModel vm0) {
			vm0.InitParam = SelectedMenu.InitParam;
			vm0.AddInfo = SelectedMenu.AddInfo;
		}
		var ret = ClientLib.ShowDialogView(view, this, IsDialog: SelectedMenu.IsDialog);
		if (ret == true) {
			if (view.DataContext is LoginViewModel vm) {
				ExpireDate = vm.LoginData?.Expire.ToDtStrDateTime2();
			}
		}
	}

	[RelayCommand]
	private void ToggleTheme() {
		App.ThemeService.ToggleTheme();
	}

	private void StartClock() {
		_timer = new DispatcherTimer {
			Interval = TimeSpan.FromSeconds(1)
		};
		_timer.Tick += (s, e) => UpdateDateTime();
		_timer.Start();
		culture.DateTimeFormat.Calendar = new System.Globalization.JapaneseCalendar();
		UpdateDateTime(); // 初回実行
	}

	private void UpdateDateTime() {
		var now = DateTime.Now;
		CurrentDate = $"{now:yy/MM/dd} {now.ToString("gy", culture)}";
		CurrentTime = now.ToString("ddd HH:mm:ss");
	}

}
