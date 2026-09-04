using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

[DataContract]
public sealed record PosBarcodeLookupRequest {
	[DataMember(Order = 1)] public string Barcode { get; init; } = string.Empty;
	/// <summary>店舗Id。上代一括変更の店舗別価格を引くために使う。0(未指定)なら全店行のみ適用される。</summary>
	[DataMember(Order = 2)] public long StoreId { get; init; }
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
}
[DataContract]
public sealed record PosCheckoutLine {
	[DataMember(Order = 1)] public string Barcode { get; init; } = string.Empty;
	[DataMember(Order = 2)] public long ProductId { get; init; }
	[DataMember(Order = 3)] public long ColorId { get; init; }
	[DataMember(Order = 4)] public string ColorCode { get; init; } = string.Empty;
	[DataMember(Order = 5)] public string ColorName { get; init; } = string.Empty;
	[DataMember(Order = 6)] public long SizeId { get; init; }
	[DataMember(Order = 7)] public string SizeCode { get; init; } = string.Empty;
	[DataMember(Order = 8)] public string SizeName { get; init; } = string.Empty;
	[DataMember(Order = 9)] public int Quantity { get; init; }
	/// <summary>明細区分（0:Pプロパー 1:Sセール）。省略時は P。</summary>
	[DataMember(Order = 10)] public int Kubun { get; init; }
	/// <summary>明細担当者キー。0 なら伝票担当を引き継ぐ。</summary>
	[DataMember(Order = 11)] public long StaffId { get; init; }
	[DataMember(Order = 12)] public string StaffCode { get; init; } = string.Empty;
	[DataMember(Order = 13)] public string StaffName { get; init; } = string.Empty;
}
[DataContract]
public sealed record PosPayment {
	[DataMember(Order = 1)] public int CashAmount { get; init; }
	[DataMember(Order = 2)] public int CardAmount { get; init; }
	[DataMember(Order = 3)] public int OtherAmount { get; init; }
}
[DataContract]
public sealed record PosCheckoutRequest {
	[DataMember(Order = 1)] public string ClientSaleId { get; init; } = string.Empty;
	[DataMember(Order = 2)] public long StoreId { get; init; }
	[DataMember(Order = 3)] public long WarehouseId { get; init; }
	[DataMember(Order = 4)] public long StaffId { get; init; }
	[DataMember(Order = 5)] public List<PosCheckoutLine> Lines { get; init; } = [];
	[DataMember(Order = 6)] public PosPayment Payment { get; init; } = new();
	/// <summary>伝票区分（10=売上 11=売上(セール) 20=返品 21=返品(セール)）。省略時は 10。</summary>
	[DataMember(Order = 7)] public int Kubun { get; init; } = 10;
}
[DataContract]
public sealed record PosCheckoutResponse {
	[DataMember(Order = 1)] public bool IsSuccess { get; init; }
	[DataMember(Order = 2)] public bool IsDuplicate { get; init; }
	[DataMember(Order = 3)] public long SaleId { get; init; }
	[DataMember(Order = 4)] public long TotalAmount { get; init; }
	[DataMember(Order = 5)] public long ChangeAmount { get; init; }
	[DataMember(Order = 6)] public string Message { get; init; } = string.Empty;
}
/// <summary>売上取消リクエスト。SaleId は取消対象の Tran01Tenuri.Id。</summary>
[DataContract]
public sealed record PosCancelSaleRequest {
	[DataMember(Order = 1)] public long SaleId { get; init; }
	/// <summary>取消操作を行った担当者キー。</summary>
	[DataMember(Order = 2)] public long StaffId { get; init; }
}
[DataContract]
public sealed record PosCancelSaleResponse {
	[DataMember(Order = 1)] public bool IsSuccess { get; init; }
	/// <summary>生成された取消伝票（Kubun=20）の Id。</summary>
	[DataMember(Order = 2)] public long CancelSaleId { get; init; }
	[DataMember(Order = 3)] public string Message { get; init; } = string.Empty;
}
/// <summary>日次精算の保存リクエスト。RealAmount/CalcAmount/AmountDiff はサーバが算出する。</summary>
[DataContract]
public sealed record PosSaveSeisanRequest {
	[DataMember(Order = 1)] public long StoreId { get; init; }
	/// <summary>レジ番号。</summary>
	[DataMember(Order = 2)] public string RegisterNo { get; init; } = string.Empty;
	/// <summary>営業日（yyyyMMdd）。</summary>
	[DataMember(Order = 3)] public string DenDay { get; init; } = string.Empty;
	[DataMember(Order = 4)] public long StaffId { get; init; }
	/// <summary>来店客数。</summary>
	[DataMember(Order = 5)] public int KyakuSu { get; init; }
	[DataMember(Order = 6)] public int Mai10000 { get; init; }
	[DataMember(Order = 7)] public int Mai5000 { get; init; }
	[DataMember(Order = 8)] public int Mai2000 { get; init; }
	[DataMember(Order = 9)] public int Mai1000 { get; init; }
	[DataMember(Order = 10)] public int Mai500 { get; init; }
	[DataMember(Order = 11)] public int Mai100 { get; init; }
	[DataMember(Order = 12)] public int Mai50 { get; init; }
	[DataMember(Order = 13)] public int Mai10 { get; init; }
	[DataMember(Order = 14)] public int Mai5 { get; init; }
	[DataMember(Order = 15)] public int Mai1 { get; init; }
	/// <summary>釣銭準備金。</summary>
	[DataMember(Order = 16)] public int JunbiAmount { get; init; }
	[DataMember(Order = 17)] public int TotalAmount { get; init; }
	[DataMember(Order = 18)] public int CashAmount { get; init; }
	[DataMember(Order = 19)] public int CardAmount { get; init; }
	[DataMember(Order = 20)] public int OtherAmount { get; init; }
	[DataMember(Order = 21)] public int TransactionCount { get; init; }
	[DataMember(Order = 22)] public int ReturnCount { get; init; }
	[DataMember(Order = 23)] public int TotalQuantity { get; init; }
	/// <summary>外税合計金額。</summary>
	[DataMember(Order = 24)] public int TaxAmount { get; init; }
	/// <summary>金券使用金額。</summary>
	[DataMember(Order = 25)] public int GiftCertificateAmount { get; init; }
	/// <summary>掛売金額。</summary>
	[DataMember(Order = 26)] public int CreditSaleAmount { get; init; }
	/// <summary>収入印紙枚数合計。</summary>
	[DataMember(Order = 27)] public int StampCount { get; init; }
	/// <summary>収入印紙金額合計。</summary>
	[DataMember(Order = 28)] public int StampAmount { get; init; }
	/// <summary>
	/// メッセージ型
	/// </summary>
	[DataMember(Order = 29)]
	public Type DataType { get; set; } = typeof(string);
	/// <summary>
	/// メッセージ本体
	/// </summary>
	[DataMember(Order = 30)]
	public string DataMsg { get; set; } = string.Empty;
}
[DataContract]
public sealed record PosSaveSeisanResponse {
	[DataMember(Order = 1)] public bool IsSuccess { get; init; }
	/// <summary>保存された Tran04PosSeisan.Id。</summary>
	[DataMember(Order = 2)] public long SeisanId { get; init; }
	/// <summary>本日何回目の精算か（同一営業日・店舗で連番）。</summary>
	[DataMember(Order = 3)] public int SeisanCnt { get; init; }
	[DataMember(Order = 4)] public string Message { get; init; } = string.Empty;
}
[ServiceContract]
public interface IPointOfSaleService {
	[OperationContract] Task<PosProduct?> LookupProductAsync(PosBarcodeLookupRequest request, CallContext context = default);
	[OperationContract] Task<PosCheckoutResponse> CheckoutAsync(PosCheckoutRequest request, CallContext context = default);
	[OperationContract] Task<PosCancelSaleResponse> CancelSaleAsync(PosCancelSaleRequest request, CallContext context = default);
	[OperationContract] Task<PosSaveSeisanResponse> SaveSeisanAsync(PosSaveSeisanRequest request, CallContext context = default);
}
