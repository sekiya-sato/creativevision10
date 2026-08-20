using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace CvWpfclient.Helpers;

/// <summary>
/// gRPC 呼出へ相関 ID を付与し、HTTP 通信層の失敗を記録する。
/// </summary>
internal sealed class GrpcRequestLoggingHandler : DelegatingHandler {
	private const string CorrelationIdHeaderName = "X-CV-Correlation-ID";
	private readonly ILogger<GrpcRequestLoggingHandler> _logger;

	public GrpcRequestLoggingHandler(ILogger<GrpcRequestLoggingHandler> logger) {
		_logger = logger;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		var correlationId = Guid.NewGuid().ToString("D");
		request.Headers.TryAddWithoutValidation(CorrelationIdHeaderName, correlationId);

		try {
			var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				_logger.LogWarning(
					"gRPC HTTP 応答が失敗しました。 CorrelationId={CorrelationId} Method={Method} Path={Path} StatusCode={StatusCode}",
					correlationId, request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
			}
			return response;
		}
		catch (Exception ex) {
			_logger.LogError(ex,
				"gRPC HTTP 通信に失敗しました。 CorrelationId={CorrelationId} Method={Method} Path={Path}",
				correlationId, request.Method, request.RequestUri?.AbsolutePath);
			throw;
		}
	}
}
