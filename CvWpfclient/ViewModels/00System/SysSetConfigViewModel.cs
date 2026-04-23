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
	private string _originalLoginId = string.Empty;
	private string _originalLoginPassword = string.Empty;


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
		var originalRuntimeUrl = _originalUrl;
		var originalRuntimeLoginId = _originalLoginId;
		var originalRuntimeLoginPassword = _originalLoginPassword;
		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		var loginIdChanged = !string.Equals(_originalLoginId, LoginId, StringComparison.Ordinal);
		var loginPasswordChanged = !string.Equals(_originalLoginPassword, LoginPassword, StringComparison.Ordinal);
		var persistedLoginId = string.IsNullOrWhiteSpace(LoginId) ? _originalLoginId : LoginId;
		var persistedLoginPassword = string.IsNullOrWhiteSpace(LoginPassword) ? _originalLoginPassword : LoginPassword;
		var runtimeOverrides = new Dictionary<string, string?> {
			["ConnectionStrings:Url"] = Url,
			["Parameters:LoginId"] = persistedLoginId,
			["Parameters:LoginPass"] = persistedLoginPassword,
		};
		var overrides = new Dictionary<string, string?>();
		if (urlChanged) {
			overrides["ConnectionStrings:Url"] = Url;
		}
		if (loginIdChanged && !string.IsNullOrWhiteSpace(LoginId)) {
			overrides["Parameters:LoginId"] = LoginId;
		}
		if (loginPasswordChanged && !string.IsNullOrWhiteSpace(LoginPassword)) {
			overrides["Parameters:LoginPass"] = LoginPassword;
		}

		if (isSavel) {
			try {
				_store.SaveConfigurationOverrides(overrides);
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"設定の保存に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return false;
			}
		}
		if (urlChanged) {
			try {
				await App.RestartHostAsync(cancellationToken, runtimeOverrides);
				_originalUrl = Url;
				_originalLoginId = persistedLoginId;
				_originalLoginPassword = persistedLoginPassword;
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"接続先の再構築に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				AppGlobal.UpdateConfigValues(originalRuntimeUrl, originalRuntimeLoginId, originalRuntimeLoginPassword);
				return false;
			}
		}
		else {
			AppGlobal.UpdateConfigValues(Url, persistedLoginId, persistedLoginPassword);
			if (loginIdChanged) {
				_originalLoginId = persistedLoginId;
			}
			if (loginPasswordChanged) {
				_originalLoginPassword = persistedLoginPassword;
			}
			if (urlChanged) {
				_originalUrl = Url;
			}
		}

		_currentSettings.ConnectionStrings.Url = Url;
		_currentSettings.Parameters.LoginId = persistedLoginId;
		_currentSettings.Parameters.LoginPass = persistedLoginPassword;
		LoginId = persistedLoginId;
		LoginPassword = persistedLoginPassword;
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
		_originalLoginId = LoginId;
		_originalLoginPassword = LoginPassword;
	}


}
