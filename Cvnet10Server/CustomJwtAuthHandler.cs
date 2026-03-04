/* ============================================================================
 * Cvnet10Server.dll : CustomJwtAuthHandler.cs
 * Created by Sekiya.Sato 2024/08/16
 * 説明: Jwt認証のためのハンドラ
 * [Description: Handler for JWT authentication]
 * 使用ライブラリ [Library used]:
 *		Microsoft.AspNetCore.Authentication.JwtBearer : LICENCE = MIT
 * ============================================================================  */

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.Encodings.Web;


namespace Cvnet10Server.Handlers;
/// <summary>
/// Jwt認証のためのハンドラ
/// [Handler for JWT authentication]
/// </summary>
public class CustomJwtAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
	private readonly TokenValidationParameters _tokenValidationParameters;

	public CustomJwtAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
		UrlEncoder encoder, IOptions<JwtBearerOptions> jwtOptions) : base(options, logger, encoder) {
		_tokenValidationParameters = jwtOptions.Value.TokenValidationParameters;
	}
	/// <summary>
	/// Jwtの検証
	/// [JWT verification]
	/// </summary>
	/// <returns></returns>
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
		/* HandleAuthenticateAsync() は必ず実行され、 
		 * Authorize属性あり : 成功時は成功、失敗時は失敗 Grpcエラー StatusCode="Unauthenticated", Detail="Bad gRPC response. HTTP status code: 401"
		 * Authorize属性なし : AuthenticateResult.Fail でも処理継続
		 * 
		 * [HandleAuthenticateAsync() is always executed,
		 *  Authorize attribute (with): succeeds on success, fails with a gRPC error on failure Grpcエラー 
		 *                              StatusCode="Unauthenticated", Detail="Bad gRPC response. HTTP status code: 401"
		 *  Authorize attribute (without): continues processing even with AuthenticateResult.Fail]                            
		 */
		const string bearer = "Bearer ";
		if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
			return AuthenticateResult.Fail("Authorization header not found.");

		if (authHeader.Count == 0)
			return AuthenticateResult.Fail("Invalid Authorization header format.");
		string authstr = authHeader[0] ?? " ";
		if (authstr.Length < bearer.Length || !authstr.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
			return AuthenticateResult.Fail("Invalid Authorization header format.");
		var token = authstr.Substring(bearer.Length).Trim();

		var tokenHandler = new JsonWebTokenHandler();
		try {
			var validationResult = await tokenHandler.ValidateTokenAsync(token, _tokenValidationParameters);
			if (!validationResult.IsValid || validationResult.ClaimsIdentity == null)
				return AuthenticateResult.Fail("Invalid token.");

			var ticket = new AuthenticationTicket(new ClaimsPrincipal(validationResult.ClaimsIdentity), Scheme.Name);
			return AuthenticateResult.Success(ticket);
		}
		catch (Exception ex) {
			Logger.LogError(ex, "Token validation failed.");
			return AuthenticateResult.Fail("Invalid token.");
		}
	}
}
