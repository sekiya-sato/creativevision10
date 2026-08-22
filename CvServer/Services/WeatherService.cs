using CodeShare;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;
using System.Net.Http.Headers;
using System.Text.Json;


namespace CvServer.Services;

public partial class WeatherService : IWeatherService {
	private readonly ILogger<WeatherService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
	private static readonly HttpClient httpClient = CreateHttpClient();
	private const int DefaultForecastCount = 40;
	private const int MaxForecastCount = 40;
	public WeatherService(ILogger<WeatherService> logger, IConfiguration configuration, IWebHostEnvironment env,
		IHttpContextAccessor httpContextAccessor, AppGlobal? appGlobal = null) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_httpContextAccessor = httpContextAccessor;
		if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0) {
			var verInfo = (appGlobal ?? AppGlobal.Shared).VerInfo;
			httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(verInfo.Product, verInfo.Version));
		}
	}

	private static HttpClient CreateHttpClient() {
		var handler = new SocketsHttpHandler {
			// OpenWeatherMap の接続先 DNS 変更を長期稼働中にも反映する。
			PooledConnectionLifetime = TimeSpan.FromMinutes(15),
		};
		return new HttpClient(handler);
	}


	[AllowAnonymous]
	public async Task<WeatherInfo?> GetCurrentWeatherAsync(string region, CallContext context = default) {
		try {
			var ct = context.CancellationToken;
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
	public async Task<List<HourlyForecast>> GetHourlyForecastAsync(string region, CallContext context = default) {
		try {
			var ct = context.CancellationToken;
			var count = GetForecastCount();
			var url = $"https://api.openweathermap.org/data/2.5/forecast?q={region}&appid={GetApiKey()}&units=metric&lang=ja&cnt={count}"; // 3時間ごと count 件取得
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

	private int GetForecastCount() {
		var value = _configuration["Application:OpenWeatherCount"];
		if (!int.TryParse(value, out var count) || count <= 0) {
			count = DefaultForecastCount;
		}
		return Math.Clamp(count, 1, MaxForecastCount);
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
			Sunrise = DateTimeOffset.FromUnixTimeSeconds(json.GetProperty("sys").GetProperty("sunrise").GetInt64()).LocalDateTime,
			Sunset = DateTimeOffset.FromUnixTimeSeconds(json.GetProperty("sys").GetProperty("sunset").GetInt64()).LocalDateTime
		};
	}

	private static List<HourlyForecast> ParseForecast(JsonElement json) {
		var list = json.GetProperty("list");
		var forecasts = new List<HourlyForecast>();
		foreach (var item in list.EnumerateArray()) {
			var dt = DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("dt").GetInt64()).LocalDateTime;
			var precipitationMm = item.TryGetProperty("rain", out var rain)
				&& rain.TryGetProperty("3h", out var precipitation)
				? Math.Max(0, precipitation.GetDouble())
				: 0;
			forecasts.Add(new HourlyForecast {
				DateTime = dt,
				Temperature = item.GetProperty("main").GetProperty("temp").GetDouble(),
				TimeLabel = dt.ToString("d日H時"),
				PrecipitationMm = precipitationMm,
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
