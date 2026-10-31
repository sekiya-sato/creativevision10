using CvBase;
using CvBase.Share;
using CvDomainLogic;

namespace CvServer.Services;

/// <summary>
/// POS端末の価格付き商品検索と、汎用追加（InsertParam）で届いたPOS伝票の確定前補正を行います。
/// 単価・税・合計・精算の採番は端末を信用せずサーバで確定させる（設計判断記録 §2.18）。
/// </summary>
public sealed class PointOfSaleService {
	/// <summary>取消伝票の端末取引IDに付ける接尾辞。元売上の端末取引ID + この値。</summary>
	public const string CancelSuffix = ":C";

	private readonly ExDatabase _db;
	private readonly ILogger<PointOfSaleService> _logger;
	public PointOfSaleService(ExDatabase db, ILogger<PointOfSaleService> logger) { _db = db; _logger = logger; }

	/// <summary>バーコード（JAN1〜3）またはSKUキーで1SKUを引き、店舗上代と税区分を付けて返す。</summary>
	public PosProduct? LookupProduct(PosBarcodeLookupRequest request) {
		var barcode = request.Barcode?.Trim() ?? string.Empty;
		var sku = barcode.Length > 0
			? _db.Fetch(typeof(DerivedShohinColSiz), "where Jan1=@0 or Jan2=@0 or Jan3=@0", barcode).OfType<DerivedShohinColSiz>().FirstOrDefault()
			: request.SkuId > 0 ? FindById<DerivedShohinColSiz>(request.SkuId) : null;
		var product = sku == null ? null : FindById<MasterShohin>(sku.Id_Shohin);
		if (product == null || sku == null) return null;
		var unitPrice = ResolveJodai(product, request.StoreId, DateTime.Today.ToString("yyyyMMdd"));
		return new PosProduct { ProductId = product.Id, ProductCode = product.Code, ProductName = product.Name, ColorId = sku.Id_Col, ColorCode = sku.Code_Col, ColorName = sku.Mei_Col, SizeId = sku.Id_Siz, SizeCode = sku.Code_Siz, SizeName = sku.Mei_Siz, UnitPrice = unitPrice, TaxId = product.Id_Tax, Barcode = barcode.Length > 0 ? barcode : sku.Jan1 };
	}

	/// <summary>
	/// 汎用追加の対象がPOS伝票か。売上・取消は端末取引IDあり、精算は SeisanCnt=0（事務所画面は1以上を入れる）。
	/// </summary>
	public static bool IsPosInsert(object item) => item switch {
		Tran01Tenuri sale => !string.IsNullOrEmpty(sale.PosClientSaleId),
		Tran04PosSeisan seisan => seisan.SeisanCnt == 0,
		_ => false,
	};

	/// <summary>
	/// 汎用追加のトランザクション内で、POS伝票をサーバ値で確定させる。POS伝票でなければそのまま返す。
	/// </summary>
	/// <param name="item">追加要求の行（Vdc/Vdu は設定済み）</param>
	/// <returns>登録する行。Duplicate=true なら同じ端末取引IDの既存売上で、登録せずにそのまま返す</returns>
	public (object Item, bool Duplicate) PrepareInsert(object item) {
		switch (item) {
			case Tran01Tenuri sale when !string.IsNullOrEmpty(sale.PosClientSaleId): {
				var existing = FindBySaleId(sale.PosClientSaleId);
				if (sale.PosClientSaleId.EndsWith(CancelSuffix, StringComparison.Ordinal)) {
					if (existing != null) throw new InvalidOperationException("この売上は既に取消されています。");
					return (PrepareCancel(sale), false);
				}
				if (existing != null) {
					_logger.LogInformation("POS売上の再送 Id={SaleId} ClientSaleId={ClientSaleId}", existing.Id, sale.PosClientSaleId);
					return (existing, true);
				}
				return (PrepareSale(sale), false);
			}
			case Tran04PosSeisan seisan when seisan.SeisanCnt == 0:
				return (PrepareSeisan(seisan), false);
			default:
				return (item, false);
		}
	}

