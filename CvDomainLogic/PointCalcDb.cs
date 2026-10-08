using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 店舗売上のポイント付与計算と台帳(TranPointEvent)・残高の同期。
/// <para>
/// 台帳は追記のみ。伝票ごとに「有効な付与(取消されていない付与)」と現在の伝票から計算した付与を比べ、
/// 異なれば有効な付与を取消して新しい付与を追記する。同じなら何もしないため、再実行しても二重計上しない。
/// ベース+ランク条件とキャンペーン(明細ごとに 商品店別→商品全店→店別→全店 の先頭を適用し、ベース・ランクを置換)を計算する。
/// ボーナス条件と利用ポイント控除は対象外。
/// </para>
/// <para>
/// 残高は SummaryPoint.Point を台帳の合計から作り直し、MasterEndCustomerAccount.Point へは追記した増減を加算する
/// (移行時の旧ポイントを保持するため上書きしない)。
/// </para>
/// </summary>
public sealed class PointCalcDb(ExDatabase db) {
	private readonly ILogger<PointCalcDb> _logger = new NLogExtender<PointCalcDb>();
	private const string ProcessNameRecalc = "ポイント再計算";
	private const long ExpectedDurationRecalcSeconds = 900; // 指定月の店舗売上のみのため15分

	private List<MasterPointBase>? _bases;
	private Dictionary<(long IdBase, int Kubun), MasterPointRank>? _ranks;
	private List<MasterPointCampaign>? _campaigns;
	private readonly Dictionary<long, HashSet<long>> _campaignShops = [];
	private readonly Dictionary<long, HashSet<long>> _campaignShohins = [];
	private readonly Dictionary<long, int?> _rankKubunByCustomer = [];

	/// <summary>ポイント付与対象の店舗売上区分（P/S売上・社販と各返品）。消費税は対象外</summary>
	public static bool IsTargetKubun(int kubun) => kubun is (int)EnumUri01.Uriage or (int)EnumUri01.UriSale or (int)EnumUri01.UriShahan
		or (int)EnumUri01.Henpin or (int)EnumUri01.HenSale or (int)EnumUri01.HenShahan;

	/// <summary>
	/// 店舗売上の追加・修正・削除に合わせて台帳と残高を同期する。呼出元のトランザクション内で使用する。
	/// </summary>
	/// <param name="idTenuri">店舗売上伝票Id</param>
	/// <param name="slip">保存後の伝票。削除では null</param>
	/// <returns>追記した台帳行数</returns>
	public int SyncTenuri(long idTenuri, Tran01Tenuri? slip) {
		var events = db.FetchDialect<TranPointEvent>("SELECT * FROM TranPointEvent WHERE Id_Tenuri=@0 ORDER BY Id", idTenuri);
		var deltas = new Dictionary<long, long>();
		var count = Sync(idTenuri, slip, events, deltas);
		ApplyBalance(deltas);
		return count;
	}

