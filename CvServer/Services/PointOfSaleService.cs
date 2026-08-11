using CodeShare;
using CvBase;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;

namespace CvServer.Services;

/// <summary>認証済みPOS端末のバーコード検索と売上確定・取消・精算を提供します。</summary>
[AllowAnonymous]
//[Authorize]
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
		if (product == null || sku == null) return Task.FromResult<PosProduct?>(null);
		var unitPrice = ResolveJodai(product, request.StoreId, DateTime.Today.ToString("yyyyMMdd"));
		return Task.FromResult<PosProduct?>(new PosProduct { ProductId = product.Id, ProductCode = product.Code, ProductName = product.Name, ColorId = sku.Id_Col, ColorCode = sku.Code_Col, ColorName = sku.Mei_Col, SizeId = sku.Id_Siz, SizeCode = sku.Code_Siz, SizeName = sku.Mei_Siz, UnitPrice = unitPrice });
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
			_logger.LogInformation("POS売上確定 Id={SaleId} ClientSaleId={ClientSaleId} Kubun={Kubun}", sale.Id, request.ClientSaleId, request.Kubun);
			return Task.FromResult(CreateResponse(sale, false, request.Kubun >= 20 ? "返品を確定しました。" : "売上を確定しました。"));
		}
		catch (Exception ex) { _db.AbortTransaction(); _logger.LogError(ex, "POS売上確定に失敗 ClientSaleId={ClientSaleId}", request.ClientSaleId); return Task.FromResult(new PosCheckoutResponse { Message = ex.Message }); }
	}

	public Task<PosCancelSaleResponse> CancelSaleAsync(PosCancelSaleRequest request, CallContext context = default) {
		try {
			context.CancellationToken.ThrowIfCancellationRequested();
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var original = FindById<Tran01Tenuri>(request.SaleId) ?? throw new InvalidOperationException($"売上が見つかりません: Id={request.SaleId}");
			if (original.Kubun is < 10 or > 11) throw new InvalidOperationException("取消対象は売上伝票のみです。");
			var cancelId = original.PosClientSaleId + ":C";
			var alreadyCancelled = _db.Fetch(typeof(Tran01Tenuri), "where PosClientSaleId=@0", cancelId).OfType<Tran01Tenuri>().FirstOrDefault();
			if (alreadyCancelled != null) throw new InvalidOperationException("この売上は既に取消されています。");
			var cancelStaff = FindById<MasterShain>(request.StaffId);
			var now = DateTime.UtcNow.Ticks;
			var cancelSale = new Tran01Tenuri {
				Vdc = now,
				Vdu = now,
				DenDay = DateTime.Today.ToString("yyyyMMdd"),
				Kubun = (int)EnumUri01.Henpin,
				Id_Tenpo = original.Id_Tenpo,
				VTenpo = original.VTenpo,
				Id_Soko = original.Id_Soko,
				VSoko = original.VSoko,
				Id_Shain = request.StaffId,
				VShain = cancelStaff == null ? new CodeNameView() : new CodeNameView(cancelStaff.Id, cancelStaff.Code, cancelStaff.Name),
				Jmeisai = original.Jmeisai,
				SuTotal = original.SuTotal,
				KingakuTotal = original.KingakuTotal,
				JodaiTotal = original.JodaiTotal,
				GedaiTotal = original.GedaiTotal,
				Total = original.Total,
				PosClientSaleId = cancelId,
				Memo = $"取消: 元売上No.{original.Id}",
				JposPayment = original.JposPayment
			};
			_db.Insert(cancelSale);
			new SummaryDb(_db).CalcTran2SummaryStock(nameof(Tran01Tenuri), nameof(Tran01Tenuri.Id_Soko), cancelSale.Id, false);
			_db.CompleteTransaction();
			_logger.LogInformation("POS売上取消 元Id={OriginalId} CancelId={CancelId}", original.Id, cancelSale.Id);
			return Task.FromResult(new PosCancelSaleResponse { IsSuccess = true, CancelSaleId = cancelSale.Id, Message = "取消を確定しました。" });
		}
		catch (Exception ex) { _db.AbortTransaction(); _logger.LogError(ex, "POS売上取消に失敗 SaleId={SaleId}", request.SaleId); return Task.FromResult(new PosCancelSaleResponse { Message = ex.Message }); }
	}

	public Task<PosSaveSeisanResponse> SaveSeisanAsync(PosSaveSeisanRequest request, CallContext context = default) {
		try {
			context.CancellationToken.ThrowIfCancellationRequested();
			if (request.StoreId <= 0) throw new InvalidOperationException("店舗を指定してください。");
			if (string.IsNullOrWhiteSpace(request.DenDay) || request.DenDay.Length != 8) throw new InvalidOperationException("営業日を yyyyMMdd で指定してください。");
			if (request.StaffId <= 0) throw new InvalidOperationException("担当者を指定してください。");
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var realAmount = CalcGenkin(request);
			var calcAmount = checked(request.JunbiAmount + request.CashAmount);
			var diff = checked(realAmount - calcAmount);
			var nextCnt = _db.Fetch(typeof(Tran02PosSeisan), "where DenDay=@0 and Id_Tenpo=@1", request.DenDay, request.StoreId)
				.OfType<Tran02PosSeisan>().Max(s => (int?)s.SeisanCnt) ?? 0;
			nextCnt++;
			var store = FindById<MasterTokui>(request.StoreId);
			var staff = FindById<MasterShain>(request.StaffId);
			var seisan = new Tran02PosSeisan {
				Vdc = DateTime.UtcNow.Ticks,
				Vdu = DateTime.UtcNow.Ticks,
				DenDay = request.DenDay,
				Id_Tenpo = request.StoreId,
				VTenpo = store == null ? new CodeNameView() : new CodeNameView(store.Id, store.Code, store.Name),
				Id_Shain = request.StaffId,
				VShain = staff == null ? new CodeNameView() : new CodeNameView(staff.Id, staff.Code, staff.Name),
				SeisanCnt = nextCnt,
				KyakuSu = request.KyakuSu,
				Mai10000 = request.Mai10000,
				Mai5000 = request.Mai5000,
				Mai2000 = request.Mai2000,
				Mai1000 = request.Mai1000,
				Mai500 = request.Mai500,
				Mai100 = request.Mai100,
				Mai50 = request.Mai50,
				Mai10 = request.Mai10,
				Mai5 = request.Mai5,
				Mai1 = request.Mai1,
				JunbiAmount = request.JunbiAmount,
				RealAmount = realAmount,
				CalcAmount = calcAmount,
				AmountDiff = diff,
				Jsummary = new PosSeisanSummary {
					TotalAmount = request.TotalAmount,
					CashAmount = request.CashAmount,
					CardAmount = request.CardAmount,
					OtherAmount = request.OtherAmount,
					TransactionCount = request.TransactionCount,
					ReturnCount = request.ReturnCount,
					TotalQuantity = request.TotalQuantity
				}
			};
			_db.Insert(seisan);
			_db.CompleteTransaction();
			_logger.LogInformation("POS精算確定 Id={SeisanId} SeisanCnt={SeisanCnt} DenDay={DenDay}", seisan.Id, seisan.SeisanCnt, seisan.DenDay);
			return Task.FromResult(new PosSaveSeisanResponse { IsSuccess = true, SeisanId = seisan.Id, SeisanCnt = seisan.SeisanCnt, Message = "精算を確定しました。" });
		}
		catch (Exception ex) { _db.AbortTransaction(); _logger.LogError(ex, "POS精算確定に失敗 DenDay={DenDay}", request.DenDay); return Task.FromResult(new PosSaveSeisanResponse { Message = ex.Message }); }
	}

	private Tran01Tenuri CreateSale(PosCheckoutRequest request) {
		var store = FindRequired<MasterTokui>(request.StoreId, "店舗");
		var warehouse = FindRequired<MasterTokui>(request.WarehouseId, "倉庫");
		var staff = FindRequired<MasterShain>(request.StaffId, "担当者");
		var denDay = DateTime.Today.ToString("yyyyMMdd");
		var lines = request.Lines.Select((line, index) => CreateLine(line, index + 1, staff, store.Id, denDay)).ToList();
		var total = checked(lines.Sum(line => line.Kingaku));
		var paid = checked(request.Payment.CashAmount + request.Payment.CardAmount + request.Payment.OtherAmount);
		if (paid < total) throw new InvalidOperationException("お預り金額が合計金額に不足しています。");
		var now = DateTime.UtcNow.Ticks;
		return new Tran01Tenuri { Vdc = now, Vdu = now, DenDay = denDay, Kubun = request.Kubun, Id_Tenpo = store.Id, VTenpo = new CodeNameView(store.Id, store.Code, store.Name), Id_Soko = warehouse.Id, VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name), Id_Shain = staff.Id, VShain = new CodeNameView(staff.Id, staff.Code, staff.Name), Jmeisai = lines, SuTotal = lines.Sum(line => line.Su), KingakuTotal = total, JodaiTotal = lines.Sum(line => line.Jodai), GedaiTotal = lines.Sum(line => line.Gedai), Total = total, PosClientSaleId = request.ClientSaleId, JposPayment = new PosPaymentDetail { CashAmount = request.Payment.CashAmount, CardAmount = request.Payment.CardAmount, OtherAmount = request.Payment.OtherAmount, ChangeAmount = paid - total } };
	}

	private Tran99Meisai CreateLine(PosCheckoutLine line, int no, MasterShain headerStaff, long storeId, string denDay) {
		var product = FindRequired<MasterShohin>(line.ProductId, "商品");
		var lineStaff = line.StaffId > 0 ? FindRequired<MasterShain>(line.StaffId, "明細担当者") : headerStaff;
		var tanka = ResolveJodai(product, storeId, denDay);
		return new Tran99Meisai { No = no, Kubun = line.Kubun, Id_Shohin = product.Id, Code_Shohin = product.Code, Mei_Shohin = product.Name, JanCode = line.Barcode, Id_Col = line.ColorId, Code_Col = line.ColorCode, Mei_Col = line.ColorName, Id_Siz = line.SizeId, Code_Siz = line.SizeCode, Mei_Siz = line.SizeName, Su = line.Quantity, Tanka = tanka, Kingaku = checked(line.Quantity * tanka), Jodai = tanka, Gedai = product.TankaGenka, Id_Shain = lineStaff.Id, Code_Shain = lineStaff.Code, Mei_Shain = lineStaff.Name };
	}

	/// <summary>
	/// 店舗・日付に応じた販売価格を解決する。上代一括変更(<see cref="DerivedJodai"/>)の適用行が無ければ商品マスタの上代を返す。
	/// </summary>
	private int ResolveJodai(MasterShohin product, long storeId, string denDay)
		=> new JodaiDb(_db).ResolveJodai(product.Id, EnumJodaiTaisho.Tenpo, storeId, denDay);
	private T? FindById<T>(long id) where T : BaseDbClass => _db.Fetch(typeof(T), "where Id=@0", id).OfType<T>().FirstOrDefault();
	private T FindRequired<T>(long id, string name) where T : BaseDbClass => FindById<T>(id) ?? throw new InvalidOperationException($"{name}が見つかりません: Id={id}");
	private static void ValidateRequest(PosCheckoutRequest request) {
		if (string.IsNullOrWhiteSpace(request.ClientSaleId) || request.ClientSaleId.Length > 36) throw new InvalidOperationException("端末取引IDが不正です。");
		if (request.StoreId <= 0 || request.WarehouseId <= 0 || request.StaffId <= 0) throw new InvalidOperationException("店舗、倉庫、担当者を指定してください。");
		if (request.Lines.Count == 0 || request.Lines.Any(line => line.ProductId <= 0 || line.Quantity <= 0)) throw new InvalidOperationException("売上明細が不正です。");
		if (request.Payment.CashAmount < 0 || request.Payment.CardAmount < 0 || request.Payment.OtherAmount < 0) throw new InvalidOperationException("金種金額は0以上で入力してください。");
		if (request.Kubun is not 10 and not 11 and not 20 and not 21) throw new InvalidOperationException("伝票区分が不正です。");
	}
	private static PosCheckoutResponse CreateResponse(Tran01Tenuri sale, bool duplicate, string message) {
		var payment = sale.JposPayment ?? new PosPaymentDetail();
		return new PosCheckoutResponse { IsSuccess = true, IsDuplicate = duplicate, SaleId = sale.Id, TotalAmount = sale.Total, ChangeAmount = payment.ChangeAmount, Message = message };
	}

	private static int CalcGenkin(PosSaveSeisanRequest request) => checked(
		request.Mai10000 * 10000 + request.Mai5000 * 5000 + request.Mai2000 * 2000 +
		request.Mai1000 * 1000 + request.Mai500 * 500 + request.Mai100 * 100 +
		request.Mai50 * 50 + request.Mai10 * 10 + request.Mai5 * 5 + request.Mai1 * 1);
}