	private Tran01Tenuri PrepareSale(Tran01Tenuri request) {
		ValidateSale(request);
		var store = FindRequired<MasterTokui>(request.Id_Tenpo, "店舗");
		var warehouse = FindRequired<MasterTokui>(request.Id_Soko, "倉庫");
		var staff = FindRequired<MasterShain>(request.Id_Shain, "担当者");
		var sysman = FindById<MasterSysman>(1) ?? new MasterSysman();
		var denDay = DateTime.Today.ToString("yyyyMMdd");
		var lines = (request.Jmeisai ?? []).Select((line, index) => CreateLine(line, index + 1, staff, store.Id, denDay)).ToList();
		var kingakuTotal = checked(lines.Sum(line => line.Kingaku));
		var rounding = (EnumRounding)store.TaxRounding;
		var totals = TaxCalculator.Apply(
			lines,
			TaxRateResolver.CreateRateResolver(sysman, denDay),
			EnumTaxCalcUnit.Slip,
			rounding);
		var total = checked(Math.Abs(kingakuTotal) + totals.TaxTotal);
		var payment = request.JposPayment ?? new PosPaymentDetail();
		var paid = checked(payment.CashAmount + payment.CardAmount + payment.OtherAmount);
		if (paid < total) throw new InvalidOperationException("お預り金額が合計金額に不足しています。");
		// 釣銭は現金で返すため、現金のお預りを超える釣銭（カード・その他の過入金）は受け付けない
		if (paid - total > payment.CashAmount) throw new InvalidOperationException("カード・その他のお預りが合計金額を超えています。");
		var registerNo = NormalizeRegisterNo(payment.RegisterNo);
		_logger.LogInformation("POS売上確定 ClientSaleId={ClientSaleId} Kubun={Kubun} Total={Total}", request.PosClientSaleId, request.Kubun, total);
		return new Tran01Tenuri {
			Vdc = request.Vdc,
			Vdu = request.Vdu,
			DenDay = denDay,
			Kubun = request.Kubun,
			Id_Tenpo = store.Id,
			VTenpo = new CodeNameView(store.Id, store.Code, store.Name),
			Id_Soko = warehouse.Id,
			VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
			Id_Shain = staff.Id,
			VShain = new CodeNameView(staff.Id, staff.Code, staff.Name),
			Jmeisai = lines,
			SuTotal = lines.Sum(line => line.Su),
			KingakuTotal = kingakuTotal,
			JodaiTotal = lines.Sum(line => (long)line.Su * line.Jodai),
			GedaiTotal = lines.Sum(line => (long)line.Su * line.Gedai),
			TaxRounding = store.TaxRounding,
			TaxableAmount1 = totals.TaxableAmount1,
			TaxableAmount2 = totals.TaxableAmount2,
			TaxableAmount3 = totals.TaxableAmount3,
			Tax1 = totals.Tax1,
			Tax2 = totals.Tax2,
			Tax3 = totals.Tax3,
			Total = total,
			PosClientSaleId = request.PosClientSaleId,
			JposPayment = new PosPaymentDetail {
				CashAmount = payment.CashAmount,
				CardAmount = payment.CardAmount,
				OtherAmount = payment.OtherAmount,
				ChangeAmount = checked((int)(paid - total)),
				RegisterNo = registerNo
			}
		};
	}

