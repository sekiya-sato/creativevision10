using ProtoBuf;
using System.Runtime.Serialization;
using System.ServiceModel;
namespace CodeShare;

[ServiceContract]
public interface IPostalAddressService {
	[OperationContract]
	Task<PostalAddressSearchResult> SearchByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default);
}

// 検索結果レコード
[DataContract]
[ProtoContract]
public sealed record PostalAddressSearchResult(
	[property: ProtoMember(1)] bool IsSuccess,
	[property: ProtoMember(2)] string NormalizedPostalCode,
	[property: ProtoMember(3)] List<PostalAddressItem> Items, // IReadOnlyList から List へ変更
	[property: ProtoMember(4)] string Message,
	[property: ProtoMember(5)] PostalAddressErrorType ErrorType
) {
	// デシリアライザ用のデフォルトコンストラクタを確保するため
	// 初期値を設定したコンストラクタを明示するか、プロパティを初期化します
	public PostalAddressSearchResult() : this(false, "", [], "", PostalAddressErrorType.None) { }
}

[DataContract]
[ProtoContract]
public sealed record PostalAddressItem(
	[property: ProtoMember(1)] string PostalCode,
	[property: ProtoMember(2)] string Address1,
	[property: ProtoMember(3)] string Address2,
	[property: ProtoMember(4)] string Address3,
	[property: ProtoMember(5)] string FullAddress,
	[property: ProtoMember(6)] string? Address1Kana,
	[property: ProtoMember(7)] string? Address2Kana,
	[property: ProtoMember(8)] string? Address3Kana
) {
	public PostalAddressItem() : this("", "", "", "", "", null, null, null) { }
}

public enum PostalAddressErrorType {
	[EnumMember]
	None,
	InvalidInput,
	Unauthorized,
	Forbidden,
	NotFound,
	RateLimited,
	NetworkError,
	ServiceError,
}

[DataContract]
public sealed class JapanPostBizOptions {
	[DataMember(Order = 1)]
	public string BaseUrl { get; set; } = "https://api.da.pf.japanpost.jp";
	[DataMember(Order = 2)]
	public string TokenPath { get; set; } = "/api/v2/j/token";
	[DataMember(Order = 3)]
	public string SearchCodePath { get; set; } = "/api/v2/searchcode";
	[DataMember(Order = 4)]
	public string ClientId { get; set; } = string.Empty;
	[DataMember(Order = 5)]
	public string SecretKey { get; set; } = string.Empty;
	[DataMember(Order = 6)]
	public string EcUid { get; set; } = string.Empty;
	[DataMember(Order = 7)]
	public string UserAgent { get; set; } = "CvServer/1.0";
	[DataMember(Order = 8)]
	public int TimeoutSeconds { get; set; } = 10;
	[DataMember(Order = 9)]
	public int DefaultLimit { get; set; } = 1000;
	[DataMember(Order = 10)]
	public int DefaultChoikiType { get; set; } = 1;
	[DataMember(Order = 11)]
	public int DefaultSearchType { get; set; } = 2;
	[DataMember(Order = 12)]
	public int TokenRefreshMarginSeconds { get; set; } = 60;
}
