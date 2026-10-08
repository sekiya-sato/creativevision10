using System.Globalization;
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
/// ベース+ランク条件とキャンペーン(明細ごとに 商品店別→商品全店→店別→全店 の先頭を適用し、ベース・ランクを置換)、
/// ボーナス(版ごとに別行)、使用ポイント(EventType=Use、DeductPointUse=する なら付与対象額から控除)を計算する。
/// 伝票の GrantPoint には有効付与の合計を設定する。失効は PointExpireDb。
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
	private List<MasterPointBonus>? _bonuses;
	private string? _fiscalStartMmdd;
	private readonly Dictionary<long, string?> _birthMonthByCustomer = [];
	private readonly Dictionary<long, long> _firstSlipByCustomer = [];

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
		if (slip != null) {
			Require(slip.UsePoint >= 0, "使用ポイントは0以上で指定してください。");
			Require(slip.UsePoint == 0 || slip.Id_Customer > 0 && IsTargetKubun(slip.Kubun), "使用ポイントは顧客を指定した売上・返品伝票だけに指定できます。");
			// 1ポイント=1円のため、使用(返品は戻し)は伝票の税込合計を超えられない
			Require(slip.UsePoint <= (slip.Jmeisai ?? []).Sum(x => x.Kingaku + x.Tax), "使用ポイントは伝票の税込合計以下で指定してください。");
		}
		var events = db.FetchDialect<TranPointEvent>("SELECT * FROM TranPointEvent WHERE Id_Tenuri=@0 ORDER BY Id", idTenuri);
		var deltas = new Dictionary<long, long>();
		var useDeltas = new Dictionary<long, long>();
		var count = Sync(idTenuri, slip, events, deltas, useDeltas);
		ApplyBalance(deltas);
		// 使用の増加で残高が負になる保存は拒否する（呼出元のトランザクションごと戻る）。使用の減少・返品・削除は許可する
		foreach (var (idCustomer, useDelta) in slip == null ? new Dictionary<long, long>() : useDeltas) {
			if (useDelta >= 0) continue;
			var balance = Balance(idCustomer);
			Require(balance >= 0, $"ポイント残高が不足しているため保存できません（保存後残高 {balance}、今回の使用 {-useDelta}）。");
		}
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
			// 対象は範囲内の伝票と、範囲内に台帳がある伝票。伝票日を別の月へ移した伝票も同期の比較・EventKey の連番が
			// ずれないよう、範囲外の台帳を含めてその伝票の全イベントを読む
			const string eventInRange = "SELECT * FROM TranPointEvent WHERE Id_Tenuri IN (SELECT Id FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0)" +
				" OR Id_Tenuri IN (SELECT Id_Tenuri FROM TranPointEvent WHERE DenDay BETWEEN @0 AND @1 AND Id_Tenuri>0) ORDER BY Id";
			var events = db.FetchDialect<TranPointEvent>(eventInRange, dayFrom, dayTo);
			if (!hasBase && events.Count == 0
				&& db.FetchDialect<long>("SELECT COUNT(*) FROM Tran01Tenuri WHERE DenDay BETWEEN @0 AND @1 AND Id_Customer>0 AND UsePoint<>0", dayFrom, dayTo).FirstOrDefault() == 0) {
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
			// 再計算では使用を拒否せず記録し、残高が負になった顧客を警告として残す
			var negative = deltas.Keys.Count(x => Balance(x) < 0);
			if (negative > 0) {
				_logger.LogWarning("ポイント再計算: 残高が負の顧客 {Count}件 ({From}-{To})", negative, fromYyyymm, toYyyymm);
			}
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

	/// <summary>伝票1件の基本付与（ベース・ランク・キャンペーン）を計算する。付与対象外・0ポイントは null</summary>
	public TranPointEvent? Calc(Tran01Tenuri? slip) =>
		CalcExpected(slip).FirstOrDefault(x => x.EventType == (int)EnumPointEventType.Grant && x.Id_PointBonus == 0);

	/// <summary>
	/// 伝票1件の台帳に残すべきイベント（基本付与・ボーナス付与(版ごと)・使用）を計算する。付与対象外は空。
	/// </summary>
	public List<TranPointEvent> CalcExpected(Tran01Tenuri? slip) {
		var result = new List<TranPointEvent>();
		if (slip == null || slip.Id_Customer <= 0 || !IsTargetKubun(slip.Kubun)) {
			return result;
		}
		LoadMasters();
		var calcFlag = TranCalcBase.GetKubunCalcFlag(slip.Kubun);
		// 制度コードごとに有効版の期間重複は禁止されている。複数制度が同時に有効な場合はコード順の先頭を適用する
		var pointBase = _bases!.FirstOrDefault(x => string.CompareOrdinal(x.DayFrom, slip.DenDay) <= 0 && string.CompareOrdinal(x.DayTo, slip.DenDay) >= 0);
		if (pointBase != null) {
			var rankKubun = GetRankKubun(slip.Id_Customer);
			var grant = CalcGrant(slip, pointBase, rankKubun, calcFlag, out var target);
			if (grant != null) result.Add(grant);
			result.AddRange(CalcBonuses(slip, pointBase, rankKubun, calcFlag, target));
		}
		// 使用ポイントはベースの有無に関係なく記録する。売上は減算、返品は戻し
		if (slip.UsePoint != 0) {
			result.Add(new TranPointEvent {
				DenDay = slip.DenDay,
				Id_Customer = slip.Id_Customer,
				Id_Tenpo = slip.Id_Tenpo,
				Id_Tenuri = slip.Id,
				EventType = (int)EnumPointEventType.Use,
				PointDelta = -slip.UsePoint * calcFlag,
				SourceVdu = slip.Vdu,
				Jcalc = Common.SerializeObject(new { Rule = "Use", slip.UsePoint, CalcFlag = calcFlag }),
			});
		}
		return result;
	}

	/// <param name="target">ボーナス判定用の付与対象額（税基準どおり、利用控除後。符号なし）</param>
	private TranPointEvent? CalcGrant(Tran01Tenuri slip, MasterPointBase pointBase, int? rankKubun, int calcFlag, out long target) {
		var rank = rankKubun is int k ? _ranks!.GetValueOrDefault((pointBase.Id, k)) : null;
		var unitPrice = rank?.PointUnitPrice ?? pointBase.PointUnitPrice;
		var amountProper = rank?.PointAmountProper ?? pointBase.PointAmountProper;
		var amountSale = rank?.PointAmountSale ?? pointBase.PointAmountSale;
		var inclusive = pointBase.TaxBasis == (int)EnumPointTaxBasis.Inclusive;
		var campaigns = GetCampaigns(pointBase.Id, slip.DenDay, rankKubun);
		LoadTargets(campaigns);

		// 利用ポイント控除: 付与対象額合計Tに対し各明細へ (T-使用)/T を掛ける。1ポイント=1円。割り算は最後の1回だけにする
		var total = (slip.Jmeisai ?? []).Sum(x => x.Kingaku + (inclusive ? x.Tax : 0));
		var deduct = pointBase.DeductPointUse == (int)EnumYesNo.Yes && slip.UsePoint > 0 && total > 0;
		decimal deductNum = deduct ? Math.Max(0, total - slip.UsePoint) : 1, deductDen = deduct ? total : 1;
		target = deduct ? (long)deductNum : total;

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
				points += Round((decimal)kingaku * lineAmount * deductNum / (lineUnit * deductDen), pointBase.Rounding);
			}
			else {
				numeratorByUnit[lineUnit] = numeratorByUnit.GetValueOrDefault(lineUnit) + (decimal)kingaku * lineAmount;
			}
		}
		if (pointBase.CalcUnit != (int)EnumPointCalcUnit.Detail && numeratorByUnit.Count > 0) {
			// 単価が複数でも割り算の誤差で丸めがずれないよう、最小公倍数で通分して1回だけ割る
			var lcm = numeratorByUnit.Keys.Aggregate(1L, (l, u) => checked(l / Gcd(l, u) * u));
			points = Round(numeratorByUnit.Sum(x => x.Value * (lcm / x.Key)) * deductNum / (lcm * deductDen), pointBase.Rounding);
		}
		// 返品は同じ規則で計算し、CalcFlag で負の付与にする
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
				DeductUsePoint = deduct ? slip.UsePoint : 0,
				TargetKingaku = target,
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

	private int Sync(long idTenuri, Tran01Tenuri? slip, List<TranPointEvent> events, Dictionary<long, long> deltas, Dictionary<long, long>? useDeltas = null) {
		var expected = CalcExpected(slip);
		var cancelled = events.Where(x => x.EventType == (int)EnumPointEventType.Cancel).Select(x => x.Id_OriginalEvent).ToHashSet();
		var active = events.Where(x => x.EventType is (int)EnumPointEventType.Grant or (int)EnumPointEventType.Use && !cancelled.Contains(x.Id)).ToList();
		var vdate = Common.GetVdate();
		var count = 0;
		// 基本付与・使用・ボーナス版ごとに、取消されていない行と計算結果を比べ、異なれば取消して追記する
		foreach (var key in expected.Select(Key).Union(active.Select(Key)).ToList()) {
			var exp = expected.FirstOrDefault(x => Key(x) == key);
			var act = active.Where(x => Key(x) == key).ToList();
			if (exp == null ? act.Count == 0 : act.Count == 1 && SameEvent(act[0], exp)) {
				continue;
			}
			foreach (var org in act) {
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
					Id_PointBonus = org.Id_PointBonus,
					SourceVdu = slip?.Vdu ?? 0,
					Memo = slip == null ? "店舗売上削除" : "店舗売上訂正",
				}, vdate, deltas, org.EventType == (int)EnumPointEventType.Use ? useDeltas : null);
				count++;
			}
			if (exp != null) {
				// EventKey は種類ごとの既存件数で連番にし、同じVduの再付与でも重複させない
				(exp.EventKey, exp.Memo) = key switch {
					((int)EnumPointEventType.Use, _) => ($"TENURI:{idTenuri}:U:{exp.SourceVdu}:{events.Count(x => x.EventType == (int)EnumPointEventType.Use)}", "店舗売上ポイント使用"),
					(_, 0) => ($"TENURI:{idTenuri}:G:{exp.SourceVdu}:{events.Count(x => x.EventType == (int)EnumPointEventType.Grant)}", "店舗売上付与"),
					_ => ($"TENURI:{idTenuri}:B:{key.Bonus}:{exp.SourceVdu}:{events.Count(x => x.EventType == (int)EnumPointEventType.Grant && x.Id_PointBonus == key.Bonus)}", "店舗売上ボーナス"),
				};
				Insert(exp, vdate, deltas, key.Type == (int)EnumPointEventType.Use ? useDeltas : null);
				count++;
			}
		}
		// 伝票の付与ポイントは台帳の有効付与合計と同じ値にする。利用者の編集ではないため Vdu は変えない
		if (slip != null) {
			var grantPoint = expected.Where(x => x.EventType == (int)EnumPointEventType.Grant).Sum(x => x.PointDelta);
			if (slip.GrantPoint != grantPoint) {
				slip.GrantPoint = grantPoint;
				db.ExecuteDialect("UPDATE Tran01Tenuri SET GrantPoint=@0 WHERE Id=@1", grantPoint, idTenuri);
			}
		}
		return count;
	}

	private static (int Type, long Bonus) Key(TranPointEvent x) => (x.EventType, x.Id_PointBonus);

	private void Insert(TranPointEvent item, long vdate, Dictionary<long, long> deltas, Dictionary<long, long>? useDeltas) {
		item.Vdc = vdate;
		item.Vdu = vdate;
		db.Insert(item);
		deltas[item.Id_Customer] = deltas.GetValueOrDefault(item.Id_Customer) + item.PointDelta;
		if (useDeltas != null) useDeltas[item.Id_Customer] = useDeltas.GetValueOrDefault(item.Id_Customer) + item.PointDelta;
	}

	private static bool SameEvent(TranPointEvent a, TranPointEvent b) =>
		a.PointDelta == b.PointDelta && a.Id_Customer == b.Id_Customer && a.DenDay == b.DenDay && a.Id_Tenpo == b.Id_Tenpo
		&& a.Id_PointBase == b.Id_PointBase && a.Id_PointRank == b.Id_PointRank && a.Id_PointBonus == b.Id_PointBonus;

	/// <summary>
	/// 伝票のボーナス付与（版ごと）。売上は条件を満たせば PointAmount、返品は同じ判定（初回購入を除く）で -PointAmount。
	/// 回数制限は同じボーナスコードの有効な正の付与（自伝票以外）を期間内で数える。
	/// </summary>
	private IEnumerable<TranPointEvent> CalcBonuses(Tran01Tenuri slip, MasterPointBase pointBase, int? rankKubun, int calcFlag, long target) {
		foreach (var bonus in _bonuses!.Where(x => x.Id_PointBase == pointBase.Id
			&& string.CompareOrdinal(x.DayFrom, slip.DenDay) <= 0 && string.CompareOrdinal(x.DayTo, slip.DenDay) >= 0
			&& (x.IsAllRanks == (int)EnumYesNo.Yes || x.RankKubun == rankKubun) && target >= x.MinimumKingaku)) {
			var matched = (EnumPointBonusTrigger)bonus.TriggerType switch {
				EnumPointBonusTrigger.Purchase => true,
				EnumPointBonusTrigger.BirthdayMonth => GetBirthMonth(slip.Id_Customer) == slip.DenDay[4..6],
				EnumPointBonusTrigger.FirstPurchase => calcFlag > 0 && GetFirstSlip(slip.Id_Customer) == slip.Id,
				_ => false,
			};
			if (!matched) continue;
			var (from, to) = LimitRange(bonus, slip.DenDay);
			var used = calcFlag > 0 ? CountBonus(bonus.Code, slip, from, to) : 0;
			if (calcFlag > 0 && used >= bonus.LimitCount) continue;
			yield return new TranPointEvent {
				DenDay = slip.DenDay,
				Id_Customer = slip.Id_Customer,
				Id_Tenpo = slip.Id_Tenpo,
				Id_Tenuri = slip.Id,
				EventType = (int)EnumPointEventType.Grant,
				PointDelta = bonus.PointAmount * calcFlag,
				Id_PointBase = pointBase.Id,
				Id_PointBonus = bonus.Id,
				SourceVdu = slip.Vdu,
				Jcalc = Common.SerializeObject(new {
					Rule = "Bonus",
					bonus.Code,
					bonus.Version,
					bonus.TriggerType,
					TargetKingaku = target,
					bonus.MinimumKingaku,
					bonus.LimitPeriodType,
					bonus.LimitCount,
					LimitFrom = from,
					LimitTo = to,
					UsedCount = used,
					CalcFlag = calcFlag,
					Point = bonus.PointAmount * calcFlag,
				}),
			};
		}
	}

	/// <summary>回数制限の期間。伝票=自伝票内のため期間なし(空)</summary>
	private (string From, string To) LimitRange(MasterPointBonus bonus, string denDay) {
		switch ((EnumPointLimitPeriod)bonus.LimitPeriodType) {
			case EnumPointLimitPeriod.Period:
				return (bonus.DayFrom, bonus.DayTo);
			case EnumPointLimitPeriod.FiscalYear:
				_fiscalStartMmdd ??= db.FetchDialect<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1").FirstOrDefault() is { Length: 8 } s
					&& DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? s[4..] : "0101";
				var mmdd = _fiscalStartMmdd;
				var day = DateTime.ParseExact(denDay, "yyyyMMdd", CultureInfo.InvariantCulture);
				DateTime StartOf(int year) {
					var month = int.Parse(mmdd[..2], CultureInfo.InvariantCulture);
					return new DateTime(year, month, Math.Min(int.Parse(mmdd[2..], CultureInfo.InvariantCulture), DateTime.DaysInMonth(year, month)));
				}
				var start = StartOf(day.Year) <= day ? StartOf(day.Year) : StartOf(day.Year - 1);
				return (start.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StartOf(start.Year + 1).AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
			case EnumPointLimitPeriod.Lifetime:
				return ("00000000", "99999999");
			default:
				return (string.Empty, string.Empty);
		}
	}

	/// <summary>同じボーナスコード(版をまたぐ)の有効な正の付与件数。自伝票と取消済みは除く。伝票単位の制限は常に0</summary>
	private int CountBonus(string code, Tran01Tenuri slip, string from, string to) => from.Length == 0 ? 0 : (int)db.FetchDialect<long>(
		"SELECT COUNT(*) FROM TranPointEvent e WHERE e.Id_Customer=@0 AND e.EventType=@1 AND e.PointDelta>0 AND e.Id_Tenuri<>@2 AND e.DenDay BETWEEN @3 AND @4" +
		" AND e.Id_PointBonus IN (SELECT Id FROM MasterPointBonus WHERE Code=@5)" +
		" AND NOT EXISTS (SELECT 1 FROM TranPointEvent c WHERE c.EventType=@6 AND c.Id_OriginalEvent=e.Id)",
		slip.Id_Customer, (int)EnumPointEventType.Grant, slip.Id, from, to, code, (int)EnumPointEventType.Cancel).FirstOrDefault();

	/// <summary>誕生月(MM)。年なし誕生日(MMdd)を優先し、なければ誕生日(yyyyMMdd)。不明は null</summary>
	private string? GetBirthMonth(long idCustomer) {
		if (!_birthMonthByCustomer.TryGetValue(idCustomer, out var month)) {
			var row = db.FetchDialect<MasterEndCustomer>("SELECT Id, BirthNoyear, Birthday FROM MasterEndCustomer WHERE Id=@0", idCustomer).FirstOrDefault();
			month = row?.BirthNoyear?.Trim() is { Length: 4 } mmdd && int.TryParse(mmdd[..2], out var m1) && m1 is >= 1 and <= 12 ? mmdd[..2]
				: row?.Birthday?.Trim() is { Length: 8 } ymd && int.TryParse(ymd[4..6], out var m2) && m2 is >= 1 and <= 12 ? ymd[4..6]
				: null;
			_birthMonthByCustomer[idCustomer] = month;
		}
		return month;
	}

	/// <summary>顧客の初回購入伝票Id（売上区分の (伝票日, Id) 最小）。なければ0</summary>
	private long GetFirstSlip(long idCustomer) {
		if (!_firstSlipByCustomer.TryGetValue(idCustomer, out var id)) {
			id = db.FetchDialect<long>("SELECT Id FROM Tran01Tenuri WHERE Id_Customer=@0 AND Kubun IN (@1,@2,@3) ORDER BY DenDay, Id LIMIT 1",
				idCustomer, (int)EnumUri01.Uriage, (int)EnumUri01.UriSale, (int)EnumUri01.UriShahan).FirstOrDefault();
			_firstSlipByCustomer[idCustomer] = id;
		}
		return id;
	}

	internal long Balance(long idCustomer) =>
		db.FetchDialect<long>("SELECT COALESCE(SUM(PointDelta),0) FROM TranPointEvent WHERE Id_Customer=@0", idCustomer).FirstOrDefault();

	private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }

	/// <summary>SummaryPoint は台帳合計から作り直し、会員情報の現在ポイントへは増減を加算する</summary>
	internal void ApplyBalance(Dictionary<long, long> deltas) {
		if (deltas.Count == 0) {
			return;
		}
		var vdate = Common.GetVdate();
		foreach (var (idCustomer, delta) in deltas) {
			var total = Balance(idCustomer);
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
		_bonuses ??= db.FetchDialect<MasterPointBonus>("SELECT * FROM MasterPointBonus WHERE IsEnabled=1 AND Id_PointBase>0 ORDER BY Code, Version");
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
