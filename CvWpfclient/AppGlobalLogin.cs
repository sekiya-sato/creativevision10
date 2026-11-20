using CodeShare;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace CvWpfclient;

/// <summary>
/// クライアントから見たログイン状態
/// </summary>
public enum EnumClientLoginState {
	/// <summary>JWTが無い、または読めない</summary>
	NotLoggedIn,
	/// <summary>JWTの有効期限(exp)を過ぎている</summary>
	Expired,
	/// <summary>有効期限内のJWTがある</summary>
	LoggedIn,
}

/// <summary>
/// ログイン状態とサーバの認証モード
/// </summary>
public static partial class AppGlobal {
	/// <summary>認証モード照会のタイムアウト</summary>
	static readonly TimeSpan AuthModeTimeout = TimeSpan.FromSeconds(3);
	static readonly object _authModeLock = new();
	static string? _authModeUrl;
	static bool? _serverAuthRequired;

	/// <summary>
	/// 現在の <see cref="LoginJwt"/> からログイン状態を判定する。署名は検証せず exp だけを見る。
	/// </summary>
	public static EnumClientLoginState GetLoginState() => GetLoginState(LoginJwt, DateTime.UtcNow);

	/// <summary>
	/// JWT文字列からログイン状態を判定する。exp が無いトークンは未ログインとして扱う。
	/// </summary>
	/// <param name="jwt">JWT文字列</param>
	/// <param name="utcNow">判定時刻(UTC)</param>
	public static EnumClientLoginState GetLoginState(string? jwt, DateTime utcNow) {
		if (string.IsNullOrWhiteSpace(jwt)) {
			return EnumClientLoginState.NotLoggedIn;
		}
		var handler = new JwtSecurityTokenHandler();
		if (!handler.CanReadToken(jwt)) {
			return EnumClientLoginState.NotLoggedIn;
		}
		JwtSecurityToken token;
		try {
			token = handler.ReadJwtToken(jwt);
		}
		catch (ArgumentException) {
			return EnumClientLoginState.NotLoggedIn;
		}
		// exp が無い場合 ValidTo は DateTime.MinValue になる
		if (token.Payload.Expiration == null) {
			return EnumClientLoginState.NotLoggedIn;
		}
		return token.ValidTo > utcNow ? EnumClientLoginState.LoggedIn : EnumClientLoginState.Expired;
	}

	/// <summary>
	/// 接続先サーバが業務APIの呼出しにログインを要求するかを返す。結果は接続先URLごとに保持する。
	/// 新APIの無い旧サーバ(Unimplemented)はログイン必須として保持する。
	/// 通信失敗時はログイン必須として返し、次回に再照会する。
	/// </summary>
	public static async Task<bool> GetServerAuthRequiredAsync(CancellationToken cancellationToken) {
		var url = Url;
		lock (_authModeLock) {
			if (_serverAuthRequired is bool cached && _authModeUrl == url) {
				return cached;
			}
		}
		bool? result;
		try {
			var loginService = GetGrpcService<ILoginService>();
			var reply = await loginService.GetAuthModeAsync(new AuthModeRequest(), GetDefaultCallContext(cancellationToken, AuthModeTimeout));
			result = reply.AuthRequired;
		}
		catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented) {
			_logger?.LogInformation("認証モード照会APIが無いサーバのためログイン必須として扱う Url={Url}", url);
			result = true;
		}
		catch (RpcException ex) {
			_logger?.LogWarning(ex, "認証モードの照会に失敗 Url={Url}", url);
			return true;
		}
		lock (_authModeLock) {
			_authModeUrl = url;
			_serverAuthRequired = result;
		}
		return result.Value;
	}

	/// <summary>
	/// 保持している認証モードを破棄する（接続先変更時）
	/// </summary>
	public static void ClearServerAuthMode() {
		lock (_authModeLock) {
			_authModeUrl = null;
			_serverAuthRequired = null;
		}
	}
}
