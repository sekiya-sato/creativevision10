using System.Runtime.Serialization;


namespace CvBase;


/// <summary>
/// POS端末の価格付き商品検索。<c>Msg101_Op_Query</c> の照会パラメータとして送る。
/// <see cref="Barcode"/>（JAN1〜3）か <see cref="SkuId"/> のどちらかで1SKUを引く。
/// </summary>
[DataContract]
public sealed record PosBarcodeLookupRequest {
	[DataMember(Order = 1)] public string Barcode { get; init; } = string.Empty;
	/// <summary>店舗Id。上代一括変更の店舗別価格を引くために使う。0(未指定)なら全店行のみ適用される。</summary>
	[DataMember(Order = 2)] public long StoreId { get; init; }
	/// <summary>SKUキー（<see cref="DerivedShohinColSiz"/>.Id）。Barcode が空のときに使う。</summary>
	[DataMember(Order = 3)] public long SkuId { get; init; }
}
[DataContract]
public sealed record PosProduct {
	[DataMember(Order = 1)] public long ProductId { get; init; }
	[DataMember(Order = 2)] public string ProductCode { get; init; } = string.Empty;
	[DataMember(Order = 3)] public string ProductName { get; init; } = string.Empty;
	[DataMember(Order = 4)] public long ColorId { get; init; }
	[DataMember(Order = 5)] public string ColorCode { get; init; } = string.Empty;
	[DataMember(Order = 6)] public string ColorName { get; init; } = string.Empty;
	[DataMember(Order = 7)] public long SizeId { get; init; }
	[DataMember(Order = 8)] public string SizeCode { get; init; } = string.Empty;
	[DataMember(Order = 9)] public string SizeName { get; init; } = string.Empty;
	[DataMember(Order = 10)] public int UnitPrice { get; init; }
	/// <summary>消費税区分（MasterShohin.Id_Tax）。端末の税計算に使う。</summary>
	[DataMember(Order = 11)] public long TaxId { get; init; }
	/// <summary>検索に一致したJAN。SkuId で引いた場合は JAN1。</summary>
	[DataMember(Order = 12)] public string Barcode { get; init; } = string.Empty;
}
