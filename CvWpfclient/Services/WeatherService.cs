using CvWpfclient.Models;
using Microsoft.Extensions.Configuration;
using NLog;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace CvWpfclient.Services;

public interface IWeatherService {
	Task<WeatherInfo?> GetCurrentWeatherAsync(CancellationToken ct = default);
	Task<List<HourlyForecast>> GetHourlyForecastAsync(CancellationToken ct = default);
}

public sealed class WeatherService(HttpClient httpClient, IConfiguration config) : IWeatherService {
	private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
	private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

	private string Region => config["Application:WeatherRegion"] ?? "Tokyo";

	public async Task<WeatherInfo?> GetCurrentWeatherAsync(CancellationToken ct = default) {
		try {
			var url = $"https://api.openweathermap.org/data/2.5/weather?q={Region}&appid={GetApiKey()}&units=metric&lang=ja";
			var json = await httpClient.GetFromJsonAsync<JsonElement>(url, _jsonOptions, ct);
			return ParseCurrentWeather(json);
		}
		catch (Exception ex) {
			_logger.Warn(ex, "天気情報の取得に失敗");
			return null;
		}
	}

	public async Task<List<HourlyForecast>> GetHourlyForecastAsync(CancellationToken ct = default) {
		try {
			var url = $"https://api.openweathermap.org/data/2.5/forecast?q={Region}&appid={GetApiKey()}&units=metric&lang=ja&cnt=8";
			var json = await httpClient.GetFromJsonAsync<JsonElement>(url, _jsonOptions, ct);
			return ParseForecast(json);
		}
		catch (Exception ex) {
			_logger.Warn(ex, "天気予報の取得に失敗");
			return [];
		}
	}

	private string GetApiKey() {
		return config["Application:OpenWeatherApiKey"] ?? "";
	}

	private static WeatherInfo ParseCurrentWeather(JsonElement json) {
		var weather = json.GetProperty("weather")[0];
		var main = json.GetProperty("main");
		var wind = json.GetProperty("wind");
		var iconCode = weather.GetProperty("icon").GetString() ?? "01d";

		return new WeatherInfo {
			Location = json.GetProperty("name").GetString() ?? "",
			Temperature = main.GetProperty("temp").GetDouble(),
			Condition = weather.GetProperty("main").GetString() ?? "",
			Description = weather.GetProperty("description").GetString() ?? "",
			IconUrl = $"https://openweathermap.org/img/wn/{iconCode}@2x.png",
			Humidity = main.GetProperty("humidity").GetInt32(),
			WindSpeed = wind.GetProperty("speed").GetDouble(),
			IconKind = MapToMaterialIcon(iconCode)
		};
	}

	private static List<HourlyForecast> ParseForecast(JsonElement json) {
		var list = json.GetProperty("list");
		var forecasts = new List<HourlyForecast>();
		foreach (var item in list.EnumerateArray()) {
			var dt = DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("dt").GetInt64()).LocalDateTime;
			forecasts.Add(new HourlyForecast {
				DateTime = dt,
				Temperature = item.GetProperty("main").GetProperty("temp").GetDouble(),
				TimeLabel = dt.ToString("HH:mm")
			});
		}
		return forecasts;
	}

	// OpenWeatherMap icon code -> MaterialDesign PackIcon Kind
	private static string MapToMaterialIcon(string iconCode) => iconCode switch {
		"01d" => "WeatherSunny",
		"01n" => "WeatherNight",
		"02d" => "WeatherPartlyCloudy",
		"02n" => "WeatherNightPartlyCloudy",
		"03d" or "03n" => "Cloud",
		"04d" or "04n" => "CloudOutline",
		"09d" or "09n" => "WeatherRainy",
		"10d" => "WeatherPartlyRainy",
		"10n" => "WeatherRainy",
		"11d" or "11n" => "WeatherLightning",
		"13d" or "13n" => "WeatherSnowy",
		"50d" or "50n" => "WeatherFog",
		_ => "WeatherSunny"
	};
}
