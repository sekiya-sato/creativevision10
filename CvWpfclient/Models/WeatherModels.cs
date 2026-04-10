namespace CvWpfclient.Models;

/// <summary>
/// OpenWeatherMap API から取得した現在の天気情報
/// </summary>
public sealed class WeatherInfo {
	/// <summary>地域名 (例: "Tokyo")</summary>
	public string Location { get; init; } = string.Empty;

	/// <summary>現在気温 (℃)</summary>
	public double Temperature { get; init; }

	/// <summary>天気の概要 (例: "Clear", "Rain")</summary>
	public string Condition { get; init; } = string.Empty;

	/// <summary>天気の詳細説明 (例: "clear sky")</summary>
	public string Description { get; init; } = string.Empty;

	/// <summary>天気アイコンURL (OpenWeatherMap)</summary>
	public string IconUrl { get; init; } = string.Empty;

	/// <summary>湿度 (%)</summary>
	public int Humidity { get; init; }

	/// <summary>風速 (m/s)</summary>
	public double WindSpeed { get; init; }

	/// <summary>MaterialDesign PackIcon Kind 名</summary>
	public string IconKind { get; init; } = "WeatherSunny";
}

/// <summary>
/// 3時間ごとの気温予報データポイント
/// </summary>
public sealed class HourlyForecast {
	/// <summary>予報日時</summary>
	public DateTime DateTime { get; init; }

	/// <summary>気温 (℃)</summary>
	public double Temperature { get; init; }

	/// <summary>時刻ラベル (例: "15:00")</summary>
	public string TimeLabel { get; init; } = string.Empty;
}

/// <summary>
/// Googleカレンダーのイベント情報
/// </summary>
public sealed class CalendarEventItem {
	/// <summary>イベントタイトル</summary>
	public string Summary { get; init; } = string.Empty;

	/// <summary>開始日時</summary>
	public DateTime StartTime { get; init; }

	/// <summary>終了日時</summary>
	public DateTime EndTime { get; init; }

	/// <summary>場所</summary>
	public string Location { get; init; } = string.Empty;

	/// <summary>終日イベントかどうか</summary>
	public bool IsAllDay { get; init; }

	/// <summary>表示用の時刻文字列</summary>
	public string TimeDisplay =>
		IsAllDay ? "終日" : $"{StartTime:HH:mm} - {EndTime:HH:mm}";
}
