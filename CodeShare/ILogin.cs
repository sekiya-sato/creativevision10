using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

/// <summary>
/// ログインリクエスト
/// </summary>
[DataContract]
public sealed class LoginRequest {
	[DataMember(Order = 1)]
	public required string Name { get; set; }
	[DataMember(Order = 2)]
	public required string LoginId { get; set; }
	[DataMember(Order = 3)]
	public required string CryptPassword { get; set; }
	[DataMember(Order = 4)]
	public required DateTime LoginDate { get; set; }
	[DataMember(Order = 5)]
	public string Info { get; set; } = string.Empty;
}
/// <summary>
/// ログインリプライ
/// </summary>
[DataContract]
public sealed class LoginReply {
	/// <summary>
	/// JSON Web Token Message
	/// </summary>
	[DataMember(Order = 1)]
	public required string JwtMessage { get; set; }
	/// <summary>
	/// 成功=0 失敗=-1
	/// </summary>
	[DataMember(Order = 2)]
	public int Result { get; set; }

	/// <summary>
	/// 新しいJWTの有効期限(LocalTime)
	/// [Expiration time of the new JWT (LocalTime)]
	/// </summary>
	[DataMember(Order = 3)]
	public DateTime Expire { get; set; }
	/// <summary>
	/// クライアントに付与するInfoApiKey情報
	/// </summary>
	[DataMember(Order = 4)]
	public string InfoPayload { get; set; } = string.Empty;
}
/// <summary>
/// ログインリフレッシュ
/// </summary>
[DataContract]
public sealed class LoginRefresh {
	/// <summary>
	/// 認証済みのトークン
	/// [Authenticated token]
	/// </summary>
	[DataMember(Order = 1)]
	public required string Token { get; set; }
	[DataMember(Order = 2)]
	public string Info { get; set; } = string.Empty;
}

/// <summary>
/// Contract:gRPC公開サービス
/// [Contract: gRPC Public Service]
/// </summary>
[ServiceContract]
public interface ILoginService {
	[OperationContract]
	Task<LoginReply> LoginAsync(LoginRequest UserRequest, CallContext context = default);

	[OperationContract]
	Task<LoginReply> LoginRefleshAsync(LoginRefresh UserRequest, CallContext context = default);
	[OperationContract]
	Task<LoginReply> CreateLoginAsync(LoginRequest UserRequest, CallContext context = default);
}
