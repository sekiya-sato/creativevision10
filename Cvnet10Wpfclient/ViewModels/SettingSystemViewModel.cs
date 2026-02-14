using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.Services;
using Cvnet10Wpfclient.ViewServices;
using Microsoft.Extensions.Configuration;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;

public partial class SettingSystemViewModel : Helpers.BaseViewModel {
    private SystemSettingsStore _store=new();
    private SystemSettingsDocument _currentSettings = new();
    private string _originalUrl = string.Empty;


    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string loginId = string.Empty;

	[ObservableProperty]
    private string loginPassword = string.Empty;

    [RelayCommand]
    private void Init() {
        LoadSettings();
	}
	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("èIóπÇµÇ‹Ç∑Ç©ÅH", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ExitWithResultFalse();
		}
	}


	[RelayCommand(IncludeCancelCommand = true)]
    private async Task SaveAsync(CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(Url)) {
            MessageEx.ShowErrorDialog("ê⁄ë±êÊ URL Çì¸óÕÇµÇƒÇ≠ÇæÇ≥Ç¢ÅB", owner: ClientLib.GetActiveView(this));
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _currentSettings.ConnectionStrings.Url = Url;
        _currentSettings.Parameters.LoginId = LoginId;
        _currentSettings.Parameters.LoginPass = LoginPassword;

        try {
            _store.Save(_currentSettings);
        }
        catch (Exception ex) {
            MessageEx.ShowErrorDialog($"ê›íËÇÃï€ë∂Ç…é∏îsÇµÇ‹ÇµÇΩ: {ex.Message}", owner: ClientLib.GetActiveView(this));
            return;
        }

        var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
        if (urlChanged) {
            try {
                await App.RestartHostAsync(cancellationToken);
            }
            catch (Exception ex) {
                MessageEx.ShowErrorDialog($"ê⁄ë±êÊÇÃçƒç\ízÇ…é∏îsÇµÇ‹ÇµÇΩ: {ex.Message}", owner: ClientLib.GetActiveView(this));
                return;
            }
        }

        _originalUrl = Url;
        ExitWithResultTrue();
    }

    private void LoadSettings() {
        _currentSettings = _store.Load();
        Url = _currentSettings.ConnectionStrings.Url ?? AppGlobal.Config.GetConnectionString("Url") ?? string.Empty;
        LoginId = _currentSettings.Parameters.LoginId ?? AppGlobal.Config.GetSection("Parameters")?["LoginId"] ?? string.Empty;
        LoginPassword = _currentSettings.Parameters.LoginPass ?? AppGlobal.Config.GetSection("Parameters")?["LoginPass"] ?? string.Empty;
        _originalUrl = Url;
    }
}
