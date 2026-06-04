using ProtoBuf;
using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;
namespace CodeShare;

/// <summary>
/// 郵便番号検索サービスインターフェース 日本郵政のAPIを呼び出す
/// </summary>
[ServiceContract]
public interface IPostalAddressService {
	[OperationContract]
	Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CallContext context = default);
}

// 検索結果レコード
[DataContract]
[ProtoContract]
public sealed record PostalAddressSearchResult(
	[property: DataMember(Order = 1)]
	[property: ProtoMember(1)] bool IsSuccess,
	[property: DataMember(Order = 2)]
	[property: ProtoMember(2)] string NormalizedPostalCode,
	[property: DataMember(Order = 3)]
	[property: ProtoMember(3)] List<PostalAddressItem> Items,
	[property: DataMember(Order = 4)]
	[property: ProtoMember(4)] string Message,
	[property: DataMember(Order = 5)]
	[property: ProtoMember(5)] PostalAddressErrorType ErrorType
) {
	// デシリアライザ用のデフォルトコンストラクタを確保するため
	// 初期値を設定したコンストラクタを明示するか、プロパティを初期化します
	public PostalAddressSearchResult() : this(false, "", [], "", PostalAddressErrorType.None) { }
}

[DataContract]
[ProtoContract]
public sealed record PostalAddressItem(
	[property: DataMember(Order = 1)]
	[property: ProtoMember(1)] string PostalCode,
	[property: DataMember(Order = 2)]
	[property: ProtoMember(2)] string Address1,
	[property: DataMember(Order = 3)]
	[property: ProtoMember(3)] string Address2,
	[property: DataMember(Order = 4)]
	[property: ProtoMember(4)] string Address3,
	[property: DataMember(Order = 5)]
	[property: ProtoMember(5)] string FullAddress,
	[property: DataMember(Order = 6)]
	[property: ProtoMember(6)] string? Address1Kana,
	[property: DataMember(Order = 7)]
	[property: ProtoMember(7)] string? Address2Kana,
	[property: DataMember(Order = 8)]
	[property: ProtoMember(8)] string? Address3Kana
) {
	public PostalAddressItem() : this("", "", "", "", "", null, null, null) { }
}

[DataContract]
public enum PostalAddressErrorType {
	[EnumMember]
	None,
	[EnumMember]
	InvalidInput,
	[EnumMember]
	Unauthorized,
	[EnumMember]
	Forbidden,
	[EnumMember]
	NotFound,
	[EnumMember]
	RateLimited,
	[EnumMember]
	NetworkError,
	[EnumMember]
	ServiceError,
}
