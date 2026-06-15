using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.Services;
using System.Globalization;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSetConfigViewModel : Helpers.BaseViewModel {
	private const string DefaultWeatherRegion = "Tokyo";
	private const string DefaultHorizontalFitPosition = "Left";
	private const string DefaultVerticalFitPosition = "Bottom";
	private const int DefaultLimit = 400;
	private ClientSettingsStore _store = new();
	private ClientSettingsDocument _currentSettings = new();
	private string _originalUrl = string.Empty;
	private string _originalLoginId = string.Empty;
	private string _originalLoginPassword = string.Empty;
	private string _originalWeatherRegion = DefaultWeatherRegion;
	private string _originalFitPosition = $"{DefaultHorizontalFitPosition}-{DefaultVerticalFitPosition}";
	private int _originalLimit = DefaultLimit;

	public string[] HorizontalFitPositionItems { get; } = [DefaultHorizontalFitPosition, "Right"];
	public string[] VerticalFitPositionItems { get; } = ["Top", DefaultVerticalFitPosition];

	[ObservableProperty]
	private string url = string.Empty;

	[ObservableProperty]
	private string loginId = string.Empty;

	[ObservableProperty]
	private string loginPassword = string.Empty;

	[ObservableProperty]
	private string weatherRegion = DefaultWeatherRegion;

	[ObservableProperty]
	private string horizontalFitPosition = DefaultHorizontalFitPosition;

	[ObservableProperty]
	private string verticalFitPosition = DefaultVerticalFitPosition;

	[ObservableProperty]
	private int limit = DefaultLimit;

	public string FitPosition => $"{HorizontalFitPosition}-{VerticalFitPosition}";

	partial void OnHorizontalFitPositionChanged(string value) {
		OnPropertyChanged(nameof(FitPosition));
	}

	partial void OnVerticalFitPositionChanged(string value) {
		OnPropertyChanged(nameof(FitPosition));
	}

	[RelayCommand]
	private void Init() {
		LoadSettings();
	}
	async Task<bool> saveLocalSetting(bool isSavel, CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(Url)) {
			MessageEx.ShowErrorDialog("接続先 URL を入力してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}

		var normalizedWeatherRegion = WeatherRegion.Trim();
		if (string.IsNullOrWhiteSpace(normalizedWeatherRegion)) {
			MessageEx.ShowErrorDialog("天気地域を入力してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (Array.IndexOf(HorizontalFitPositionItems, HorizontalFitPosition) < 0
				|| Array.IndexOf(VerticalFitPositionItems, VerticalFitPosition) < 0) {
			MessageEx.ShowErrorDialog("ウィンドウ配置は左右と上下の組み合わせから選択してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (Limit <= 0) {
			MessageEx.ShowErrorDialog("取得件数上限は 1 以上の数値で入力してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}

		cancellationToken.ThrowIfCancellationRequested();
		var originalRuntimeUrl = _originalUrl;
		var originalRuntimeLoginId = _originalLoginId;
		var originalRuntimeLoginPassword = _originalLoginPassword;
		var originalRuntimeWeatherRegion = _originalWeatherRegion;
		var originalRuntimeFitPosition = _originalFitPosition;
		var originalRuntimeLimit = _originalLimit;
		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		var loginIdChanged = !string.Equals(_originalLoginId, LoginId, StringComparison.Ordinal);
		var loginPasswordChanged = !string.Equals(_originalLoginPassword, LoginPassword, StringComparison.Ordinal);
		var persistedLoginId = string.IsNullOrWhiteSpace(LoginId) ? _originalLoginId : LoginId;
		var persistedLoginPassword = string.IsNullOrWhiteSpace(LoginPassword) ? _originalLoginPassword : LoginPassword;
		var fitPosition = FitPosition;
		var runtimeOverrides = new Dictionary<string, string?> {
			["ConnectionStrings:Url"] = Url,
			["Application:LoginId"] = persistedLoginId,
			["Application:LoginPass"] = persistedLoginPassword,
			["Application:WeatherRegion"] = normalizedWeatherRegion,
			["Application:FitPosition"] = fitPosition,
			["Application:Limit"] = Limit.ToString(CultureInfo.InvariantCulture),
		};
		var overrides = new Dictionary<string, object?> {
			["Application:WeatherRegion"] = normalizedWeatherRegion,
			["Application:FitPosition"] = fitPosition,
			["Application:Limit"] = Limit,
		};
		if (urlChanged) {
			overrides["ConnectionStrings:Url"] = Url;
		}
		if (loginIdChanged && !string.IsNullOrWhiteSpace(LoginId)) {
			overrides["Application:LoginId"] = LoginId;
		}
		if (loginPasswordChanged && !string.IsNullOrWhiteSpace(LoginPassword)) {
			overrides["Application:LoginPass"] = LoginPassword;
		}

		if (isSavel) {
			try {
				_store.SaveConfigurationValues(overrides);
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
				AppGlobal.UpdateConfigValues(originalRuntimeUrl, originalRuntimeLoginId, originalRuntimeLoginPassword, originalRuntimeWeatherRegion, originalRuntimeFitPosition, originalRuntimeLimit);
				return false;
			}
		}
		else {
			AppGlobal.UpdateConfigValues(Url, persistedLoginId, persistedLoginPassword, normalizedWeatherRegion, fitPosition, Limit);
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
		_currentSettings.Application.LoginId = persistedLoginId;
		_currentSettings.Application.LoginPass = persistedLoginPassword;
		_currentSettings.Application.WeatherRegion = normalizedWeatherRegion;
		_currentSettings.Application.FitPosition = fitPosition;
		_currentSettings.Application.Limit = Limit;
		LoginId = persistedLoginId;
		LoginPassword = persistedLoginPassword;
		WeatherRegion = normalizedWeatherRegion;
		_originalWeatherRegion = normalizedWeatherRegion;
		_originalFitPosition = fitPosition;
		_originalLimit = Limit;
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
		LoginId = AppGlobal.Application.LoginId;
		LoginPassword = AppGlobal.Application.LoginPass;
		WeatherRegion = string.IsNullOrWhiteSpace(AppGlobal.WeatherRegion) ? DefaultWeatherRegion : AppGlobal.WeatherRegion;
		ApplyFitPosition(AppGlobal.FitPosition);
		Limit = AppGlobal.Limit > 0 ? AppGlobal.Limit : DefaultLimit;
		_originalUrl = Url;
		_originalLoginId = LoginId;
		_originalLoginPassword = LoginPassword;
		_originalWeatherRegion = WeatherRegion;
		_originalFitPosition = FitPosition;
		_originalLimit = Limit;
	}

	private void ApplyFitPosition(string? fitPosition) {
		var value = fitPosition ?? string.Empty;
		HorizontalFitPosition = value.Contains("Right", StringComparison.OrdinalIgnoreCase)
			? "Right"
			: DefaultHorizontalFitPosition;
		VerticalFitPosition = value.Contains("Top", StringComparison.OrdinalIgnoreCase)
			? "Top"
			: DefaultVerticalFitPosition;
	}

}
