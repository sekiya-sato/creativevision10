using Microsoft.AspNetCore.Http;

namespace CvServer;

/// <summary>
/// クライアントとサーバーのログを結び付ける相関 ID を扱う。
/// </summary>
internal static class RequestCorrelation {
	public const string HeaderName = "X-CV-Correlation-ID";

	public static string Resolve(HttpContext context) {
		var candidate = context.Request.Headers[HeaderName].ToString();
		return Guid.TryParse(candidate, out var correlationId)
			? correlationId.ToString("D")
			: context.TraceIdentifier;
	}
}
