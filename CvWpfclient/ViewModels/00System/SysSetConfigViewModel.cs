using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
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
	/*
	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ExitWithResultFalse();
		}
	}*/


	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SaveAsync(CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(Url)) {
			MessageEx.ShowErrorDialog("接続先 URL を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();
		AppGlobal.UpdateConfigValues(Url, LoginId, LoginPassword);

		try {
			_store.Save(_currentSettings);
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"設定の保存に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
			return;
		}

		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		if (urlChanged) {
			try {
				await App.RestartHostAsync(cancellationToken);
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"接続先の再構築に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return;
			}
		}

		_originalUrl = Url;
		ExitWithResultTrue();
	}
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task NoSaveAsync(CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(Url)) {
			MessageEx.ShowErrorDialog("接続先 URL を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		cancellationToken.ThrowIfCancellationRequested();
		AppGlobal.UpdateConfigValues(Url, LoginId, LoginPassword);
		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		if (urlChanged) {
			try {
				var setting = new Dictionary<string, string?> {
					["ConnectionStrings:Url"] = Url,
					["Parameters:LoginId"] = LoginId,
					["Parameters:LoginPass"] = LoginPassword,
				};
				await App.RestartHostAsync(cancellationToken, setting);
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"接続先の再構築に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return;
			}
		}
		_originalUrl = Url;
		ExitWithResultTrue();
	}

	private void LoadSettings() {
		_currentSettings = _store.Load();
		Url = AppGlobal.Url ?? string.Empty;
		LoginId = AppGlobal.Config.GetSection("Parameters")?["LoginId"] ?? string.Empty;
		LoginPassword = AppGlobal.Config.GetSection("Parameters")?["LoginPass"] ?? string.Empty;
		_originalUrl = Url;
	}


}
