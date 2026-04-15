using CodeShare;
using Microsoft.AspNetCore.Authorization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;


namespace CvServer.Services;

public partial class SearchByPostalCodeService : IPostalAddressService, IDisposable {
	private readonly ILogger<SearchByPostalCodeService> _logger;
	private readonly IConfiguration _configuration;
	private static readonly HttpClient httpClient = new();
	private static JapanPostBizOptions? _japanPostBizOptions;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private string? _cachedToken;
	private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;
	public SearchByPostalCodeService(ILogger<SearchByPostalCodeService> logger, IConfiguration configuration) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);

		_logger = logger;
		_configuration = configuration;
		var verInfo = new AppGlobal().VerInfo;
		if (httpClient != null && httpClient.DefaultRequestHeaders.Count() == 0) { // UserAgentは必ず設定する。API側でUserAgentがないリクエストを拒否する可能性があるため。
			httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(verInfo.Product, verInfo.Version));
			_japanPostBizOptions = GetJapanPostBizOptions();
			httpClient.BaseAddress = new Uri(_japanPostBizOptions.BaseUrl);
		}
	}

	[AllowAnonymous]
	public async Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default) {
		var normalizedPostalCode = NormalizePostalCode(postalCode);
		if (normalizedPostalCode == null) {
			return new PostalAddressSearchResult(false, string.Empty, [], "郵便番号は7桁の数字で入力してください。", PostalAddressErrorType.InvalidInput);
		}

		try {
			using var response = await SendSearchRequestAsync(normalizedPostalCode, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
				Invalidate();
				using var retriedResponse = await SendSearchRequestAsync(normalizedPostalCode, cancellationToken).ConfigureAwait(false);
				return await CreateResultAsync(retriedResponse, normalizedPostalCode, cancellationToken).ConfigureAwait(false);
			}

			return await CreateResultAsync(response, normalizedPostalCode, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException ex) {
			_logger.LogWarning(ex, "郵便番号検索で通信エラーが発生しました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索に失敗しました。ネットワーク接続を確認してください。", PostalAddressErrorType.NetworkError);
		}
		catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			_logger.LogWarning(ex, "郵便番号検索がタイムアウトしました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索がタイムアウトしました。しばらくしてから再実行してください。", PostalAddressErrorType.NetworkError);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "郵便番号検索で想定外のエラーが発生しました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索中にエラーが発生しました。", PostalAddressErrorType.ServiceError);
		}
	}

	private async Task<HttpResponseMessage> SendSearchRequestAsync(string normalizedPostalCode, CancellationToken cancellationToken) {
		var authorization = await GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
		var url = BuildSearchUrl(normalizedPostalCode);
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Authorization = authorization;
		return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}

	private async Task<PostalAddressSearchResult> CreateResultAsync(HttpResponseMessage response, string normalizedPostalCode, CancellationToken cancellationToken) {
		if (!response.IsSuccessStatusCode) {
			return await CreateErrorResultAsync(response, normalizedPostalCode, cancellationToken).ConfigureAwait(false);
		}

		var body = await response.Content.ReadFromJsonAsync<JapanPostSearchCodeResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
		var items = body?.Addresses?.Select(address => new PostalAddressItem(
			HumanReadPostalCode(address.ZipCode) ?? string.Empty,
			address.PrefName ?? string.Empty,
			address.CityName ?? string.Empty,
			address.TownName ?? string.Empty,
			$"{address.PrefName}{address.CityName}{address.TownName}",
			address.PrefKana,
			address.CityKana,
			address.TownKana)).ToList() ?? [];

		if (items.Count == 0) {
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "該当する住所が見つかりません。", PostalAddressErrorType.NotFound);
		}

		return new PostalAddressSearchResult(true, normalizedPostalCode, items, string.Empty, PostalAddressErrorType.None);
	}

	private async Task<PostalAddressSearchResult> CreateErrorResultAsync(HttpResponseMessage response, string normalizedPostalCode, CancellationToken cancellationToken) {
		var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		_logger.LogWarning("郵便番号検索APIエラー。 status={StatusCode} body={Body}", (int)response.StatusCode, responseText);

		return response.StatusCode switch {
			HttpStatusCode.BadRequest => new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号の形式が不正です。", PostalAddressErrorType.InvalidInput),
			HttpStatusCode.Unauthorized => new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索の認証に失敗しました。", PostalAddressErrorType.Unauthorized),
			HttpStatusCode.Forbidden => new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索APIの利用が許可されていません。", PostalAddressErrorType.Forbidden),
			HttpStatusCode.NotFound => new PostalAddressSearchResult(false, normalizedPostalCode, [], "該当する住所が見つかりません。", PostalAddressErrorType.NotFound),
			(HttpStatusCode)429 => new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索APIの利用回数制限に達しました。", PostalAddressErrorType.RateLimited),
			_ => new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索APIの呼び出しに失敗しました。", PostalAddressErrorType.ServiceError),
		};
	}

	private string BuildSearchUrl(string normalizedPostalCode) {
		var path = _japanPostBizOptions?.SearchCodePath.TrimEnd('/');
		var limit = Math.Clamp(_japanPostBizOptions?.DefaultLimit ?? 1000, 1, 1000);
		var query = new List<string> {
			"page=1",
			$"limit={limit}",
		};

		// 7桁郵便番号の通常検索では必須パラメータを優先し、任意パラメータは最小限に抑える。
		if (!string.IsNullOrWhiteSpace(_japanPostBizOptions?.EcUid)) {
			query.Add($"ec_uid={Uri.EscapeDataString(_japanPostBizOptions.EcUid)}");
		}

		return $"{path}/{Uri.EscapeDataString(normalizedPostalCode)}?{string.Join("&", query)}";
	}

	private static string? NormalizePostalCode(string postalCode) {
		var normalized = new string((postalCode ?? string.Empty).Where(char.IsDigit).ToArray());
		return normalized.Length == 7 ? normalized : null;
	}
	private static string HumanReadPostalCode(string? postalCode) {
		if (string.IsNullOrWhiteSpace(postalCode)) {
			return string.Empty;
		}
		if (postalCode.Length == 7) {
			return $"{postalCode.Substring(0, 3)}-{postalCode.Substring(3, 4)}";
		}
		return postalCode;
	}

	private sealed record JapanPostSearchCodeResponse(
		[property: JsonPropertyName("addresses")] List<JapanPostAddressDto>? Addresses,
		[property: JsonPropertyName("level")] int Level,
		[property: JsonPropertyName("limit")] int Limit,
		[property: JsonPropertyName("count")] int Count,
		[property: JsonPropertyName("page")] int Page);

	private sealed record JapanPostAddressDto(
		[property: JsonPropertyName("zip_code")] string? ZipCode,
		[property: JsonPropertyName("pref_code")] string? PrefCode,
		[property: JsonPropertyName("pref_name")] string? PrefName,
		[property: JsonPropertyName("pref_kana")] string? PrefKana,
		[property: JsonPropertyName("pref_roma")] string? PrefRoma,
		[property: JsonPropertyName("city_code")] string? CityCode,
		[property: JsonPropertyName("city_name")] string? CityName,
		[property: JsonPropertyName("city_kana")] string? CityKana,
		[property: JsonPropertyName("city_roma")] string? CityRoma,
		[property: JsonPropertyName("town_name")] string? TownName,
		[property: JsonPropertyName("town_kana")] string? TownKana,
		[property: JsonPropertyName("town_roma")] string? TownRoma);



	public async Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken = default) {
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (IsTokenValid()) {
				return new AuthenticationHeaderValue("Bearer", _cachedToken);
			}
			var options = _japanPostBizOptions ?? GetJapanPostBizOptions();
			var tokenResponse = await RequestTokenAsync(options, cancellationToken).ConfigureAwait(false);
			_cachedToken = tokenResponse.Token;
			_expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn - options.TokenRefreshMarginSeconds));

			return new AuthenticationHeaderValue("Bearer", _cachedToken);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "日本郵便APIのトークン取得に失敗しました。");
			throw;
		}
		finally {
			_lock.Release();
		}
	}
	public void Invalidate() {
		_cachedToken = null;
		_expiresAtUtc = DateTimeOffset.MinValue;
	}
	private bool IsTokenValid() {
		return !string.IsNullOrWhiteSpace(_cachedToken) && DateTimeOffset.UtcNow < _expiresAtUtc;
	}
	public JapanPostBizOptions GetJapanPostBizOptions() {
		var verInfo = new AppGlobal().VerInfo;
		return new JapanPostBizOptions {
			BaseUrl = _configuration.GetSection("JapanPostBiz")["BaseUrl"] ?? "https://api.da.pf.japanpost.jp",
			TokenPath = _configuration.GetSection("JapanPostBiz")["TokenPath"] ?? "/api/v2/j/token",
			SearchCodePath = _configuration.GetSection("JapanPostBiz")["SearchCodePath"] ?? "/api/v2/searchcode",
			ClientId = _configuration.GetSection("JapanPostBiz")["ClientId"] ?? "",
			SecretKey = _configuration.GetSection("JapanPostBiz")["SecretKey"] ?? "",
			EcUid = _configuration.GetSection("JapanPostBiz")["EcUid"] ?? string.Empty,
			UserAgent = _configuration.GetSection("JapanPostBiz")["UserAgent"] ?? $"{verInfo.Product}/{verInfo.Version}",
			TimeoutSeconds = _configuration.GetValue<int?>("JapanPostBiz:TimeoutSeconds") ?? 10,
			DefaultLimit = _configuration.GetValue<int?>("JapanPostBiz:DefaultLimit") ?? 1000,
			DefaultChoikiType = _configuration.GetValue<int?>("JapanPostBiz:DefaultChoikiType") ?? 1,
			DefaultSearchType = _configuration.GetValue<int?>("JapanPostBiz:DefaultSearchType") ?? 2,
			TokenRefreshMarginSeconds = _configuration.GetValue<int?>("JapanPostBiz:TokenRefreshMarginSeconds") ?? 60,
		};
	}
	private async Task<JapanPostTokenResponse> RequestTokenAsync(JapanPostBizOptions options, CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.SecretKey)) {
			throw new InvalidOperationException("JapanPostBiz の ClientId または SecretKey が設定されていません。");
		}

		var request = new JapanPostTokenRequest("client_credentials", options.ClientId, options.SecretKey);
		using var response = await httpClient.PostAsJsonAsync(options.TokenPath, request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) {
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			_logger.LogWarning("日本郵便 token API エラー。 status={StatusCode} body={Body}", (int)response.StatusCode, body);
			throw new InvalidOperationException("日本郵便APIのトークン取得に失敗しました。");
		}

		var tokenResponse = await response.Content.ReadFromJsonAsync<JapanPostTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
		if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.Token)) {
			throw new InvalidOperationException("日本郵便APIのトークン応答が不正です。");
		}

		return tokenResponse;
	}

	public void Dispose() {
		_lock.Dispose();
	}

	private sealed record JapanPostTokenRequest(
		[property: JsonPropertyName("grant_type")] string GrantType,
		[property: JsonPropertyName("client_id")] string ClientId,
		[property: JsonPropertyName("secret_key")] string SecretKey);

	private sealed record JapanPostTokenResponse(
		[property: JsonPropertyName("scope")] string Scope,
		[property: JsonPropertyName("token_type")] string TokenType,
		[property: JsonPropertyName("expires_in")] int ExpiresIn,
		[property: JsonPropertyName("token")] string Token);
}
