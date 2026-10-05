using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

public sealed class AutoReplenishConflictException(string message) : InvalidOperationException(message);

/// <summary>自動補充の読取・保存・確定。数量の正本はサーバーで再計算し、全更新を同一トランザクションに収める。</summary>
public sealed class AutoReplenishDb(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private const string Recalculate = "補充の計算条件が更新されています。取消後に再計算してください。";
	private static AutoReplenishSku Sku(TranHoju r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static AutoReplenishSku Sku(TranHaibun r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static AutoReplenishSku Sku(Tran99Meisai r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static AutoReplenishSku Sku(SummaryRealStock r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static AutoReplenishSku Sku(MasterAutoReplenishStock r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static AutoReplenishSku Sku(MasterAutoReplenishExclude r) => new(r.Id_Shohin, r.Id_Col, r.Id_Siz);
	private static string Ids(IEnumerable<long> ids) => string.Join(",", ids.Distinct().Order());
	private List<T> Read<T>(string suffix = "", params object[] args) => _db.FetchDialect<T>($"SELECT * FROM {typeof(T).Name} {suffix}", args);
	private static void Audit(BaseDbClass row, bool create) {
		var stamp = Common.GetVdate();
		if (create) { row.Id = 0; row.Vdc = stamp; }
		row.Vdu = Math.Max(stamp, checked(row.Vdu + 1));
	}
	private T Transaction<T>(Func<T> body) {
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		try { var result = body(); _db.CompleteTransaction(); return result; }
		catch { _db.AbortTransaction(); throw; }
	}
	private static string CheckDay(string day) {
		if (!AllocationRules.IsYmd(day) || day != DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			throw new ArgumentException("基準日は当日を指定してください。");
		return day;
	}
	private MasterTokui CheckSource(long id) => Read<MasterTokui>("WHERE Id = @0 AND TenType = 0 AND IsZaiko = 1", id).SingleOrDefault()
		?? throw new ArgumentException("補充元は倉庫（店種0）を指定してください。");

	public AutoReplenishResult Preview(AutoReplenishParam param) => Transaction(() => BuildPreview(param));

	private AutoReplenishResult BuildPreview(AutoReplenishParam param) {
		var day = CheckDay(param.DenDay);
		var source = CheckSource(param.Id_Soko);
		var employee = param.Id_Shain > 0 ? Read<MasterShain>("WHERE Id = @0", param.Id_Shain).SingleOrDefault() : null;
		if (param.Id_Shain > 0 && employee == null) throw new ArgumentException("入力社員が存在しません。");
		var settings = Read<MasterAutoReplenishStock>("WHERE Id_Soko = @0 ORDER BY Id", source.Id);
		var excludes = Read<MasterAutoReplenishExclude>("WHERE Id_Soko = @0 ORDER BY Id", source.Id);
		// 不足SKU＋設定SKUを起点とする。全倉庫の現在庫は取得しない。
		var shortageStock = Read<SummaryRealStock>("WHERE Id_Soko = @0 AND Su < ReserveQty ORDER BY Id", source.Id);
		var keys = settings.Select(Sku).Union(shortageStock.Select(Sku)).ToHashSet();
		var result = new AutoReplenishResult();
		var productIds = Ids(keys.Select(k => k.Id_Shohin));
		var storeIds = settings.Select(s => s.Id_Tenpo).Append(source.Id).Distinct().ToArray();
		var products = productIds.Length == 0 ? [] : Read<MasterShohin>($"WHERE Id IN ({productIds}) ORDER BY Id");
		var variants = productIds.Length == 0 ? [] : Read<DerivedShohinColSiz>($"WHERE Id_Shohin IN ({productIds}) ORDER BY Id");
		var shops = Read<MasterTokui>($"WHERE Id IN ({Ids(storeIds)}) ORDER BY Id");
		var days = Read<Tran60TanaDate>($"WHERE Id_Shop IN ({Ids(storeIds)}) ORDER BY Id");
		var stock = productIds.Length == 0 ? [] : Read<SummaryRealStock>($"WHERE Id_Soko IN ({Ids(storeIds)}) AND Id_Shohin IN ({productIds}) ORDER BY Id");
		var transit = productIds.Length == 0 ? [] : Read<SummaryStock>($"WHERE Id_Soko IN ({Ids(storeIds)}) AND Id_Shohin IN ({productIds}) ORDER BY Id");
		var allocations = productIds.Length == 0 ? [] : Read<TranHaibun>($"WHERE EndFlag = 0 AND Id_Shohin IN ({productIds}) AND (Id_Soko = @0 OR Id_Tenpo IN ({Ids(storeIds)})) ORDER BY Id", source.Id);
		if (allocations.Any(a => a.RelateNo2 > 0)) result.Errors.Add("未完了配分に生成伝票リンクが残っています。配分・積送の状態を確認してください。");
		if (productIds.Length > 0 && _db.FetchDialect<long>("SELECT Id FROM Tran13Hachu WHERE Id_Soko = @0 AND EndFlag = 0 AND Kubun BETWEEN 10 AND 19 AND DenDay <= @1 AND NOT json_valid(Jmeisai)", source.Id, day).Count > 0)
			result.Errors.Add("既存発注明細に不正JSONがあります。明細を確認してください。");
		var hachus = productIds.Length == 0 ? [] : Read<Tran13Hachu>("WHERE Id_Soko = @0 AND EndFlag = 0 AND Kubun BETWEEN 10 AND 19 AND DenDay <= @1 AND json_valid(Jmeisai) ORDER BY Id", source.Id, day);
		// 店舗入荷待ちの仕入配分は他倉庫由来もあり、関連発注の有効性を別途まとめて検査する。
		var relatedIds = allocations.Where(a => a.Kubun == 0 && a.RelateNo1 > 0).Select(a => (long)a.RelateNo1).ToArray();
		if (relatedIds.Length > 0 && _db.FetchDialect<long>($"SELECT Id FROM Tran13Hachu WHERE Id IN ({Ids(relatedIds)}) AND NOT json_valid(Jmeisai)").Count > 0)
			result.Errors.Add("仕入配分の関連発注明細に不正JSONがあります。明細を確認してください。");
		var related = relatedIds.Length == 0 ? [] : Read<Tran13Hachu>($"WHERE Id IN ({Ids(relatedIds)}) AND json_valid(Jmeisai) ORDER BY Id");
		var purchaseAllocations = relatedIds.Length == 0 ? [] : Read<TranHaibun>($"WHERE EndFlag = 0 AND Kubun = 0 AND RelateNo1 IN ({Ids(relatedIds)}) ORDER BY Id");
		var arrivalShopIds = purchaseAllocations.Select(a => a.Id_Tenpo).Except(storeIds).ToArray();
		var arrivalShops = arrivalShopIds.Length == 0 ? [] : Read<MasterTokui>($"WHERE Id IN ({Ids(arrivalShopIds)}) ORDER BY Id");
		var allOrderIds = hachus.Select(h => h.Id).Union(related.Select(h => h.Id)).ToArray();
		if (allOrderIds.Length > 0 && _db.FetchDialect<long>($"SELECT Id FROM Tran03Shiire WHERE RelateNo1 IN ({Ids(allOrderIds)}) AND NOT json_valid(Jmeisai)").Count > 0)
			result.Errors.Add("関連仕入明細に不正JSONがあります。明細を確認してください。");
		var shiire = allOrderIds.Length == 0 ? [] : Read<Tran03Shiire>($"WHERE RelateNo1 IN ({Ids(allOrderIds)}) AND json_valid(Jmeisai) ORDER BY Id");
		var supplierIds = products.Select(p => p.Id_ConsignmentShiire).Where(id => id > 0).ToArray();
		var suppliers = supplierIds.Length == 0 ? [] : Read<MasterShiire>($"WHERE Id IN ({Ids(supplierIds)}) ORDER BY Id");
		var sysman = Read<MasterSysman>("WHERE Id = 1").SingleOrDefault() ?? throw new ArgumentException("自社設定（Id=1）が存在しません。");
		var jodai = productIds.Length == 0 ? new Dictionary<long, int>() : _db.FetchDialect<ResolvedPrice>(
			$"SELECT sh.Id, {DerivedJodai.FinalJodaiSql("sh.Id", "@0", "@1", "@2", "sh")} AS Jodai FROM MasterShohin sh WHERE sh.Id IN ({productIds}) ORDER BY sh.Id",
			(int)EnumJodaiTaisho.Honbu, 0L, day).ToDictionary(r => r.Id, r => r.Jodai);
		var productMap = products.ToDictionary(p => p.Id);
		var shopMap = shops.ToDictionary(s => s.Id);
		var supplierMap = suppliers.ToDictionary(s => s.Id);
		var stockMap = stock.ToDictionary(s => (s.Id_Soko, Sku(s)));
		var transitMap = transit.GroupBy(s => (s.Id_Soko, Sku(s))).ToDictionary(g => g.Key, g => Sum(g.Select(s => (long)s.TransitQty)));
		var dayMap = days.ToDictionary(s => s.Id_Shop);
		var relatedMap = related.ToDictionary(h => h.Id);
		var arrivalShopCodes = shops.Concat(arrivalShops).ToDictionary(s => s.Id, s => s.Code);
		var receivedByOrder = shiire.SelectMany(s => (s.Jmeisai ?? []).Select(m => (Key: ((long)s.RelateNo1, Sku(m)), Su: checked((long)m.Su * s.CalcFlag))))
			.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => Sum(g.Select(m => m.Su)));
		var unarrivedSupply = new Dictionary<long, long>();
		foreach (var group in purchaseAllocations.GroupBy(a => ((long)a.RelateNo1, Sku(a)))) {
			if (!relatedMap.TryGetValue(group.Key.Item1, out var order) || order.EndFlag != 0 || order.Kubun is < 10 or > 19) continue;
			var remaining = Math.Max(0, checked(Sum((order.Jmeisai ?? []).Where(m => Sku(m) == group.Key.Item2).Select(m => (long)m.Su)) - receivedByOrder.GetValueOrDefault(group.Key)));
			foreach (var allocation in group.Where(a => a.Id_Soko == order.Id_Soko)
				.OrderBy(a => arrivalShopCodes.GetValueOrDefault(a.Id_Tenpo, string.Empty), StringComparer.Ordinal).ThenBy(a => a.Id)) {
				var supply = Math.Min(remaining, Math.Max(0L, checked((long)allocation.Su - allocation.ArrivedSu)));
				unarrivedSupply[allocation.Id] = supply; remaining = checked(remaining - supply);
			}
		}
		var excluded = excludes.Where(e => e.Excluded == 1).Select(Sku).ToHashSet();
		var pending = new Dictionary<AutoReplenishSku, long>();
		foreach (var hachu in hachus) {
			foreach (var group in (hachu.Jmeisai ?? []).Where(m => keys.Contains(Sku(m))).GroupBy(Sku)) {
				var rest = Math.Max(0, checked(Sum(group.Select(m => (long)m.Su)) - receivedByOrder.GetValueOrDefault((hachu.Id, group.Key))));
				var bound = Sum(allocations.Where(a => a.Kubun == 0 && a.RelateNo1 == hachu.Id && a.Id_Soko == source.Id && Sku(a) == group.Key)
					.Select(a => Math.Max(0L, checked((long)a.Su - a.ArrivedSu))));
				pending[group.Key] = checked(pending.GetValueOrDefault(group.Key) + Math.Max(0, checked(rest - bound)));
			}
		}
		foreach (var key in keys.OrderBy(k => k.Id_Shohin).ThenBy(k => k.Id_Col).ThenBy(k => k.Id_Siz)) {
			var product = productMap.GetValueOrDefault(key.Id_Shohin);
			var variant = variants.FirstOrDefault(v => v.Id_Shohin == key.Id_Shohin && v.Id_Col == key.Id_Col && v.Id_Siz == key.Id_Siz);
			var inputs = new List<AutoReplenishStoreInput>();
			foreach (var setting in settings.Where(s => Sku(s) == key)) {
				var shop = shopMap.GetValueOrDefault(setting.Id_Tenpo);
				string reason = setting.Enabled != 1 ? "設定無効" : excluded.Contains(key) ? "SKU除外" : shop?.TenType != 6 || shop.IsZaiko != 1 ? "在庫管理する直営店以外" :
					!dayMap.TryGetValue(setting.Id_Tenpo, out var schedule) || (schedule.AutoHoju & (1 << (int)DateTime.ParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture).DayOfWeek)) == 0 ? "補充曜日対象外・未設定" : string.Empty;
				if (reason.Length > 0) { result.Rows.Add(Display(new TranHoju { Id_Tenpo = setting.Id_Tenpo }, key, reason)); continue; }
				if (setting.TargetSu < 0 || setting.Priority < 0) { result.Errors.Add("店舗設定に負の基準数・優先順位があります。"); continue; }
				var s = stockMap.GetValueOrDefault((setting.Id_Tenpo, key));
				if (allocations.Any(a => a.Kubun == 0 && a.Id_Tenpo == setting.Id_Tenpo && Sku(a) == key && string.IsNullOrEmpty(a.KakuteiDay)
					&& (long)a.Su - a.ArrivedSu > unarrivedSupply.GetValueOrDefault(a.Id)))
					result.Rows.Add(Display(new TranHoju { Id_Tenpo = setting.Id_Tenpo }, key, "仕入配分の未入荷枠が有効発注残を超えるため、供給予定を制限しました"));
				var incoming = Sum(allocations.Where(a => a.Id_Tenpo == setting.Id_Tenpo && a.Id_Soko != a.Id_Tenpo && a.Kubun is 0 or 1 or 2 && Sku(a) == key)
					.Select(a => {
						if (!string.IsNullOrEmpty(a.KakuteiDay)) return (long)a.JitsuSu;
						if (a.Kubun != 0) return (long)a.Su;
						return checked((long)a.ArrivedSu + unarrivedSupply.GetValueOrDefault(a.Id));
					}));
				inputs.Add(new(setting.Id_Tenpo, shop!.Code, setting.Priority, setting.TargetSu, s?.Su ?? 0, s?.ReserveQty ?? 0, incoming,
					transitMap.GetValueOrDefault((setting.Id_Tenpo, key))));
			}
			if (excluded.Contains(key)) {
				if (shortageStock.Any(s => Sku(s) == key)) result.Rows.Add(Display(new TranHoju(), key, "SKU除外"));
				continue;
			}
			var sourceStock = stockMap.GetValueOrDefault((source.Id, key));
			foreach (var quantity in AutoReplenishCalculator.Calculate(sourceStock?.Su ?? 0, sourceStock?.ReserveQty ?? 0,
				pending.GetValueOrDefault(key), transitMap.GetValueOrDefault((source.Id, key)), inputs)) {
				var supplier = product == null ? null : supplierMap.GetValueOrDefault(product.Id_ConsignmentShiire);
				var row = new TranHoju { Id_Tenpo = quantity.Id_Tenpo, DemandSu = quantity.DemandSu, TransferSu = quantity.TransferSu,
					CoveredSu = quantity.CoveredSu, Su = quantity.Su, Tanka = product?.TankaGenka ?? 0, Gedai = product?.TankaGenka ?? 0,
					Jodai = jodai.GetValueOrDefault(key.Id_Shohin), Id_Shiire = supplier?.Id ?? 0, Id_Shain = param.Id_Shain,
					NouhinDay = DateTime.ParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture).AddDays(supplier?.LeadTimeDays ?? 0).ToString("yyyyMMdd"),
					Kingaku = checked((int)checked((long)quantity.Su * (product?.TankaGenka ?? 0))) };
				var item = Display(row, key, string.Empty);
				var own = inputs.FirstOrDefault(s => s.Id_Tenpo == quantity.Id_Tenpo);
				item.RealSu = own?.RealSu ?? sourceStock?.Su ?? 0;
				item.ReserveQty = own?.ReserveSu ?? sourceStock?.ReserveQty ?? 0;
				item.TargetSu = own?.TargetSu ?? 0;
				item.IncomingSu = own?.ArrivalSu ?? pending.GetValueOrDefault(key);
				item.TransitSu = own?.TransitSu ?? transitMap.GetValueOrDefault((source.Id, key));
				if (product == null || variant == null || product.IsZaiko != 1) item.Reason = "在庫管理する商品・SKUが存在しません";
				else if (quantity.Su > 0 && product.PurchaseType != 0) item.Reason = "追加発注は通常仕入（仕入区分0）のみ対象です";
				else if (quantity.Su > 0 && supplier == null) item.Reason = "補充発注先（委託仕入先）が未設定・存在しません";
				if (item.Reason.Length > 0) result.Errors.Add($"{item.ProductCode}/{item.ColorCode}/{item.SizeCode}: {item.Reason}");
				result.Rows.Add(item);
			}

			AutoReplenishRow Display(TranHoju row, AutoReplenishSku sku, string reason) {
				row.DenDay = day; row.Id_Soko = source.Id; row.Id_Shohin = sku.Id_Shohin; row.Id_Col = sku.Id_Col; row.Id_Siz = sku.Id_Siz;
				row.Kubun = 15; row.JanCode = variant?.Jan1 ?? string.Empty;
				var shop = shopMap.GetValueOrDefault(row.Id_Tenpo);
				return new AutoReplenishRow { Row = row, ShopCode = shop?.Code ?? string.Empty, ShopName = shop?.Name ?? "倉庫不足",
					ProductCode = product?.Code ?? sku.Id_Shohin.ToString(), ProductName = product?.Name ?? string.Empty,
					ColorCode = variant?.Code_Col ?? sku.Id_Col.ToString(), SizeCode = variant?.Code_Siz ?? sku.Id_Siz.ToString(),
					ShiireName = supplierMap.GetValueOrDefault(row.Id_Shiire)?.Name ?? string.Empty, Reason = reason };
			}
		}
		// 追加・削除も集合で検出。価格の実効値・税率・関係マスタも再検査する。
		var snapshot = Common.SerializeObject(new { day, source, employee, settings, excludes, shortageStock, shops, days, stock, transit, allocations, purchaseAllocations, arrivalShops, hachus, related, shiire, products, variants, suppliers, sysman, jodai });
		result.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
		return result;
	}

	private static long Sum(IEnumerable<long> values) { long total = 0; foreach (var value in values) total = checked(total + value); return total; }

	public AutoReplenishResult Save(AutoReplenishParam param) => Transaction(() => {
		if (!Guid.TryParse(param.ExecutionKey, out var key)) throw new ArgumentException("実行キーが不正です。");
		var executionKey = key.ToString();
		var existing = Read<TranAutoReplenishBatch>("WHERE ExecutionKey = @0", executionKey).SingleOrDefault();
		if (existing != null) {
			if (existing.Id_Soko != param.Id_Soko || existing.DenDay != param.DenDay || existing.Fingerprint != param.Fingerprint)
				throw new AutoReplenishConflictException("実行キーが別の補充条件で使用されています。");
			return Load(existing.Id);
		}
		if (Read<TranAutoReplenishBatch>("WHERE Id_Soko = @0 AND Status = 0", param.Id_Soko).Count > 0)
			throw new AutoReplenishConflictException("この倉庫には未確定の補充があります。読込・取消後に再計算してください。");
		var preview = BuildPreview(param);
		ValidatePreview(preview, param.Fingerprint);
		var rows = preview.Rows.Where(r => r.Row.DemandSu > 0).ToList();
		if (rows.Count == 0) throw new ArgumentException("補充需要がないため保存できません。");
		var batch = new TranAutoReplenishBatch { Id_Soko = param.Id_Soko, DenDay = param.DenDay, ExecutionKey = executionKey,
			ActiveKey = $"source:{param.Id_Soko}", Fingerprint = preview.Fingerprint, Id_Shain = param.Id_Shain, PreviewJson = Common.SerializeObject(preview) };
		Audit(batch, true);
		_db.Insert(batch);
		foreach (var item in rows) { item.Row.Id_Batch = batch.Id; Audit(item.Row, true); _db.Insert(item.Row); }
		batch.PreviewJson = Common.SerializeObject(preview);
		_db.Update(batch);
		return Load(batch.Id);
	});

	private static void ValidatePreview(AutoReplenishResult preview, string fingerprint) {
		if (preview.Errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, preview.Errors.Distinct()));
		if (string.IsNullOrEmpty(fingerprint) || fingerprint != preview.Fingerprint) throw new AutoReplenishConflictException(Recalculate);
	}

	public AutoReplenishResult Load(long batchId) {
		var batch = Read<TranAutoReplenishBatch>("WHERE Id = @0", batchId).SingleOrDefault() ?? throw new ArgumentException("補充バッチが存在しません。");
		var actual = Read<TranHoju>("WHERE Id_Batch = @0 ORDER BY Id", batch.Id);
		var snapshot = string.IsNullOrEmpty(batch.PreviewJson) ? new List<AutoReplenishRow>() : Common.DeserializeObject<AutoReplenishResult>(batch.PreviewJson)?.Rows ?? [];
		var map = actual.ToDictionary(r => (r.Id_Tenpo, Sku(r)));
		foreach (var item in snapshot) if (item.Row.DemandSu > 0 && map.TryGetValue((item.Row.Id_Tenpo, Sku(item.Row)), out var row)) item.Row = row;
		return new AutoReplenishResult { Batch = batch, Rows = snapshot, Fingerprint = batch.Fingerprint,
			CreatedHachuIds = actual.Select(r => r.GeneratedHachuId).Where(id => id > 0).Distinct().ToList(),
			CreatedHaibunIds = actual.Select(r => r.GeneratedHaibunId).Where(id => id > 0).Distinct().ToList() };
	}

	public AutoReplenishResult History(long sokoId) => new() {
		Batches = Read<TranAutoReplenishBatch>(sokoId > 0 ? "WHERE Id_Soko = @0 ORDER BY Id DESC LIMIT 100" : "ORDER BY Id DESC LIMIT 100", sokoId)
	};

	private TranAutoReplenishBatch RequireBatch(AutoReplenishParam param) {
		var batch = Read<TranAutoReplenishBatch>("WHERE Id = @0", param.Id_Batch).SingleOrDefault() ?? throw new ArgumentException("補充バッチが存在しません。");
		if (batch.Id_Soko != param.Id_Soko || !Guid.TryParse(param.ExecutionKey, out var key) || key.ToString() != batch.ExecutionKey)
			throw new AutoReplenishConflictException("補充バッチと実行キーが一致しません。");
		return batch;
	}

	public AutoReplenishResult Cancel(AutoReplenishParam param) => Transaction(() => {
		var batch = RequireBatch(param);
		if (batch.Status == (int)AutoReplenishStatus.Cancelled) return Load(batch.Id);
		if (batch.Status != 0 || batch.Vdu != param.ExpectedVdu) throw new AutoReplenishConflictException("補充状態が更新されています。再読込してください。");
		batch.Status = (int)AutoReplenishStatus.Cancelled; batch.ActiveKey = $"closed:{batch.ExecutionKey}"; Audit(batch, false); _db.Update(batch);
		return Load(batch.Id);
	});

	public AutoReplenishResult Commit(AutoReplenishParam param, Action<BaseDbClass>? onInserted = null) => Transaction(() => {
		var batch = RequireBatch(param);
		if (batch.Status == (int)AutoReplenishStatus.Confirmed) return Load(batch.Id);
		if (batch.Status != 0 || batch.Vdu != param.ExpectedVdu) throw new AutoReplenishConflictException("補充状態が更新されています。再読込してください。");
		var stored = Common.DeserializeObject<AutoReplenishResult>(batch.PreviewJson);
		if (stored?.Fingerprint != batch.Fingerprint) throw new AutoReplenishConflictException("補充の保存時点の検証情報が変更されています。");
		var preview = BuildPreview(param with { DenDay = batch.DenDay, Id_Shain = batch.Id_Shain });
		ValidatePreview(preview, batch.Fingerprint);
		var rows = Read<TranHoju>("WHERE Id_Batch = @0 ORDER BY Id", batch.Id);
		var expected = preview.Rows.Where(r => r.Row.DemandSu > 0).ToDictionary(r => (r.Row.Id_Tenpo, Sku(r.Row)), r => r.Row);
		if (rows.Count != expected.Count) throw new AutoReplenishConflictException("補充明細の所属・数量が変更されています。");
		foreach (var row in rows) {
			if (!expected.TryGetValue((row.Id_Tenpo, Sku(row)), out var e) || row.Id_Soko != batch.Id_Soko || row.DenDay != batch.DenDay
				|| row.DemandSu != e.DemandSu || row.TransferSu != e.TransferSu || row.CoveredSu != e.CoveredSu || row.Su != e.Su
				|| row.Tanka != e.Tanka || row.Jodai != e.Jodai || row.Gedai != e.Gedai || row.Kingaku != e.Kingaku
				|| row.NouhinDay != e.NouhinDay || row.Id_Shiire != e.Id_Shiire || row.GeneratedHachuId != 0 || row.GeneratedHaibunId != 0)
				throw new AutoReplenishConflictException("補充明細の所属・数量が変更されています。");
		}
		var source = CheckSource(batch.Id_Soko);
		var shain = Read<MasterShain>("WHERE Id = @0", batch.Id_Shain).SingleOrDefault();
		var productIds = Ids(rows.Select(r => r.Id_Shohin));
		var products = Read<MasterShohin>($"WHERE Id IN ({productIds})").ToDictionary(p => p.Id);
		var variants = Read<DerivedShohinColSiz>($"WHERE Id_Shohin IN ({productIds})").ToDictionary(v => new AutoReplenishSku(v.Id_Shohin, v.Id_Col, v.Id_Siz));
		var supplierIds = Ids(rows.Where(r => r.Su > 0).Select(r => r.Id_Shiire));
		var suppliers = supplierIds.Length == 0 ? new Dictionary<long, MasterShiire>() : Read<MasterShiire>($"WHERE Id IN ({supplierIds})").ToDictionary(s => s.Id);
		var sysman = Read<MasterSysman>("WHERE Id = 1").SingleOrDefault() ?? new MasterSysman();
		var reserves = new HashSet<ReserveKey>();
		foreach (var row in rows.Where(r => r.TransferSu > 0)) {
			var allocation = new TranHaibun { DenDay = batch.DenDay, NouhinDay = batch.DenDay, Id_Soko = batch.Id_Soko, Id_Tenpo = row.Id_Tenpo,
				Id_Shohin = row.Id_Shohin, Id_Col = row.Id_Col, Id_Siz = row.Id_Siz, JanCode = row.JanCode, Su = row.TransferSu, Kubun = 1,
				Tanka = row.Tanka, Gedai = row.Gedai, Jodai = row.Jodai, Kingaku = checked((int)checked((long)row.TransferSu * row.Tanka)),
				Id_Shain = batch.Id_Shain, Memo = "自動補充" };
			AllocationRules.NormalizeNewRow(allocation); Audit(allocation, true); _db.Insert(allocation);
			row.GeneratedHaibunId = allocation.Id; reserves.Add(ReserveKey.From(allocation)); onInserted?.Invoke(allocation);
		}
		foreach (var group in rows.Where(r => r.Su > 0).GroupBy(r => (r.Id_Shiire, r.NouhinDay))) {
			var supplier = suppliers[group.Key.Id_Shiire];
			var detail = group.GroupBy(Sku).Select((g, i) => {
				var r = g.First(); var p = products[g.Key.Id_Shohin]; var v = variants[g.Key];
				var su = checked((int)Sum(g.Select(x => (long)x.Su)));
				return new Tran99Meisai { No = i + 1, Id_Shohin = p.Id, Code_Shohin = p.Code, Mei_Shohin = p.Name,
					Id_Col = r.Id_Col, Id_Siz = r.Id_Siz, Code_Col = v.Code_Col, Code_Siz = v.Code_Siz, Mei_Col = v.Mei_Col, Mei_Siz = v.Mei_Siz,
					JanCode = r.JanCode, Su = su, Tanka = r.Tanka, Gedai = r.Gedai, Jodai = r.Jodai, Kingaku = checked((long)su * r.Tanka), Id_Tax = p.Id_Tax };
			}).ToList();
			var tax = TaxCalculator.Apply(detail, TaxRateResolver.CreateRateResolver(sysman, batch.DenDay), EnumTaxCalcUnit.Slip, (EnumRounding)supplier.TaxRounding);
			var hachu = new Tran13Hachu { DenDay = batch.DenDay, NouhinDay = group.Key.NouhinDay, Id_Soko = batch.Id_Soko, VSoko = Name(source),
				Id_Shiire = supplier.Id, VShiire = Name(supplier), Id_Shain = batch.Id_Shain, VShain = shain == null ? new() : Name(shain),
				Kubun = 15, Rate = supplier.RateProper, TaxRounding = supplier.TaxRounding, Jmeisai = detail,
				SuTotal = checked((int)Sum(detail.Select(m => (long)m.Su))), KingakuTotal = Sum(detail.Select(m => m.Kingaku)),
				JodaiTotal = Sum(detail.Select(m => checked((long)m.Su * m.Jodai))), GedaiTotal = Sum(detail.Select(m => checked((long)m.Su * m.Gedai))),
				Tax1 = tax.Tax1, Tax2 = tax.Tax2, Tax3 = tax.Tax3, TaxableAmount1 = tax.TaxableAmount1, TaxableAmount2 = tax.TaxableAmount2, TaxableAmount3 = tax.TaxableAmount3,
				Memo = "自動補充" };
			hachu.Total = checked(Math.Abs(hachu.KingakuTotal) + tax.TaxTotal);
			Audit(hachu, true); _db.Insert(hachu); onInserted?.Invoke(hachu);
			foreach (var row in group) row.GeneratedHachuId = hachu.Id;
		}
		if (onInserted == null && reserves.Count > 0) new SummaryDb(_db).CalcHaibun2Reserve(reserves);
		foreach (var row in rows) { row.JitsuSu = row.Su; row.KakuteiDay = batch.DenDay; Audit(row, false); _db.Update(row); }
		batch.Status = (int)AutoReplenishStatus.Confirmed; batch.KakuteiDay = batch.DenDay; batch.ActiveKey = $"closed:{batch.ExecutionKey}";
		Audit(batch, false); _db.Update(batch);
		return Load(batch.Id);
	});

	private static CodeNameView Name(IBaseCodeName row) => new() { Sid = ((BaseDbClass)row).Id, Cd = row.Code, Mei = row.Name };

	public AutoReplenishResult LoadSettings(long sokoId) => new() {
		StockSettings = Read<MasterAutoReplenishStock>(sokoId > 0 ? "WHERE Id_Soko = @0 ORDER BY Id" : "ORDER BY Id", sokoId),
		ExcludeSettings = Read<MasterAutoReplenishExclude>(sokoId > 0 ? "WHERE Id_Soko = @0 ORDER BY Id" : "ORDER BY Id", sokoId)
	};
	private void ValidateSku(long shohin, long col, long siz) {
		if (shohin <= 0 || Read<MasterShohin>("WHERE Id = @0 AND IsZaiko = 1", shohin).Count == 0 ||
			Read<DerivedShohinColSiz>("WHERE Id_Shohin = @0 AND Id_Col = @1 AND Id_Siz = @2", shohin, col, siz).Count != 1)
			throw new ArgumentException("商品・色・サイズの組合せが存在しません。");
	}
	public AutoReplenishResult SaveStockSetting(AutoReplenishParam param) => Transaction(() => {
		var row = param.StockSetting ?? throw new ArgumentException("店舗基準設定が指定されていません。");
		CheckSource(row.Id_Soko); ValidateSku(row.Id_Shohin, row.Id_Col, row.Id_Siz);
		if (row.Id_Tenpo == row.Id_Soko || Read<MasterTokui>("WHERE Id = @0 AND TenType = 6 AND IsZaiko = 1", row.Id_Tenpo).Count != 1 ||
			row.TargetSu < 0 || row.Priority < 0 || row.Enabled is not (0 or 1)) throw new ArgumentException("直営店・基準数・優先順位・有効フラグを確認してください。");
		var duplicate = Read<MasterAutoReplenishStock>("WHERE Id_Tenpo = @0 AND Id_Shohin = @1 AND Id_Col = @2 AND Id_Siz = @3", row.Id_Tenpo, row.Id_Shohin, row.Id_Col, row.Id_Siz).SingleOrDefault();
		SaveSetting(row, param.ExpectedVdu, duplicate);
		return new AutoReplenishResult { StockSettings = [row] };
	});
	public AutoReplenishResult SaveExcludeSetting(AutoReplenishParam param) => Transaction(() => {
		var row = param.ExcludeSetting ?? throw new ArgumentException("SKU除外設定が指定されていません。");
		CheckSource(row.Id_Soko); ValidateSku(row.Id_Shohin, row.Id_Col, row.Id_Siz);
		if (row.Excluded is not (0 or 1)) throw new ArgumentException("除外フラグは0または1を指定してください。");
		var duplicate = Read<MasterAutoReplenishExclude>("WHERE Id_Soko = @0 AND Id_Shohin = @1 AND Id_Col = @2 AND Id_Siz = @3", row.Id_Soko, row.Id_Shohin, row.Id_Col, row.Id_Siz).SingleOrDefault();
		SaveSetting(row, param.ExpectedVdu, duplicate);
		return new AutoReplenishResult { ExcludeSettings = [row] };
	});
	private void SaveSetting<T>(T row, long expectedVdu, T? duplicate) where T : BaseDbClass {
		if (duplicate != null && duplicate.Id != row.Id) throw new AutoReplenishConflictException("同じキーの設定があります。再読込してください。");
		if (row.Id == 0) { Audit(row, true); _db.Insert(row); }
		else {
			var current = Read<T>("WHERE Id = @0", row.Id).SingleOrDefault();
			if (current == null || current.Vdu != expectedVdu) throw new AutoReplenishConflictException("設定が更新されています。再読込してください。");
			row.Vdc = current.Vdc; row.Vdu = current.Vdu; Audit(row, false); _db.Update(row);
		}
	}
	private sealed class ResolvedPrice { public long Id { get; set; } public int Jodai { get; set; } }
}
