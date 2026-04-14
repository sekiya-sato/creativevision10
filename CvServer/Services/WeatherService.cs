using CodeShare;
using Microsoft.AspNetCore.Authorization;


namespace CvServer.Services;

public partial class WeatherService : IWeatherService {
	private readonly ILogger<WeatherService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly IHttpContextAccessor _httpContextAccessor;
	public WeatherService(ILogger<WeatherService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_httpContextAccessor = httpContextAccessor;
	}

	[AllowAnonymous]
	public Task<WeatherInfo?> GetCurrentWeatherAsync(CancellationToken ct = default) {
		throw new NotImplementedException();
	}

	[AllowAnonymous]
	public Task<List<HourlyForecast>> GetHourlyForecastAsync(CancellationToken ct = default) {
		throw new NotImplementedException();
	}

}
