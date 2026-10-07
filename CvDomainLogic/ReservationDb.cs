using System.Globalization;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>取置の売上変換・取消の結果区分</summary>
public enum ReservationOutcome {
	/// <summary>処理した（対象0件を含む）</summary>
	Success,
	/// <summary>対象行が無い・完了済み・Vdu不一致。何も書いていない</summary>
	Conflict,
	/// <summary>取置(区分6)以外の行、または出庫元が店舗と異なる取置を含む。何も書いていない</summary>
	InvalidKubun,
}

/// <summary>
/// 取置配分（区分 <see cref="EnumHaibun.Reservation"/>(6)）の終わらせ方。
/// <para>
/// 取置は配分確定（<see cref="ShippingDb.Commit"/>）の対象外で、次の3通りで完了する（決定 D5）。
/// </para>
/// <list type="bullet">
/// <item>売上変換（<see cref="Convert"/>）: 店舗×顧客ごとに店舗売上 <see cref="Tran01Tenuri"/> を作り、在庫を計上する</item>
/// <item>取消（<see cref="Cancel"/>）: 伝票を作らずに完了にする</item>
/// <item>期限切れ（<see cref="ExpireOverdue"/>）: 期限日を過ぎた取置を日次タスクが自動で取り消す</item>
/// </list>
/// <para>
/// どれも <see cref="TranHaibun.EndFlag"/>=1 にして引当を引き直す。取消・期限切れの取置は欠品(<c>ShortSu = Su</c>)として残す。
/// 呼び出し元が張ったトランザクション内で実行される前提で、検証に失敗したときは何も書かずに返す。
/// 売上変換は店舗×顧客で伝票化し、取消と期限切れは伝票を作らない。POS連携による自動消化はなく、別途POS売上を作ると二重計上になる。
/// </para>
/// </summary>
public class ReservationDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary>売上変換で作る店舗売上のメモ</summary>
	public const string ConvertMemo = "取置売上";

	/// <summary>
	/// 取置を店舗売上へ変換する。店舗×顧客ごとに1伝票の <see cref="Tran01Tenuri"/>（P売上）を作る。
	/// <para>
	/// 単価は取置した日の店舗上代（取置行の <see cref="TranHaibun.Tanka"/>。判断 3）。
	/// 消費税は POS の会計（<c>PointOfSaleService.CreateSale</c>）と同じく、伝票単位・店舗の端数処理で <see cref="TaxCalculator.Apply"/> する。
	/// </para>
	/// </summary>
	/// <param name="rows">変換する取置（Id・一覧取得時点のVdu）</param>
	/// <param name="denDay">売上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="outcome">結果区分。<see cref="ReservationOutcome.Success"/> 以外のときは何も書いていない</param>
	/// <returns>作成した店舗売上のIdと、変換した取置の行数</returns>
	public (IReadOnlyList<long> CreatedSlipIds, int ConvertedCount) Convert(
		IReadOnlyCollection<(long Id, long ExpectedVdu)> rows, string denDay, long idShain, out ReservationOutcome outcome) {
		ArgumentException.ThrowIfNullOrWhiteSpace(denDay);
		var targets = LoadOpenTargets(rows, out outcome);
		if (outcome != ReservationOutcome.Success || targets.Count == 0) {
			return ([], 0);
		}
		var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
		var taxIdByShohin = new TranTaxRebuildDb(_db).LoadShohinTaxIds();
		var tokuiById = FetchByIds<MasterTokui>(targets.Select(x => x.Id_Tenpo));
		var customerById = FetchByIds<MasterEndCustomer>(targets.Select(x => x.Id_Customer));
		var shohinById = FetchByIds<MasterShohin>(targets.Select(x => x.Id_Shohin));
		var skuByKey = LoadSkus(targets.Select(x => x.Id_Shohin));
		var shain = idShain > 0 ? FetchByIds<MasterShain>([idShain]).GetValueOrDefault(idShain) : null;
		var summaryDb = new SummaryDb(_db);
		var created = new List<long>();
		var vdate = Common.GetVdate();

		foreach (var group in targets.GroupBy(x => (x.Id_Tenpo, x.Id_Customer)).OrderBy(g => g.Key.Id_Tenpo).ThenBy(g => g.Key.Id_Customer)) {
			var store = tokuiById.GetValueOrDefault(group.Key.Id_Tenpo);
			var customer = customerById.GetValueOrDefault(group.Key.Id_Customer);
			var meisai = group.OrderBy(x => x.Id).Select((h, i) => {
				var shohin = shohinById.GetValueOrDefault(h.Id_Shohin);
				var sku = skuByKey.GetValueOrDefault((h.Id_Shohin, h.Id_Col, h.Id_Siz));
				return new Tran99Meisai {
					No = i + 1,
					Kubun = (int)EnumUri01.Uriage,
					Id_Shohin = h.Id_Shohin,
					Code_Shohin = shohin?.Code ?? string.Empty,
					Mei_Shohin = shohin?.Name ?? string.Empty,
					JanCode = h.JanCode,
					Id_Col = h.Id_Col,
					Code_Col = sku?.Code_Col ?? string.Empty,
					Mei_Col = sku?.Mei_Col ?? string.Empty,
					Id_Siz = h.Id_Siz,
					Code_Siz = sku?.Code_Siz ?? string.Empty,
					Mei_Siz = sku?.Mei_Siz ?? string.Empty,
					Su = h.Su,
					Tanka = h.Tanka,
					Kingaku = (long)h.Su * h.Tanka,
					Jodai = h.Jodai,
					Gedai = h.Gedai,
					Id_Tax = h.Id_Shohin > 0 && taxIdByShohin.TryGetValue(h.Id_Shohin, out var tax) ? tax : TaxCalculator.StandardTaxId,
				};
			}).ToList();
			var totals = TaxCalculator.Apply(meisai, TaxRateResolver.CreateRateResolver(sysman, denDay),
				EnumTaxCalcUnit.Slip, (EnumRounding)(store?.TaxRounding ?? 0));
			var kingakuTotal = meisai.Sum(x => x.Kingaku);
			var storeView = store == null ? new CodeNameView() : new CodeNameView(store.Id, store.Code, store.Name);
			var slip = new Tran01Tenuri {
				Vdc = vdate,
				Vdu = vdate,
				DenDay = denDay,
				Kubun = (int)EnumUri01.Uriage,
				Id_Tenpo = group.Key.Id_Tenpo,
				VTenpo = storeView,
				// 取置は店舗自身の在庫で押さえている（Id_Soko = Id_Tenpo。保存時に検査済み）
				Id_Soko = group.Key.Id_Tenpo,
				VSoko = storeView,
				Id_Customer = group.Key.Id_Customer,
				VCustomer = customer == null ? new CodeNameView() : new CodeNameView(customer.Id, customer.Code, customer.Name),
				Code_Customer = customer?.Code ?? string.Empty,
				Id_Shain = idShain,
				VShain = shain == null ? new CodeNameView() : new CodeNameView(shain.Id, shain.Code, shain.Name),
				Jmeisai = meisai,
				SuTotal = meisai.Sum(x => x.Su),
				KingakuTotal = kingakuTotal,
				JodaiTotal = meisai.Sum(x => (long)x.Su * x.Jodai),
				GedaiTotal = meisai.Sum(x => (long)x.Su * x.Gedai),
				TaxRounding = store?.TaxRounding ?? 0,
				TaxableAmount1 = totals.TaxableAmount1,
				TaxableAmount2 = totals.TaxableAmount2,
				TaxableAmount3 = totals.TaxableAmount3,
				Tax1 = totals.Tax1,
				Tax2 = totals.Tax2,
				Tax3 = totals.Tax3,
				Total = Math.Abs(kingakuTotal) + totals.TaxTotal,
				Memo = ConvertMemo,
			};
			_db.Insert(slip);
			created.Add(slip.Id);
			// 在庫を計上する。バッチ処理なので gRPC を往復せず直接呼ぶ（ShippingDb.CreateShippingSlips と同じ）
			summaryDb.CalcTran2SummaryStock(nameof(Tran01Tenuri), nameof(ITranSoko.Id_Soko), slip.Id, invertFlag: false);
			_db.Execute(
				$"UPDATE {nameof(TranHaibun)} SET EndFlag = 1, JitsuSu = Su, ShortSu = 0, KakuteiDay = @0, RelateNo2 = @1, "
				+ $"EndReason = @2, Vdu = {vdate} WHERE Id IN ({string.Join(",", group.Select(x => x.Id))})",
				denDay, (int)slip.Id, (int)EnumHaibunEndReason.Converted);
		}
		summaryDb.CalcHaibun2Reserve(targets.Select(ReserveKey.From).ToHashSet());
		return (created, targets.Count);
	}

	/// <summary>
	/// 取置を取り消す。伝票は作らずに完了（取消）にし、引当を引き直す。
	/// </summary>
	/// <param name="rows">取り消す取置（Id・一覧取得時点のVdu）</param>
	/// <param name="cancelDay">取消日 yyyyMMdd（確定日に入れる）</param>
	/// <param name="outcome">結果区分。<see cref="ReservationOutcome.Success"/> 以外のときは何も書いていない</param>
	/// <returns>取り消した行数</returns>
	public int Cancel(IReadOnlyCollection<(long Id, long ExpectedVdu)> rows, string cancelDay, out ReservationOutcome outcome) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cancelDay);
		var targets = LoadOpenTargets(rows, out outcome);
		if (outcome != ReservationOutcome.Success || targets.Count == 0) {
			return 0;
		}
		MarkCancelled(targets, cancelDay, EnumHaibunEndReason.Cancelled);
		return targets.Count;
	}

	/// <summary>
	/// 期限日を過ぎた取置（<c>LimitDay &lt; today</c>）を自動で取り消す。期限日の当日までは有効で、翌日から対象になる。
	/// 完了日（確定日）は期限日の翌日にする。スケジューラの日次タスク「取置期限切れ自動取消」から呼ぶ。
	/// </summary>
	/// <param name="today">基準日</param>
	/// <returns>取り消した行数</returns>
	public int ExpireOverdue(DateTime today) {
		var todayYmd = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var targets = _db.Fetch<TranHaibun>(
			$"where Kubun = {(int)EnumHaibun.Reservation} and EndFlag = 0 and LimitDay <> '' and LimitDay < @0", todayYmd);
		if (targets.Count == 0) {
			return 0;
		}
		// 期限日ごとに完了日（期限日の翌日）が違うのでまとめて更新する
		foreach (var group in targets.GroupBy(x => x.LimitDay)) {
			MarkCancelled([.. group], NextDay(group.Key, todayYmd), EnumHaibunEndReason.Expired, recalcReserve: false);
		}
		new SummaryDb(_db).CalcHaibun2Reserve(targets.Select(ReserveKey.From).ToHashSet());
		return targets.Count;
	}

	/// <summary>取消・期限切れの更新。全量を欠品として完了し、伝票は作らない。</summary>
	void MarkCancelled(List<TranHaibun> targets, string endDay, EnumHaibunEndReason reason, bool recalcReserve = true) {
		var vdate = Common.GetVdate();
		_db.Execute(
			$"UPDATE {nameof(TranHaibun)} SET EndFlag = 1, JitsuSu = 0, ShortSu = Su, KakuteiDay = @0, EndReason = @1, "
			+ $"Vdu = {vdate} WHERE Id IN ({string.Join(",", targets.Select(x => x.Id))})",
			endDay, (int)reason);
		if (recalcReserve) {
			new SummaryDb(_db).CalcHaibun2Reserve(targets.Select(ReserveKey.From).ToHashSet());
		}
	}

	/// <summary>
	/// 操作対象の取置を読み、すべて「区分6・未完了・Vdu一致」かを確かめる。1件でも外れれば空を返す（fail-fast）。
	/// </summary>
	List<TranHaibun> LoadOpenTargets(IReadOnlyCollection<(long Id, long ExpectedVdu)> rows, out ReservationOutcome outcome) {
		outcome = ReservationOutcome.Success;
		var refs = rows.Where(r => r.Id > 0).DistinctBy(r => r.Id).ToList();
		if (refs.Count == 0) {
			return [];
		}
		var current = _db.Fetch<TranHaibun>(
			$"where Id in ({string.Join(",", refs.Select(r => r.Id))}) and EndFlag = 0")
			.ToDictionary(x => x.Id);
		foreach (var r in refs) {
			if (!current.TryGetValue(r.Id, out var h) || h.Vdu != r.ExpectedVdu) {
				outcome = ReservationOutcome.Conflict;
				return [];
			}
			// 出庫元≠店舗の取置は保存時の検査では作られないが、汎用の書き込み経路から入ると
			// 伝票の在庫拠点（店舗）と引当を外す拠点（Id_Soko）がずれるので扱わない
			if (h.Kubun != (int)EnumHaibun.Reservation || h.Id_Soko != h.Id_Tenpo) {
				outcome = ReservationOutcome.InvalidKubun;
				return [];
			}
		}
		return [.. refs.Select(r => current[r.Id])];
	}

	/// <summary>yyyyMMdd の翌日。日付として読めなければ <paramref name="fallback"/></summary>
	static string NextDay(string ymd, string fallback) =>
		DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
			: fallback;

	Dictionary<long, T> FetchByIds<T>(IEnumerable<long> ids) where T : BaseDbClass {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) {
			return [];
		}
		return _db.Fetch<T>($"where Id in ({string.Join(",", list)})").ToDictionary(x => x.Id);
	}

	/// <summary>明細の色・サイズのコードと名称（伝票時点の値として明細へ写す）</summary>
	Dictionary<(long, long, long), DerivedShohinColSiz> LoadSkus(IEnumerable<long> shohinIds) {
		var list = shohinIds.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) {
			return [];
		}
		return _db.Fetch<DerivedShohinColSiz>($"where Id_Shohin in ({string.Join(",", list)})")
			.DistinctBy(x => (x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(x => (x.Id_Shohin, x.Id_Col, x.Id_Siz));
	}
}