	private Tran01Tenuri PrepareCancel(Tran01Tenuri request) {
		var originalSaleId = request.PosClientSaleId[..^CancelSuffix.Length];
		// POS以外の売上（端末取引IDなし）は取消しない
		if (originalSaleId.Length == 0) throw new InvalidOperationException("POS売上以外は取消できません。");
		var original = FindBySaleId(originalSaleId) ?? throw new InvalidOperationException($"取消対象のPOS売上が見つかりません: {originalSaleId}");
		if (original.Kubun is not 10 and not 11 and not 14) throw new InvalidOperationException("取消対象は売上伝票のみです。");
		// 取消の返金は取消を行ったレジで出すため、精算の集計先は取消したレジにする
		var registerNo = NormalizeRegisterNo(request.JposPayment?.RegisterNo);
		var originalPayment = original.JposPayment ?? new PosPaymentDetail();
		var cancelStaff = FindById<MasterShain>(request.Id_Shain);
		_logger.LogInformation("POS売上取消 元Id={OriginalId} ClientSaleId={ClientSaleId}", original.Id, request.PosClientSaleId);
		return new Tran01Tenuri {
			Vdc = request.Vdc,
			Vdu = request.Vdu,
			DenDay = DateTime.Today.ToString("yyyyMMdd"),
			// 社販売上(14)の取消は社販返品(24)にする。10/11は従来通りP返品(20)固定(11→21への変更は今回対象外)。
			Kubun = original.Kubun == (int)EnumUri01.UriShahan ? (int)EnumUri01.HenShahan : (int)EnumUri01.Henpin,
			Id_Tenpo = original.Id_Tenpo,
			VTenpo = original.VTenpo,
			Id_Soko = original.Id_Soko,
			VSoko = original.VSoko,
			Id_Shain = request.Id_Shain,
			VShain = cancelStaff == null ? new CodeNameView() : new CodeNameView(cancelStaff.Id, cancelStaff.Code, cancelStaff.Name),
			Jmeisai = original.Jmeisai,
			SuTotal = original.SuTotal,
			KingakuTotal = original.KingakuTotal,
			JodaiTotal = original.JodaiTotal,
			GedaiTotal = original.GedaiTotal,
			TaxRounding = original.TaxRounding,
			TaxableAmount1 = original.TaxableAmount1,
			TaxableAmount2 = original.TaxableAmount2,
			TaxableAmount3 = original.TaxableAmount3,
			Tax1 = original.Tax1,
			Tax2 = original.Tax2,
			Tax3 = original.Tax3,
			Total = original.Total,
			PosClientSaleId = request.PosClientSaleId,
			Memo = $"取消: 元売上No.{original.Id}",
			JposPayment = new PosPaymentDetail {
				CashAmount = originalPayment.CashAmount,
				CardAmount = originalPayment.CardAmount,
				OtherAmount = originalPayment.OtherAmount,
				ChangeAmount = originalPayment.ChangeAmount,
				RegisterNo = registerNo
			}
		};
	}

	private Tran04PosSeisan PrepareSeisan(Tran04PosSeisan seisan) {
		if (seisan.Id_Tenpo <= 0) throw new InvalidOperationException("店舗を指定してください。");
		if (string.IsNullOrWhiteSpace(seisan.DenDay) || seisan.DenDay.Length != 8) throw new InvalidOperationException("営業日を yyyyMMdd で指定してください。");
		if (seisan.Id_Shain <= 0) throw new InvalidOperationException("担当者を指定してください。");
		seisan.RegisterNo = NormalizeRegisterNo(seisan.RegisterNo);
		var summary = seisan.Jsummary ?? new PosSeisanSummary();
		var realAmount = CalcGenkin(seisan);
		var calcAmount = checked(seisan.JunbiAmount + summary.CashAmount);
		// 精算は営業日×店舗×レジ番号で1件だけ。登録済みなら（事務所画面の行を含む）二重登録しない
		var existing = _db.Fetch(typeof(Tran04PosSeisan), "where DenDay=@0 and Id_Tenpo=@1 and RegisterNo=@2", seisan.DenDay, seisan.Id_Tenpo, seisan.RegisterNo)
			.OfType<Tran04PosSeisan>().FirstOrDefault();
		if (existing != null) throw new InvalidOperationException($"営業日 {seisan.DenDay} のレジ {seisan.RegisterNo} の精算は登録済みです（Id={existing.Id}）。");
		var store = FindById<MasterTokui>(seisan.Id_Tenpo);
		var staff = FindById<MasterShain>(seisan.Id_Shain);
		seisan.VTenpo = store == null ? new CodeNameView() : new CodeNameView(store.Id, store.Code, store.Name);
		seisan.VShain = staff == null ? new CodeNameView() : new CodeNameView(staff.Id, staff.Code, staff.Name);
		seisan.SeisanCnt = 1;
		seisan.RealAmount = realAmount;
		seisan.CalcAmount = calcAmount;
		seisan.AmountDiff = checked(realAmount - calcAmount);
		seisan.Jsummary = summary;
		_logger.LogInformation("POS精算確定 SeisanCnt={SeisanCnt} DenDay={DenDay} RegisterNo={RegisterNo}", seisan.SeisanCnt, seisan.DenDay, seisan.RegisterNo);
		return seisan;
	}

