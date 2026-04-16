using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.Services;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSetConfigViewModel : Helpers.BaseViewModel {
	private ClientSettingsStore _store = new();
	private ClientSettingsDocument _currentSettings = new();
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
	async Task<bool> saveLocalSetting(bool isSavel, CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(Url)) {
			MessageEx.ShowErrorDialog("接続先 URL を入力してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}

		cancellationToken.ThrowIfCancellationRequested();
		_currentSettings.ConnectionStrings.Url = Url;
		_currentSettings.Parameters.LoginId = LoginId;
		_currentSettings.Parameters.LoginPass = LoginPassword;
		AppGlobal.UpdateConfigValues(Url, LoginId, LoginPassword);
		if (isSavel) {
			try {
				_store.Save(_currentSettings);
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"設定の保存に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return false;
			}
		}
		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		if (urlChanged) {
			try {
				var setting = new Dictionary<string, string?> {
					["ConnectionStrings:Url"] = Url,
					["Parameters:LoginId"] = LoginId,
					["Parameters:LoginPass"] = LoginPassword,
				};
				await App.RestartHostAsync(cancellationToken, setting);
				_originalUrl = Url;
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"接続先の再構築に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return false;
			}
		}
		return true;
	}




	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SaveAsync(CancellationToken cancellationToken) {
		if (await saveLocalSetting(true, cancellationToken)) {
			ExitWithResultTrue();
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task NoSaveAsync(CancellationToken cancellationToken) {
		if (await saveLocalSetting(false, cancellationToken)) {
			ExitWithResultTrue();
		}
	}

	private void LoadSettings() {
		_currentSettings = _store.Load();
		Url = AppGlobal.Url ?? string.Empty;
		LoginId = AppGlobal.Parameters.LoginId;
		LoginPassword = AppGlobal.Parameters.LoginPass;
		_originalUrl = Url;
	}


}