	/// <summary>
	/// 指定年月(yyyyMM)の店舗売上のポイントを再計算する。マスタ変更・取込等で台帳と伝票がずれた分を取消・付与で補正する。
	/// </summary>
	/// <returns>追記した台帳行数</returns>
	public int Recalc(string fromYyyymm, string toYyyymm) {
		var dayFrom = fromYyyymm + "01";
		var dayTo = toYyyymm + "31";
		var count = 0;
		db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		try {
			LoadMasters();
			var hasBase = _bases!.Any(x => string.CompareOrdinal(x.DayFrom, dayTo) <= 0 && string.CompareOrdinal(x.DayTo, dayFrom) >= 0);
			const string eventInRange = "SELECT * FROM TranPointEvent WHERE Id_Tenuri IN (SELECT Id FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0) OR (DenDay BETWEEN @0 AND @1 AND Id_Tenuri>0) ORDER BY Id";
			var events = db.FetchDialect<TranPointEvent>(eventInRange, dayFrom, dayTo);
			if (!hasBase && events.Count == 0) {
				db.CompleteTransaction();
				return 0;
			}
			foreach (var row in db.FetchDialect<MasterEndCustomerAccount>(
				"SELECT Id_Customer, PointRank FROM MasterEndCustomerAccount WHERE Id_Customer IN (SELECT Id_Customer FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0)", dayFrom, dayTo)) {
				_rankKubunByCustomer[row.Id_Customer] = ParseRank(row.PointRank);
			}
			var slips = db.FetchDialect<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0", dayFrom, dayTo)
				.ToDictionary(x => x.Id);
			var eventsBySlip = events.GroupBy(x => x.Id_Tenuri).ToDictionary(x => x.Key, x => x.ToList());
			var deltas = new Dictionary<long, long>();
			foreach (var id in slips.Keys.Union(eventsBySlip.Keys).Order()) {
				// 台帳だけにある伝票は削除・顧客解除・日付移動の可能性があるため、現在の行を読み直す
				var slip = slips.GetValueOrDefault(id)
					?? db.FetchDialect<Tran01Tenuri>("SELECT * FROM Tran01Tenuri WHERE Id=@0", id).FirstOrDefault();
				count += Sync(id, slip, eventsBySlip.GetValueOrDefault(id) ?? [], deltas);
			}
			ApplyBalance(deltas);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		return count;
	}

	/// <summary>ポイント再計算（手動・自動実行）。マニュアル排他を取得して実行する</summary>
	public IAsyncEnumerable<StreamStepProgress> RecalcAsyncStream(CalcDateTermParameter param, int isAutoExec = (int)EmSysHistType.ManualExec) {
		(string Name, Func<CalcDateTermParameter, int> Action)[] steps = [
			($"Point : Recalc {param.DateYymmFrom}-{param.DateYymmTo}", p => Recalc(p.DateYymmFrom, p.DateYymmTo)),
		];
		return StreamStepProgressRunner.Run(
			steps,
			param,
			_logger,
			"処理開始",
			"処理エラー: {StepName}",
			"処理終了",
			new ManualLockDb(db),
			ProcessNameRecalc,
			ExpectedDurationRecalcSeconds,
			isAutoExec);
	}

	/// <summary>伝票1件の付与を計算する。付与対象外・0ポイントは null</summary>
	public TranPointEvent? Calc(Tran01Tenuri? slip) {
		if (slip == null || slip.Id_Customer <= 0 || !IsTargetKubun(slip.Kubun)) {
			return null;
		}
		LoadMasters();
		// 制度コードごとに有効版の期間重複は禁止されている。複数制度が同時に有効な場合はコード順の先頭を適用する
		var pointBase = _bases!.FirstOrDefault(x => string.CompareOrdinal(x.DayFrom, slip.DenDay) <= 0 && string.CompareOrdinal(x.DayTo, slip.DenDay) >= 0);
		if (pointBase == null) {
			return null;
		}
		var rankKubun = GetRankKubun(slip.Id_Customer);
		var rank = rankKubun is int k ? _ranks!.GetValueOrDefault((pointBase.Id, k)) : null;
		var unitPrice = rank?.PointUnitPrice ?? pointBase.PointUnitPrice;
		var amountProper = rank?.PointAmountProper ?? pointBase.PointAmountProper;
		var amountSale = rank?.PointAmountSale ?? pointBase.PointAmountSale;
		var inclusive = pointBase.TaxBasis == (int)EnumPointTaxBasis.Inclusive;
		var campaigns = GetCampaigns(pointBase.Id, slip.DenDay, rankKubun);
		LoadTargets(campaigns);

		long properKingaku = 0, saleKingaku = 0, points = 0;
		// 伝票単位は単価ごとに 金額×付与数 を合計し、最後に1回だけ丸める
		var numeratorByUnit = new Dictionary<long, decimal>();
		var applied = new Dictionary<long, CampaignApplied>();
		foreach (var line in slip.Jmeisai ?? []) {
			var kingaku = line.Kingaku + (inclusive ? line.Tax : 0);
			var isSale = line.Kubun == 1;
			if (isSale) saleKingaku += kingaku; else properKingaku += kingaku;
			// キャンペーンが該当した明細はキャンペーンの単価・付与数でベース・ランクを置換する
			var campaign = FindCampaign(campaigns, slip.Id_Tenpo, line.Id_Shohin);
			var lineUnit = campaign?.PointUnitPrice ?? unitPrice;
			var lineAmount = isSale ? campaign?.PointAmountSale ?? amountSale : campaign?.PointAmountProper ?? amountProper;
			if (campaign != null) {
				var a = applied.TryGetValue(campaign.Id, out var x) ? x : applied[campaign.Id] = new CampaignApplied(campaign);
				if (isSale) a.SaleKingaku += kingaku; else a.ProperKingaku += kingaku;
			}
			if (pointBase.CalcUnit == (int)EnumPointCalcUnit.Detail) {
				points += Round((decimal)kingaku * lineAmount / lineUnit, pointBase.Rounding);
			}
			else {
				numeratorByUnit[lineUnit] = numeratorByUnit.GetValueOrDefault(lineUnit) + (decimal)kingaku * lineAmount;
			}
		}
		if (pointBase.CalcUnit != (int)EnumPointCalcUnit.Detail && numeratorByUnit.Count > 0) {
			// 単価が複数でも割り算の誤差で丸めがずれないよう、最小公倍数で通分して1回だけ割る
			var lcm = numeratorByUnit.Keys.Aggregate(1L, (l, u) => checked(l / Gcd(l, u) * u));
			points = Round(numeratorByUnit.Sum(x => x.Value * (lcm / x.Key)) / lcm, pointBase.Rounding);
		}
		// 返品は同じ規則で計算し、CalcFlag で負の付与にする
		var calcFlag = TranCalcBase.GetKubunCalcFlag(slip.Kubun);
		points *= calcFlag;
		if (points == 0) {
			return null;
		}
		return new TranPointEvent {
			DenDay = slip.DenDay,
			Id_Customer = slip.Id_Customer,
			Id_Tenpo = slip.Id_Tenpo,
			Id_Tenuri = slip.Id,
			EventType = (int)EnumPointEventType.Grant,
			PointDelta = points,
			Id_PointBase = pointBase.Id,
			Id_PointRank = rank?.Id ?? 0,
			SourceVdu = slip.Vdu,
			Jcalc = Common.SerializeObject(new {
				Rule = applied.Count == 0 ? "Base" : "Campaign",
				pointBase.Code,
				pointBase.Version,
				RankKubun = rank?.Kubun,
				UnitPrice = unitPrice,
				AmountProper = amountProper,
				AmountSale = amountSale,
				pointBase.TaxBasis,
				pointBase.CalcUnit,
				pointBase.Rounding,
				ProperKingaku = properKingaku,
				SaleKingaku = saleKingaku,
				CalcFlag = calcFlag,
				Point = points,
				Campaigns = applied.Count == 0 ? null : applied.Values.Select(x => new {
					x.Campaign.Id,
					x.Campaign.Code,
					x.Campaign.PriorityType,
					x.Campaign.RankKubun,
					UnitPrice = x.Campaign.PointUnitPrice,
					AmountProper = x.Campaign.PointAmountProper,
					AmountSale = x.Campaign.PointAmountSale,
					x.ProperKingaku,
					x.SaleKingaku,
				}).ToList(),
			}),
		};
	}

	private sealed class CampaignApplied(MasterPointCampaign campaign) {
		public MasterPointCampaign Campaign { get; } = campaign;
		public long ProperKingaku { get; set; }
		public long SaleKingaku { get; set; }
	}

	/// <summary>
	/// 伝票日・ベース版・会員ランクで適用可能なキャンペーンを優先順(商品店別→商品全店→店別→全店)に返す。
	/// 同じ優先区分の重複は保存時に禁止しているが、旧データ等で残る場合はランク指定ありを先、次にコード順とする。
	/// </summary>
	private List<MasterPointCampaign> GetCampaigns(long idBase, string denDay, int? rankKubun) =>
		[.. _campaigns!
			.Where(x => x.Id_PointBase == idBase && string.CompareOrdinal(x.DayFrom, denDay) <= 0 && string.CompareOrdinal(x.DayTo, denDay) >= 0
				&& (x.RankKubun == 0 || x.RankKubun == rankKubun))
			.OrderByDescending(x => x.PriorityType).ThenBy(x => x.RankKubun == 0).ThenBy(x => x.Code, StringComparer.Ordinal)];

	/// <summary>明細に最初に該当するキャンペーン。対象店舗・商品が未設定の店別・商品系は適用しない</summary>
	private MasterPointCampaign? FindCampaign(List<MasterPointCampaign> campaigns, long idTenpo, long idShohin) =>
		campaigns.FirstOrDefault(x => (EnumPointCampaignPriority)x.PriorityType switch {
			EnumPointCampaignPriority.AllShops => true,
			EnumPointCampaignPriority.Shop => InTarget(_campaignShops, x.Id, idTenpo),
			EnumPointCampaignPriority.ShohinAllShops => InTarget(_campaignShohins, x.Id, idShohin),
			EnumPointCampaignPriority.ShohinShop => InTarget(_campaignShohins, x.Id, idShohin) && InTarget(_campaignShops, x.Id, idTenpo),
			_ => false,
		});

	private static bool InTarget(Dictionary<long, HashSet<long>> targets, long idCampaign, long id) =>
		id > 0 && targets.TryGetValue(idCampaign, out var set) && set.Contains(id);

	private static long Gcd(long a, long b) {
		while (b != 0) (a, b) = (b, a % b);
		return a;
	}

	private int Sync(long idTenuri, Tran01Tenuri? slip, List<TranPointEvent> events, Dictionary<long, long> deltas) {
		var expected = Calc(slip);
		var cancelled = events.Where(x => x.EventType == (int)EnumPointEventType.Cancel).Select(x => x.Id_OriginalEvent).ToHashSet();
		var active = events.Where(x => x.EventType == (int)EnumPointEventType.Grant && x.Id_PointBonus == 0 && !cancelled.Contains(x.Id)).ToList();
		if (expected != null && active.Count == 1 && SameGrant(active[0], expected)) {
			return 0;
		}
		if (expected == null && active.Count == 0) {
			return 0;
		}
		var vdate = Common.GetVdate();
		var count = 0;
		foreach (var org in active) {
			Insert(new TranPointEvent {
				EventKey = $"TENURI:{idTenuri}:C:{org.Id}",
				DenDay = org.DenDay,
				Id_Customer = org.Id_Customer,
				Id_Tenpo = org.Id_Tenpo,
				Id_Tenuri = idTenuri,
				EventType = (int)EnumPointEventType.Cancel,
				PointDelta = -org.PointDelta,
				Id_OriginalEvent = org.Id,
				Id_PointBase = org.Id_PointBase,
				Id_PointRank = org.Id_PointRank,
				SourceVdu = slip?.Vdu ?? 0,
				Memo = slip == null ? "店舗売上削除" : "店舗売上訂正",
			}, vdate, deltas);
			count++;
		}
		if (expected != null) {
			var grantCount = events.Count(x => x.EventType == (int)EnumPointEventType.Grant);
			expected.EventKey = $"TENURI:{idTenuri}:G:{expected.SourceVdu}:{grantCount}";
			expected.Memo = "店舗売上付与";
			Insert(expected, vdate, deltas);
			count++;
		}
		return count;
	}

	private void Insert(TranPointEvent item, long vdate, Dictionary<long, long> deltas) {
		item.Vdc = vdate;
		item.Vdu = vdate;
		db.Insert(item);
		deltas[item.Id_Customer] = deltas.GetValueOrDefault(item.Id_Customer) + item.PointDelta;
	}

	private static bool SameGrant(TranPointEvent a, TranPointEvent b) =>
		a.PointDelta == b.PointDelta && a.Id_Customer == b.Id_Customer && a.DenDay == b.DenDay && a.Id_Tenpo == b.Id_Tenpo
		&& a.Id_PointBase == b.Id_PointBase && a.Id_PointRank == b.Id_PointRank;

	/// <summary>SummaryPoint は台帳合計から作り直し、会員情報の現在ポイントへは増減を加算する</summary>
	private void ApplyBalance(Dictionary<long, long> deltas) {
		if (deltas.Count == 0) {
			return;
		}
		var vdate = Common.GetVdate();
		foreach (var (idCustomer, delta) in deltas) {
			var total = db.FetchDialect<long>("SELECT COALESCE(SUM(PointDelta),0) FROM TranPointEvent WHERE Id_Customer=@0", idCustomer).FirstOrDefault();
			if (db.ExecuteDialect("UPDATE SummaryPoint SET Point=@0, Vdu=@1 WHERE Id_Customer=@2", checked((int)total), vdate, idCustomer) == 0) {
				db.Insert(new SummaryPoint { Id_Customer = checked((int)idCustomer), Point = checked((int)total), Vdc = vdate, Vdu = vdate });
			}
			if (delta != 0) {
				db.ExecuteDialect("UPDATE MasterEndCustomerAccount SET Point=Point+@0, Vdu=@1 WHERE Id_Customer=@2", delta, vdate, idCustomer);
			}
		}
	}

	private void LoadMasters() {
		_bases ??= db.FetchDialect<MasterPointBase>("SELECT * FROM MasterPointBase WHERE IsEnabled=1 ORDER BY Code, Version");
		_ranks ??= db.FetchDialect<MasterPointRank>("SELECT * FROM MasterPointRank WHERE Id_PointBase>0")
			.ToDictionary(x => (x.Id_PointBase, x.Kubun));
		_campaigns ??= db.FetchDialect<MasterPointCampaign>("SELECT * FROM MasterPointCampaign WHERE IsEnabled=1 AND Id_PointBase>0");
	}

	/// <summary>対象店舗・商品は伝票保存ごとに全件読まないよう、適用候補のキャンペーン分だけ読み込んでキャッシュする</summary>
	private void LoadTargets(List<MasterPointCampaign> campaigns) {
		var ids = campaigns.Where(x => x.PriorityType != (int)EnumPointCampaignPriority.AllShops && !_campaignShops.ContainsKey(x.Id)).Select(x => x.Id).ToList();
		if (ids.Count == 0) {
			return;
		}
		foreach (var id in ids) {
			_campaignShops[id] = [];
			_campaignShohins[id] = [];
		}
		var inList = string.Join(",", ids);
		foreach (var row in db.FetchDialect<MasterPointCampaignShop>($"SELECT Id_PointCampaign, Id_Tenpo FROM MasterPointCampaignShop WHERE Id_PointCampaign IN ({inList})")) {
			_campaignShops[row.Id_PointCampaign].Add(row.Id_Tenpo);
		}
		foreach (var row in db.FetchDialect<MasterPointCampaignShohin>($"SELECT Id_PointCampaign, Id_Shohin FROM MasterPointCampaignShohin WHERE Id_PointCampaign IN ({inList})")) {
			_campaignShohins[row.Id_PointCampaign].Add(row.Id_Shohin);
		}
	}

	/// <summary>会員のPointRank(数値文字列)をランクコードとして扱う。会員情報なし・数値でなければベース条件</summary>
	private int? GetRankKubun(long idCustomer) {
		if (!_rankKubunByCustomer.TryGetValue(idCustomer, out var kubun)) {
			var rank = db.FetchDialect<string>("SELECT PointRank FROM MasterEndCustomerAccount WHERE Id_Customer=@0", idCustomer).FirstOrDefault();
			_rankKubunByCustomer[idCustomer] = kubun = ParseRank(rank);
		}
		return kubun;
	}

	private static int? ParseRank(string? value) => int.TryParse(value?.Trim(), out var k) ? k : null;

	private static long Round(decimal value, int rounding) => (long)((EnumRounding)rounding switch {
		EnumRounding.Ceiling => Math.Ceiling(value),
		EnumRounding.Floor => Math.Floor(value),
		_ => Math.Round(value, MidpointRounding.AwayFromZero),
	});
}
