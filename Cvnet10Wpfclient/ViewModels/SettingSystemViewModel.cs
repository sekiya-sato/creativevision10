using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Asset;
using Cvnet10Wpfclient.Models;
using Cvnet10Wpfclient.Services;
using Cvnet10Wpfclient.Util;
using Microsoft.Extensions.Configuration;

namespace Cvnet10Wpfclient.ViewModels;

public partial class SettingSystemViewModel : ObservableObject {
    private SystemSettingsStore _store=new();
    private SystemSettingsDocument _currentSettings = new();
    private string? _originalUrl;


    [ObservableProperty]
    private string? url;

    [ObservableProperty]
    private string? loginId;

    [ObservableProperty]
    private string? loginPassword;

    [RelayCommand]
    private void Init() {
        LoadSettings();
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
        WeakReferenceMessenger.Default.Send(new DialogCloseMessage(true));
    }

    [RelayCommand]
    private void Cancel() {
        WeakReferenceMessenger.Default.Send(new DialogCloseMessage(false));
    }

    private void LoadSettings() {
        _currentSettings = _store.Load();
        Url = _currentSettings.ConnectionStrings.Url ?? AppCurrent.Config.GetConnectionString("Url");
        LoginId = _currentSettings.Parameters.LoginId ?? AppCurrent.Config.GetSection("Parameters")?["LoginId"];
        LoginPassword = _currentSettings.Parameters.LoginPass ?? AppCurrent.Config.GetSection("Parameters")?["LoginPass"];
        _originalUrl = Url;
    }
}
