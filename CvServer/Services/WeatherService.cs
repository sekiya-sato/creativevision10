using CodeShare;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http.Headers;
using System.Text.Json;


namespace CvServer.Services;

public partial class WeatherService : IWeatherService {
	private readonly ILogger<WeatherService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
	private static readonly HttpClient httpClient = new();
	public WeatherService(ILogger<WeatherService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_httpContextAccessor = httpContextAccessor;
		var verInfo = new AppGlobal().VerInfo;
		httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(verInfo.Product, verInfo.Version));
	}


	[AllowAnonymous]
	public async Task<WeatherInfo?> GetCurrentWeatherAsync(string region, CancellationToken ct = default) {
		try {
			var url = $"https://api.openweathermap.org/data/2.5/weather?q={region}&appid={GetApiKey()}&units=metric&lang=ja";
			var json = await httpClient.GetFromJsonAsync<JsonElement>(url, _jsonOptions, ct);
			return ParseCurrentWeather(json);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "天気情報の取得に失敗");
			return null;
		}
	}

	[AllowAnonymous]
	public async Task<List<HourlyForecast>> GetHourlyForecastAsync(string region, CancellationToken ct = default) {
		try {
			var url = $"https://api.openweathermap.org/data/2.5/forecast?q={region}&appid={GetApiKey()}&units=metric&lang=ja&cnt=16"; // 3時間ごと16件（48時間分）取得
			var json = await httpClient.GetFromJsonAsync<JsonElement>(url, _jsonOptions, ct);
			return ParseForecast(json);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "天気予報の取得に失敗");
			return [];
		}
	}

	private string GetApiKey() {
		return _configuration["Application:OpenWeatherApiKey"] ?? "";
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
			IconKind = MapToMaterialIcon(iconCode),
			SunRize = DateTimeOffset.FromUnixTimeSeconds(json.GetProperty("sys").GetProperty("sunrise").GetInt64()).LocalDateTime,
			SunSet = DateTimeOffset.FromUnixTimeSeconds(json.GetProperty("sys").GetProperty("sunset").GetInt64()).LocalDateTime
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
				TimeLabel = dt.ToString("d日H時")
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
