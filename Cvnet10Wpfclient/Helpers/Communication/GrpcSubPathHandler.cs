using System.Net.Http;

namespace Cvnet10Wpfclient.Helpers;

/// <summary>
/// gRPCサーバ側でサブパスが必要な場合に、クライアント側のリクエストURIを適切に書き換えるためのハンドラー
/// </summary>
public sealed class GrpcSubPathHandler : DelegatingHandler {
	private readonly string _subPath;

	public GrpcSubPathHandler(string subPath) {
		_subPath = "/" + subPath.Trim('/');
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {

		var uri = request.RequestUri!;
		var builder = new UriBuilder(uri);
		var originalPath = uri.AbsolutePath;

		// パスの連結
		builder.Path = _subPath + originalPath;
		request.RequestUri = builder.Uri;

		//System.Diagnostics.Debug.WriteLine($"SubPathHandler: Rewritten URI = {request.RequestUri}");

		return await base.SendAsync(request, cancellationToken);
	}
}
