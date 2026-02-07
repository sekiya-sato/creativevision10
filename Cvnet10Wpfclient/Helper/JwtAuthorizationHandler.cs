using System.Net.Http;
using System.Net.Http.Headers;

namespace Cvnet10Wpfclient.Helper;

/// <summary>
/// すべての gRPC 呼び出しに JWT とクライアント ID を付与するハンドラー。
/// </summary>
internal sealed class JwtAuthorizationHandler : DelegatingHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var token = AppCurrent.LoginJwt;
        if (!string.IsNullOrWhiteSpace(token)) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!request.Headers.Contains("X-ClientId")) {
            request.Headers.Add("X-ClientId", AppCurrent.ClientId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
