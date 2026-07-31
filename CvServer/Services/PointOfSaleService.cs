using CodeShare;
using CvBase;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;

namespace CvServer.Services;

/// <summary>認証済みPOS端末のバーコード検索と売上確定を提供します。</summary>
[Authorize]
public sealed class PointOfSaleService : IPointOfSaleService {
	private readonly ExDatabase _db;
	private readonly ILogger<PointOfSaleService> _logger;
	public PointOfSaleService(ExDatabase db, ILogger<PointOfSaleService> logger) { _db = db; _logger = logger; }

	public Task<PosProduct?> LookupProductAsync(PosBarcodeLookupRequest request, CallContext context = default) {
		var barcode = request.Barcode?.Trim();
		if (string.IsNullOrWhiteSpace(barcode)) return Task.FromResult<PosProduct?>(null);
		context.CancellationToken.ThrowIfCancellationRequested();
		var sku = _db.Fetch(typeof(DerivedShohinColSiz), "where Jan1=@0 or Jan2=@0 or Jan3=@0", barcode).OfType<DerivedShohinColSiz>().FirstOrDefault();
		var product = sku == null ? null : FindById<MasterShohin>(sku.Id_Shohin);
		return Task.FromResult(product == null || sku == null ? null : new PosProduct { ProductId = product.Id, ProductCode = product.Code, ProductName = product.Name, ColorId = sku.Id_Col, ColorCode = sku.Code_Col, ColorName = sku.Mei_Col, SizeId = sku.Id_Siz, SizeCode = sku.Code_Siz, SizeName = sku.Mei_Siz, UnitPrice = product.TankaJodai });
	}

	public Task<PosCheckoutResponse> CheckoutAsync(PosCheckoutRequest request, CallContext context = default) {
		try {
			context.CancellationToken.ThrowIfCancellationRequested();
			ValidateRequest(request);
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var existing = _db.Fetch(typeof(Tran01Tenuri), "where PosClientSaleId=@0", request.ClientSaleId).OfType<Tran01Tenuri>().FirstOrDefault();
			if (existing != null) { _db.CompleteTransaction(); return Task.FromResult(CreateResponse(existing, true, "同じ端末取引IDの売上を返しました。")); }
			var sale = CreateSale(request);
			_db.Insert(sale);
			new SummaryDb(_db).CalcTran2SummaryStock(nameof(Tran01Tenuri), nameof(Tran01Tenuri.Id_Soko), sale.Id, false);
			_db.CompleteTransaction();
			_logger.LogInformation("POS売上確定 Id={SaleId} ClientSaleId={ClientSaleId}", sale.Id, request.ClientSaleId);
			return Task.FromResult(CreateResponse(sale, false, "売上を確定しました。"));
		}
		catch (Exception ex) { _db.AbortTransaction(); _logger.LogError(ex, "POS売上確定に失敗 ClientSaleId={ClientSaleId}", request.ClientSaleId); return Task.FromResult(new PosCheckoutResponse { Message = ex.Message }); }
	}

	private Tran01Tenuri CreateSale(PosCheckoutRequest request) {
		var store = FindRequired<MasterTokui>(request.StoreId, "店舗");
		var warehouse = FindRequired<MasterTokui>(request.WarehouseId, "倉庫");
		var staff = FindRequired<MasterShain>(request.StaffId, "担当者");
		var lines = request.Lines.Select((line, index) => CreateLine(line, index + 1)).ToList();
		var total = checked(lines.Sum(line => line.Kingaku));
		var paid = checked(request.Payment.CashAmount + request.Payment.CardAmount + request.Payment.OtherAmount);
		if (paid < total) throw new InvalidOperationException("お預り金額が合計金額に不足しています。");
		var now = DateTime.UtcNow.Ticks;
		return new Tran01Tenuri { Vdc = now, Vdu = now, DenDay = DateTime.Today.ToString("yyyyMMdd"), Kubun = (int)EnumUri01.Uriage, Id_Tenpo = store.Id, VTenpo = new CodeNameView(store.Id, store.Code, store.Name), Id_Soko = warehouse.Id, VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name), Id_Shain = staff.Id, VShain = new CodeNameView(staff.Id, staff.Code, staff.Name), Jmeisai = lines, SuTotal = lines.Sum(line => line.Su), KingakuTotal = total, JodaiTotal = lines.Sum(line => line.Jodai), GedaiTotal = lines.Sum(line => line.Gedai), Total = total, PosClientSaleId = request.ClientSaleId, JposPayment = new PosPaymentDetail { CashAmount = request.Payment.CashAmount, CardAmount = request.Payment.CardAmount, OtherAmount = request.Payment.OtherAmount, ChangeAmount = paid - total } };
	}

	private Tran99Meisai CreateLine(PosCheckoutLine line, int no) {
		var product = FindRequired<MasterShohin>(line.ProductId, "商品");
		return new Tran99Meisai { No = no, Kubun = 0, Id_Shohin = product.Id, Code_Shohin = product.Code, Mei_Shohin = product.Name, JanCode = line.Barcode, Id_Col = line.ColorId, Code_Col = line.ColorCode, Mei_Col = line.ColorName, Id_Siz = line.SizeId, Code_Siz = line.SizeCode, Mei_Siz = line.SizeName, Su = line.Quantity, Tanka = product.TankaJodai, Kingaku = checked(line.Quantity * product.TankaJodai), Jodai = product.TankaJodai, Gedai = product.TankaGenka };
	}
	private T? FindById<T>(long id) where T : BaseDbClass => _db.Fetch(typeof(T), "where Id=@0", id).OfType<T>().FirstOrDefault();
	private T FindRequired<T>(long id, string name) where T : BaseDbClass => FindById<T>(id) ?? throw new InvalidOperationException($"{name}が見つかりません: Id={id}");
	private static void ValidateRequest(PosCheckoutRequest request) {
		if (string.IsNullOrWhiteSpace(request.ClientSaleId) || request.ClientSaleId.Length > 36) throw new InvalidOperationException("端末取引IDが不正です。");
		if (request.StoreId <= 0 || request.WarehouseId <= 0 || request.StaffId <= 0) throw new InvalidOperationException("店舗、倉庫、担当者を指定してください。");
		if (request.Lines.Count == 0 || request.Lines.Any(line => line.ProductId <= 0 || line.Quantity <= 0)) throw new InvalidOperationException("売上明細が不正です。");
		if (request.Payment.CashAmount < 0 || request.Payment.CardAmount < 0 || request.Payment.OtherAmount < 0) throw new InvalidOperationException("金種金額は0以上で入力してください。");
	}
	private static PosCheckoutResponse CreateResponse(Tran01Tenuri sale, bool duplicate, string message) {
		var payment = sale.JposPayment ?? new PosPaymentDetail();
		return new PosCheckoutResponse { IsSuccess = true, IsDuplicate = duplicate, SaleId = sale.Id, TotalAmount = sale.Total, ChangeAmount = payment.ChangeAmount, Message = message };
	}
}
