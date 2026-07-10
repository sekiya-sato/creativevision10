/*
# description
JwtAuthorizationHandler は gRPC クライアントの HTTP パイプラインに参加し、認証ヘッダーを既存の CallContext 側へ委譲する空の中継ハンドラーです。

# example
var handler = new JwtAuthorizationHandler();
 */
using System.Net.Http;
namespace CvWpfclient.Helpers;

/// <summary>
/// gRPC クライアントの HTTP パイプラインへ参加するハンドラー。
/// 認証系ヘッダーは AppGlobal.GetDefaultCallContext() 側を正とするため、ここでは付与しない。
/// </summary>
internal sealed class JwtAuthorizationHandler : DelegatingHandler {
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		return base.SendAsync(request, cancellationToken);
	}
}
