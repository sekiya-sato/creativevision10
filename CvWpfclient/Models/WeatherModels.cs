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

	public DateTime SunRize { get; init; } = DateTime.MinValue;
	public DateTime SunSet { get; init; } = DateTime.MinValue;

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

