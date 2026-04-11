using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CvWpfclient.Services;

public interface IJapanPostBizTokenProvider {
	Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken = default);
	void Invalidate();
}

public sealed class JapanPostBizTokenProvider(
	HttpClient httpClient,
	IConfiguration configuration,
	ILogger<JapanPostBizTokenProvider> logger) : IJapanPostBizTokenProvider, IDisposable {
	private readonly JapanPostBizOptions _options = configuration.GetSection("JapanPostBiz").Get<JapanPostBizOptions>() ?? new();
	private readonly SemaphoreSlim _lock = new(1, 1);
	private string? _cachedToken;
	private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

	public async Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken = default) {
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (IsTokenValid()) {
				return new AuthenticationHeaderValue("Bearer", _cachedToken);
			}

			var tokenResponse = await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
			_cachedToken = tokenResponse.Token;
			_expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn - _options.TokenRefreshMarginSeconds));

			return new AuthenticationHeaderValue("Bearer", _cachedToken);
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

	private async Task<JapanPostTokenResponse> RequestTokenAsync(CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.SecretKey)) {
			throw new InvalidOperationException("JapanPostBiz の ClientId または SecretKey が設定されていません。");
		}

		var request = new JapanPostTokenRequest("client_credentials", _options.ClientId, _options.SecretKey);
		using var response = await httpClient.PostAsJsonAsync(_options.TokenPath, request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) {
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			logger.LogWarning("日本郵便 token API エラー。 status={StatusCode} body={Body}", (int)response.StatusCode, body);
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
