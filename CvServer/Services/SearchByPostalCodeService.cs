using CodeShare;
using Microsoft.AspNetCore.Authorization;


namespace CvServer.Services;

public partial class SearchByPostalCodeService : IPostalAddressService {
	private readonly ILogger<SearchByPostalCodeService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly IHttpContextAccessor _httpContextAccessor;
	public SearchByPostalCodeService(ILogger<SearchByPostalCodeService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor) {
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
	public Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default) {
		throw new NotImplementedException();
	}
}
