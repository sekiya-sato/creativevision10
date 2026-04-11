using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CvWpfclient.Services;

public interface IPostalAddressService {
	Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default);
}

public sealed record PostalAddressSearchResult(
	bool IsSuccess,
	string NormalizedPostalCode,
	IReadOnlyList<PostalAddressItem> Items,
	string Message,
	PostalAddressErrorType ErrorType);

public sealed record PostalAddressItem(
	string PostalCode,
	string Address1,
	string Address2,
	string Address3,
	string FullAddress,
	string? Address1Kana,
	string? Address2Kana,
	string? Address3Kana);

public enum PostalAddressErrorType {
	None,
	InvalidInput,
	Unauthorized,
	Forbidden,
	NotFound,
	RateLimited,
	NetworkError,
	ServiceError,
}

public sealed class JapanPostBizOptions {
	public string BaseUrl { get; set; } = "https://api.da.pf.japanpost.jp";
	public string TokenPath { get; set; } = "/api/v2/j/token";
	public string SearchCodePath { get; set; } = "/api/v2/searchcode";
	public string ClientId { get; set; } = string.Empty;
	public string SecretKey { get; set; } = string.Empty;
	public string EcUid { get; set; } = string.Empty;
	public string UserAgent { get; set; } = "CvWpfclient/1.0";
	public int TimeoutSeconds { get; set; } = 10;
	public int DefaultLimit { get; set; } = 1000;
	public int DefaultChoikiType { get; set; } = 1;
	public int DefaultSearchType { get; set; } = 2;
	public int TokenRefreshMarginSeconds { get; set; } = 60;
}

public sealed class JapanPostBizPostalAddressService(
	HttpClient httpClient,
	IJapanPostBizTokenProvider tokenProvider,
	IConfiguration configuration,
	ILogger<JapanPostBizPostalAddressService> logger) : IPostalAddressService {
	private readonly JapanPostBizOptions _options = configuration.GetSection("JapanPostBiz").Get<JapanPostBizOptions>() ?? new();

	public async Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default) {
		var normalizedPostalCode = NormalizePostalCode(postalCode);
		if (normalizedPostalCode == null) {
			return new PostalAddressSearchResult(false, string.Empty, [], "郵便番号は7桁の数字で入力してください。", PostalAddressErrorType.InvalidInput);
		}

		try {
			using var response = await SendSearchRequestAsync(normalizedPostalCode, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
				tokenProvider.Invalidate();
				using var retriedResponse = await SendSearchRequestAsync(normalizedPostalCode, cancellationToken).ConfigureAwait(false);
				return await CreateResultAsync(retriedResponse, normalizedPostalCode, cancellationToken).ConfigureAwait(false);
			}

			return await CreateResultAsync(response, normalizedPostalCode, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException ex) {
			logger.LogWarning(ex, "郵便番号検索で通信エラーが発生しました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索に失敗しました。ネットワーク接続を確認してください。", PostalAddressErrorType.NetworkError);
		}
		catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			logger.LogWarning(ex, "郵便番号検索がタイムアウトしました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索がタイムアウトしました。しばらくしてから再実行してください。", PostalAddressErrorType.NetworkError);
		}
		catch (Exception ex) {
			logger.LogError(ex, "郵便番号検索で想定外のエラーが発生しました。 postalCode={PostalCode}", normalizedPostalCode);
			return new PostalAddressSearchResult(false, normalizedPostalCode, [], "郵便番号検索中にエラーが発生しました。", PostalAddressErrorType.ServiceError);
		}
	}

	private async Task<HttpResponseMessage> SendSearchRequestAsync(string normalizedPostalCode, CancellationToken cancellationToken) {
		var authorization = await tokenProvider.GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
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
			address.ZipCode ?? string.Empty,
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
		logger.LogWarning("郵便番号検索APIエラー。 status={StatusCode} body={Body}", (int)response.StatusCode, responseText);

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
		var path = _options.SearchCodePath.TrimEnd('/');
		var limit = Math.Clamp(_options.DefaultLimit, 1, 1000);
		var query = new List<string> {
			"page=1",
			$"limit={limit}",
		};

		// 7桁郵便番号の通常検索では必須パラメータを優先し、任意パラメータは最小限に抑える。
		if (!string.IsNullOrWhiteSpace(_options.EcUid)) {
			query.Add($"ec_uid={Uri.EscapeDataString(_options.EcUid)}");
		}

		return $"{path}/{Uri.EscapeDataString(normalizedPostalCode)}?{string.Join("&", query)}";
	}

	private static string? NormalizePostalCode(string postalCode) {
		var normalized = new string((postalCode ?? string.Empty).Where(char.IsDigit).ToArray());
		return normalized.Length == 7 ? normalized : null;
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
}
