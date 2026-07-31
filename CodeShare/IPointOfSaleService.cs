using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

[DataContract] public sealed class PosBarcodeLookupRequest { [DataMember(Order = 1)] public string Barcode { get; init; } = string.Empty; }
[DataContract] public sealed class PosProduct {
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
[DataContract] public sealed class PosCheckoutLine {
	[DataMember(Order = 1)] public string Barcode { get; init; } = string.Empty;
	[DataMember(Order = 2)] public long ProductId { get; init; }
	[DataMember(Order = 3)] public long ColorId { get; init; }
	[DataMember(Order = 4)] public string ColorCode { get; init; } = string.Empty;
	[DataMember(Order = 5)] public string ColorName { get; init; } = string.Empty;
	[DataMember(Order = 6)] public long SizeId { get; init; }
	[DataMember(Order = 7)] public string SizeCode { get; init; } = string.Empty;
	[DataMember(Order = 8)] public string SizeName { get; init; } = string.Empty;
	[DataMember(Order = 9)] public int Quantity { get; init; }
}
[DataContract] public sealed class PosPayment {
	[DataMember(Order = 1)] public int CashAmount { get; init; }
	[DataMember(Order = 2)] public int CardAmount { get; init; }
	[DataMember(Order = 3)] public int OtherAmount { get; init; }
}
[DataContract] public sealed class PosCheckoutRequest {
	[DataMember(Order = 1)] public string ClientSaleId { get; init; } = string.Empty;
	[DataMember(Order = 2)] public long StoreId { get; init; }
	[DataMember(Order = 3)] public long WarehouseId { get; init; }
	[DataMember(Order = 4)] public long StaffId { get; init; }
	[DataMember(Order = 5)] public List<PosCheckoutLine> Lines { get; init; } = [];
	[DataMember(Order = 6)] public PosPayment Payment { get; init; } = new();
}
[DataContract] public sealed class PosCheckoutResponse {
	[DataMember(Order = 1)] public bool IsSuccess { get; init; }
	[DataMember(Order = 2)] public bool IsDuplicate { get; init; }
	[DataMember(Order = 3)] public long SaleId { get; init; }
	[DataMember(Order = 4)] public int TotalAmount { get; init; }
	[DataMember(Order = 5)] public int ChangeAmount { get; init; }
	[DataMember(Order = 6)] public string Message { get; init; } = string.Empty;
}
[ServiceContract] public interface IPointOfSaleService {
	[OperationContract] Task<PosProduct?> LookupProductAsync(PosBarcodeLookupRequest request, CallContext context = default);
	[OperationContract] Task<PosCheckoutResponse> CheckoutAsync(PosCheckoutRequest request, CallContext context = default);
}
