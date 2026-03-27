using System.Net.Http;
namespace Cvnet10Wpfclient.Helpers;

/// <summary>
/// gRPC クライアントの HTTP パイプラインへ参加するハンドラー。
/// 認証系ヘッダーは AppGlobal.GetDefaultCallContext() 側を正とするため、ここでは付与しない。
/// </summary>
internal sealed class JwtAuthorizationHandler : DelegatingHandler {
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		return base.SendAsync(request, cancellationToken);
	}
}