	private Tran99Meisai CreateLine(Tran99Meisai line, int no, MasterShain headerStaff, long storeId, string denDay) {
		var product = FindRequired<MasterShohin>(line.Id_Shohin, "商品");
		var lineStaff = line.Id_Shain > 0 ? FindRequired<MasterShain>(line.Id_Shain, "明細担当者") : headerStaff;
		var tanka = ResolveJodai(product, storeId, denDay);
		return new Tran99Meisai { No = no, Kubun = line.Kubun, Id_Shohin = product.Id, Code_Shohin = product.Code, Mei_Shohin = product.Name, JanCode = line.JanCode, Id_Col = line.Id_Col, Code_Col = line.Code_Col, Mei_Col = line.Mei_Col, Id_Siz = line.Id_Siz, Code_Siz = line.Code_Siz, Mei_Siz = line.Mei_Siz, Su = line.Su, Tanka = tanka, Kingaku = checked((long)line.Su * tanka), Jodai = tanka, Gedai = product.TankaGenka, Id_Shain = lineStaff.Id, Code_Shain = lineStaff.Code, Mei_Shain = lineStaff.Name, Id_Tax = product.Id_Tax };
	}

	/// <summary>
	/// 店舗・日付に応じた販売価格を解決する。上代一括変更(<see cref="DerivedJodai"/>)の適用行が無ければ商品マスタの上代を返す。
	/// </summary>
	private int ResolveJodai(MasterShohin product, long storeId, string denDay)
		=> new JodaiDb(_db).ResolveJodai(product.Id, EnumJodaiTaisho.Tenpo, storeId, denDay);
	private Tran01Tenuri? FindBySaleId(string clientSaleId) => _db.Fetch(typeof(Tran01Tenuri), "where PosClientSaleId=@0", clientSaleId).OfType<Tran01Tenuri>().FirstOrDefault();
	private T? FindById<T>(long id) where T : BaseDbClass => _db.Fetch(typeof(T), "where Id=@0", id).OfType<T>().FirstOrDefault();
	private T FindRequired<T>(long id, string name) where T : BaseDbClass => FindById<T>(id) ?? throw new InvalidOperationException($"{name}が見つかりません: Id={id}");
	private static void ValidateSale(Tran01Tenuri request) {
		// 列は36文字。取消IDは末尾に ":C" を付けるため、売上の端末取引IDはその分短く制限する
		if (request.PosClientSaleId.Length > 36 - CancelSuffix.Length) throw new InvalidOperationException("端末取引IDが不正です。");
		if (request.Id_Tenpo <= 0 || request.Id_Soko <= 0 || request.Id_Shain <= 0) throw new InvalidOperationException("店舗、倉庫、担当者を指定してください。");
		if (request.Jmeisai == null || request.Jmeisai.Count == 0 || request.Jmeisai.Any(line => line.Id_Shohin <= 0 || line.Su <= 0)) throw new InvalidOperationException("売上明細が不正です。");
		var payment = request.JposPayment ?? new PosPaymentDetail();
		if (payment.CashAmount < 0 || payment.CardAmount < 0 || payment.OtherAmount < 0) throw new InvalidOperationException("金種金額は0以上で入力してください。");
		if (request.Kubun is not 10 and not 11 and not 14 and not 20 and not 21 and not 24) throw new InvalidOperationException("伝票区分が不正です。");
	}

	/// <summary>レジ番号の前後空白を除き、必須・8文字以内（Tran04PosSeisan.RegisterNo の列長）を検査する。事務所の精算画面と同じく Trim して比較する。</summary>
	private static string NormalizeRegisterNo(string? registerNo) {
		var value = registerNo?.Trim() ?? string.Empty;
		if (value.Length == 0) throw new InvalidOperationException("レジ番号を指定してください。");
		if (value.Length > 8) throw new InvalidOperationException("レジ番号は8文字以内で指定してください。");
		return value;
	}

	private static int CalcGenkin(Tran04PosSeisan seisan) => checked(
		seisan.Mai10000 * 10000 + seisan.Mai5000 * 5000 + seisan.Mai2000 * 2000 +
		seisan.Mai1000 * 1000 + seisan.Mai500 * 500 + seisan.Mai100 * 100 +
		seisan.Mai50 * 50 + seisan.Mai10 * 10 + seisan.Mai5 * 5 + seisan.Mai1 * 1);
}
