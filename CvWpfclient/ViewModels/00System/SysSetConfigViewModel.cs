using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using CvWpfclient.Services;
using System.Globalization;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSetConfigViewModel : Helpers.BaseViewModel {
	private const string DefaultWeatherRegion = "Tokyo";
	private const string DefaultJmaWeatherAreaCode = "130000";
	private const string DefaultHorizontalFitPosition = "Left";
	private const string DefaultVerticalFitPosition = "Bottom";
	private const int DefaultLimit = 400;
	private ClientSettingsStore _store = new();
	private ClientSettingsDocument _currentSettings = new();
	private string _originalUrl = string.Empty;
	private string _originalLoginId = string.Empty;
	private string _originalLoginPassword = string.Empty;
	private string _originalWeatherRegion = DefaultWeatherRegion;
	private string _originalJmaWeatherAreaCode = DefaultJmaWeatherAreaCode;
	private string _originalFitPosition = $"{DefaultHorizontalFitPosition}-{DefaultVerticalFitPosition}";
	private int _originalLimit = DefaultLimit;
	private bool _originalDebugMode = false;

	public string[] HorizontalFitPositionItems { get; } = [DefaultHorizontalFitPosition, "Right"];
	public string[] VerticalFitPositionItems { get; } = ["Top", DefaultVerticalFitPosition];
	// 気象庁予報区の一覧（地域 / 地方 / コード）※気象庁の予報区コードが変更された場合は、ここを修正する必要があります。
	public JmaWeatherAreaOption[] JmaWeatherAreaItems { get; } = [
		new("北海道", "宗谷地方", "011000"),
		new("北海道", "上川・留萌地方", "012000"),
		new("北海道", "石狩・空知・後志地方", "016000"),
		new("北海道", "網走・北見・紋別地方", "013000"),
		new("北海道", "釧路・根室地方", "014100"),
		new("北海道", "胆振・日高地方", "015000"),
		new("北海道", "渡島・檜山地方", "017000"),
		new("東北", "青森県", "020000"),
		new("東北", "秋田県", "050000"),
		new("東北", "岩手県", "030000"),
		new("東北", "宮城県", "040000"),
		new("東北", "山形県", "060000"),
		new("東北", "福島県", "070000"),
		new("関東甲信", "茨城県", "080000"),
		new("関東甲信", "栃木県", "090000"),
		new("関東甲信", "群馬県", "100000"),
		new("関東甲信", "埼玉県", "110000"),
		new("関東甲信", "東京都", "130000"),
		new("関東甲信", "千葉県", "120000"),
		new("関東甲信", "神奈川県", "140000"),
		new("関東甲信", "長野県", "200000"),
		new("関東甲信", "山梨県", "190000"),
		new("東海", "静岡県", "220000"),
		new("東海", "愛知県", "230000"),
		new("東海", "岐阜県", "210000"),
		new("東海", "三重県", "240000"),
		new("北陸", "新潟県", "150000"),
		new("北陸", "富山県", "160000"),
		new("北陸", "石川県", "170000"),
		new("北陸", "福井県", "180000"),
		new("近畿", "滋賀県", "250000"),
		new("近畿", "京都府", "260000"),
		new("近畿", "大阪府", "270000"),
		new("近畿", "兵庫県", "280000"),
		new("近畿", "奈良県", "290000"),
		new("近畿", "和歌山県", "300000"),
		new("中国", "岡山県", "330000"),
		new("中国", "広島県", "340000"),
		new("中国", "島根県", "320000"),
		new("中国", "鳥取県", "310000"),
		new("四国", "徳島県", "360000"),
		new("四国", "香川県", "370000"),
		new("四国", "愛媛県", "380000"),
		new("四国", "高知県", "390000"),
		new("九州（山口含む）", "山口県", "350000"),
		new("九州（山口含む）", "福岡県", "400000"),
		new("九州（山口含む）", "大分県", "440000"),
		new("九州（山口含む）", "長崎県", "420000"),
		new("九州（山口含む）", "佐賀県", "410000"),
		new("九州（山口含む）", "熊本県", "430000"),
		new("九州（山口含む）", "宮崎県", "450000"),
		new("九州（山口含む）", "鹿児島県", "460100"),
		new("沖縄", "沖縄本島地方", "471000"),
		new("沖縄", "大東島地方", "472000"),
		new("沖縄", "宮古島地方", "473000"),
		new("沖縄", "八重山地方", "474000"),
	];

	[ObservableProperty]
	public partial string Url { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LoginId { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LoginPassword { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string WeatherRegion { get; set; } = DefaultWeatherRegion;

	[ObservableProperty]
	public partial string JmaWeatherAreaCode { get; set; } = DefaultJmaWeatherAreaCode;

	[ObservableProperty]
	public partial string HorizontalFitPosition { get; set; } = DefaultHorizontalFitPosition;

	[ObservableProperty]
	public partial string VerticalFitPosition { get; set; } = DefaultVerticalFitPosition;

	[ObservableProperty]
	public partial int Limit { get; set; } = DefaultLimit;

	[ObservableProperty]
	public partial bool DebugMode { get; set; } = false;

	public string DebugModeDisplayText => DebugMode ? "有効" : "無効";

	public string FitPosition => $"{HorizontalFitPosition}-{VerticalFitPosition}";

	partial void OnHorizontalFitPositionChanged(string value) {
		OnPropertyChanged(nameof(FitPosition));
	}

	partial void OnVerticalFitPositionChanged(string value) {
		OnPropertyChanged(nameof(FitPosition));
	}

	partial void OnDebugModeChanged(bool value) {
		OnPropertyChanged(nameof(DebugModeDisplayText));
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
		var normalizedJmaWeatherAreaCode = NormalizeJmaWeatherAreaCode(JmaWeatherAreaCode);
		if (string.IsNullOrWhiteSpace(normalizedJmaWeatherAreaCode) || !IsKnownJmaWeatherAreaCode(normalizedJmaWeatherAreaCode)) {
			MessageEx.ShowErrorDialog("気象庁予報区は一覧から選択してください。", owner: ClientLib.GetActiveView(this));
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
		var originalRuntimeJmaWeatherAreaCode = _originalJmaWeatherAreaCode;
		var originalRuntimeFitPosition = _originalFitPosition;
		var originalRuntimeLimit = _originalLimit;
		var originalRuntimeDebugMode = _originalDebugMode;
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
			["Application:JmaWeatherAreaCode"] = normalizedJmaWeatherAreaCode,
			["Application:FitPosition"] = fitPosition,
			["Application:Limit"] = Limit.ToString(CultureInfo.InvariantCulture),
			["Application:DebugMode"] = DebugMode ? "true" : "false",
		};
		var overrides = new Dictionary<string, object?> {
			["Application:WeatherRegion"] = normalizedWeatherRegion,
			["Application:JmaWeatherAreaCode"] = normalizedJmaWeatherAreaCode,
			["Application:FitPosition"] = fitPosition,
			["Application:Limit"] = Limit,
			["Application:DebugMode"] = DebugMode,
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
				AppGlobal.UpdateConfigValues(originalRuntimeUrl, originalRuntimeLoginId, originalRuntimeLoginPassword, originalRuntimeWeatherRegion, originalRuntimeFitPosition, originalRuntimeLimit, originalRuntimeJmaWeatherAreaCode, originalRuntimeDebugMode);
				return false;
			}
		}
		else {
			AppGlobal.UpdateConfigValues(Url, persistedLoginId, persistedLoginPassword, normalizedWeatherRegion, fitPosition, Limit, normalizedJmaWeatherAreaCode, DebugMode);
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
		_currentSettings.Application.JmaWeatherAreaCode = normalizedJmaWeatherAreaCode;
		_currentSettings.Application.FitPosition = fitPosition;
		_currentSettings.Application.Limit = Limit;
		_currentSettings.Application.DebugMode = DebugMode;
		LoginId = persistedLoginId;
		LoginPassword = persistedLoginPassword;
		WeatherRegion = normalizedWeatherRegion;
		JmaWeatherAreaCode = normalizedJmaWeatherAreaCode;
		_originalWeatherRegion = normalizedWeatherRegion;
		_originalJmaWeatherAreaCode = normalizedJmaWeatherAreaCode;
		_originalFitPosition = fitPosition;
		_originalLimit = Limit;
		_originalDebugMode = DebugMode;
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
		JmaWeatherAreaCode = NormalizeKnownJmaWeatherAreaCode(AppGlobal.JmaWeatherAreaCode);
		ApplyFitPosition(AppGlobal.FitPosition);
		Limit = AppGlobal.Limit > 0 ? AppGlobal.Limit : DefaultLimit;
		DebugMode = AppGlobal.DebugMode;
		_originalUrl = Url;
		_originalLoginId = LoginId;
		_originalLoginPassword = LoginPassword;
		_originalWeatherRegion = WeatherRegion;
		_originalJmaWeatherAreaCode = JmaWeatherAreaCode;
		_originalFitPosition = FitPosition;
		_originalLimit = Limit;
		_originalDebugMode = DebugMode;
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

	private string NormalizeJmaWeatherAreaCode(string? areaCode) => string.IsNullOrWhiteSpace(areaCode)
		? DefaultJmaWeatherAreaCode
		: areaCode.Trim();

	private string NormalizeKnownJmaWeatherAreaCode(string? areaCode) {
		var normalized = NormalizeJmaWeatherAreaCode(areaCode);
		return IsKnownJmaWeatherAreaCode(normalized) ? normalized : DefaultJmaWeatherAreaCode;
	}

	private bool IsKnownJmaWeatherAreaCode(string areaCode) {
		return JmaWeatherAreaItems.Any(item => string.Equals(item.Code, areaCode, StringComparison.Ordinal));
	}

}

public sealed record JmaWeatherAreaOption(string Region, string Name, string Code) {
	public string DisplayName => $"{Region} / {Name} ({Code})";
}
