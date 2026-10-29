using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfpos.Services;
using CvWpfpos.Views._00System;
using CvWpfpos.Views._06Uriage;
using System.Windows;

namespace CvWpfpos.ViewModels;

/// <summary>
/// POS メニュー画面。ログイン後の初期画面として各機能へ遷移する。
/// </summary>
public partial class MenuViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string StoreName { get; set; }

    [ObservableProperty]
    public partial string LoginId { get; set; }

    [ObservableProperty]
    public partial string CurrentDateText { get; set; }

    public string StatusText => $"{StoreName}  |  {LoginId}";

    public MenuViewModel() : this(AppGlobal.Settings, AppGlobal.LoginId) { }

    public MenuViewModel(PosSettings settings, string loginId)
    {
        StoreName = settings.StoreName;
        LoginId = loginId;
        CurrentDateText = DateTime.Now.ToString("yyyy/MM/dd(ddd) HH:mm");
    }

    [RelayCommand]
    private void OpenSales()
    {
        var window = new PosUriageInputView();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenSeisan()
    {
        var dialog = new PosSeisanView { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void OpenReports()
    {
        var dialog = new PosReportView { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void OpenJournal()
    {
        var dialog = new PosReceiptListView { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ReLogin()
    {
        AppGlobal.Settings.AccessToken = string.Empty;
        AppGlobal.TokenStore.Clear();
        if (new LoginView().ShowDialog() != true)
        {
            Application.Current.Shutdown();
            return;
        }
        LoginId = AppGlobal.LoginId;
        OnPropertyChanged(nameof(StatusText));
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    private static void ShowNotReady(string feature)
    {
        MessageBox.Show(Application.Current.MainWindow, $"{feature} は後続フェーズで実装されます。", "準備中", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
