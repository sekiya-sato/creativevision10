using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;


[ServiceContract]
public interface IWeatherService {
	[OperationContract]
	Task<WeatherInfo?> GetCurrentWeatherAsync(string region, CancellationToken ct = default);
	[OperationContract]
	Task<List<HourlyForecast>> GetHourlyForecastAsync(string region, CancellationToken ct = default);
}


/// <summary>
/// OpenWeatherMap API から取得した現在の天気情報
/// </summary>
[DataContract]
public sealed record class WeatherInfo {
	/// <summary>地域名 (例: "Tokyo")</summary>
	[DataMember(Order = 1)]
	public string Location { get; init; } = string.Empty;

	/// <summary>現在気温 (℃)</summary>
	[DataMember(Order = 2)]
	public double Temperature { get; init; }

	/// <summary>天気の概要 (例: "Clear", "Rain")</summary>
	[DataMember(Order = 3)]
	public string Condition { get; init; } = string.Empty;

	/// <summary>天気の詳細説明 (例: "clear sky")</summary>
	[DataMember(Order = 4)]
	public string Description { get; init; } = string.Empty;

	/// <summary>天気アイコンURL (OpenWeatherMap)</summary>
	[DataMember(Order = 5)]
	public string IconUrl { get; init; } = string.Empty;

	/// <summary>湿度 (%)</summary>
	[DataMember(Order = 6)]
	public int Humidity { get; init; }

	/// <summary>風速 (m/s)</summary>
	[DataMember(Order = 7)]
	public double WindSpeed { get; init; }

	/// <summary>MaterialDesign PackIcon Kind 名</summary>
	[DataMember(Order = 8)]
	public string IconKind { get; init; } = "WeatherSunny";

	/// <summary>日の出時刻</summary>
	[DataMember(Order = 9)]
	public DateTime SunRize { get; init; } = DateTime.MinValue;

	/// <summary>日の入り時刻</summary>
	[DataMember(Order = 10)]
	public DateTime SunSet { get; init; } = DateTime.MinValue;

	/// <summary>時間別予報リスト</summary>
	[DataMember(Order = 11)]
	public List<HourlyForecast> HourlyForecasts { get; init; } = [];

	// デシリアライザ用のデフォルトコンストラクタ
	public WeatherInfo() { }
}

/// <summary>
/// 3時間ごとの気温予報データポイント
/// </summary>
[DataContract]
public sealed record class HourlyForecast {
	/// <summary>予報日時</summary>
	[DataMember(Order = 1)]
	public DateTime DateTime { get; init; }

	/// <summary>気温 (℃)</summary>
	[DataMember(Order = 2)]
	public double Temperature { get; init; }

	/// <summary>時刻ラベル (例: "15:00")</summary>
	[DataMember(Order = 3)]
	public string TimeLabel { get; init; } = string.Empty;

	// デシリアライザ用のデフォルトコンストラクタ
	public HourlyForecast() { }
}
