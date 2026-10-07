using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>配分確定でエラーになった倉庫+SKU（有効在庫割れ）</summary>
/// <param name="Id_Soko">出庫元倉庫</param>
/// <param name="Id_Shohin">商品</param>
/// <param name="Id_Col">色</param>
/// <param name="Id_Siz">サイズ</param>
/// <param name="Shiji">確定しようとした確定数の合計</param>
/// <param name="Yuko">確定前の有効在庫（実在庫 − 確定対象以外の引当数）</param>
public readonly record struct ShippingConfirmError(long Id_Soko, long Id_Shohin, long Id_Col, long Id_Siz, int Shiji, int Yuko);

/// <summary>配分確定の結果区分</summary>
public enum CommitOutcome {
	/// <summary>確定した（対象0件を含む）</summary>
	Success,
	/// <summary>対象行が無い・完了済み・Vdu不一致。何も書いていない</summary>
	Conflict,
	/// <summary>確定できない区分を含む。何も書いていない</summary>
	InvalidKubun,
	/// <summary>有効在庫を割る倉庫+SKUがある。何も書いていない</summary>
	Shortage,
	/// <summary>仕入配分(区分0)の確定数が入荷済み数を超える。何も書いていない（呼び出し元が戻す）</summary>
	NotArrived,
}

/// <summary>
/// 配分の確定（伝票作成）。
/// <para>
/// 決定 D8 により、確定と伝票作成を1段階にまとめた（旧CV.net由来の「出荷指示確定 → 出荷処理」の2段階＝旧決定 I3 / I6 は廃止）。
/// 確定数を反映し、出荷売上または移動伝票を作り、<see cref="TranHaibun.RelateNo2"/> へ伝票Idを書いて
/// <see cref="TranHaibun.EndFlag"/>=1 で引当を解除するまでを、呼び出し元のトランザクション内で行う。
/// 確定取消は設けない（決定 D9）。訂正は作成した伝票側で行う。
/// </para>
/// <para>
/// 確定方式・入荷上限の判断は `Doc/spec/2026-09-28_設計判断記録.md` 2.8 / 2.9 を参照する。
/// </para>
/// </summary>
public class ShippingDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary><see cref="CommitOutcome.NotArrived"/> のとき、入荷済み数を超えて確定しようとした行</summary>
	public IReadOnlyList<TranHaibun> NotArrivedRows { get; private set; } = [];

	/// <summary>
	/// 伝票作成。確定数を反映済み（<see cref="TranHaibun.KakuteiDay"/> 有効）の配分から伝票を作り、引当を解除する。
	/// 通常は <see cref="Commit"/> から呼ばれる（テストは直接呼ぶ）。
	/// <para>
	/// まとめる単位は仮想ヘッダのキー
	/// <c>DenDay + NouhinDay + Id_Soko + Id_Tenpo + Kubun + RelateNo1</c>（決定 I5）。
	/// 旧CV.netの配分伝票NO（1出庫元 ⇒ 1出荷先）と同じ括りになる。
	/// </para>
	/// <para>
	/// 生成する伝票は出荷先の店種区分で分かれる（決定 I4）。
	/// 卸先(1)・売仕店(3) は出荷売上伝票 <see cref="Tran00Uriage"/>、
	/// 倉庫(0)・直営店(6) は移動出庫伝票 <see cref="Tran10IdoOut"/> になる。
	/// </para>
	/// <para>
	/// 数量は確定数 <see cref="TranHaibun.JitsuSu"/> を使う。欠品(<see cref="TranHaibun.ShortSu"/>)は出荷しない。
	/// 全量欠品の行は伝票を作らずに完了だけ立てて引当から外す。
	/// </para>
	/// </summary>
	/// <param name="haibunIds">伝票を作る配分行のId</param>
	/// <param name="denDay">生成する伝票の在庫計上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <returns>生成した伝票Idの一覧</returns>
	public IReadOnlyList<long> CreateShippingSlips(IEnumerable<long> haibunIds, string denDay, long idShain) {
		var ids = haibunIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return [];
		}
		var rows = _db.Fetch<TranHaibun>(
			$"where Id in ({string.Join(",", ids)}) and EndFlag = 0 and ifnull(KakuteiDay,'') <> '' "
			+ $"order by {HaibunHeaderKey.KeyColumnsSql()}, Id");
		if (rows.Count == 0) {
			return [];
		}
		var tokuiById = LoadTokui(rows.Select(x => x.Id_Tenpo));
		// MasterSysman(税率)・商品Id→消費税区分は明細をまとめて処理する前に一括で読む(TranTaxRebuildDb.LoadShohinTaxIdsと同じ考え方)。
		var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
		var taxIdByShohin = new TranTaxRebuildDb(_db).LoadShohinTaxIds();
		var summaryDb = new SummaryDb(_db);
		var created = new List<long>();

		// 仮想ヘッダ単位でまとめる(決定 I5)。キーの定義は HaibunHeaderKey に集約している
		foreach (var group in rows.GroupBy(HaibunHeaderKey.From)) {
			var shipped = group.Where(x => x.JitsuSu > 0).ToList();
			long slipId = 0;
			if (shipped.Count > 0) {
				var meisai = shipped.Select((h, i) => new Tran99Meisai {
					No = i + 1,
					Id_Shohin = h.Id_Shohin,
					Id_Col = h.Id_Col,
					Id_Siz = h.Id_Siz,
					Su = h.JitsuSu,
					Tanka = h.Tanka,
					Kingaku = (long)h.JitsuSu * h.Tanka,
					Jodai = h.Jodai,
					Gedai = h.Gedai,
				}).ToList();
				var tokui = tokuiById.GetValueOrDefault(group.Key.Id_Tenpo);
				var tenType = tokui?.TenType ?? 0;
				slipId = IsShukka(tenType)
					? CreateUriage(group.Key, meisai, idShain, denDay, tokui, sysman, taxIdByShohin)
					: CreateIdoOut(group.Key, meisai, idShain, denDay);
				created.Add(slipId);
				// 生成した伝票の在庫を反映する。バッチ処理なので gRPC を往復せず直接呼ぶ
				summaryDb.CalcTran2SummaryStock(
					IsShukka(tenType) ? nameof(Tran00Uriage) : nameof(Tran10IdoOut),
					nameof(ITranSoko.Id_Soko), slipId, invertFlag: false);
				if (!IsShukka(tenType)) {
					summaryDb.CalcTran2SummaryStock(nameof(Tran10IdoOut), nameof(ITranIdo.Id_Ido), slipId, invertFlag: false);
				}
			}
			// 出荷した行も全量欠品の行も完了にして引当から外す
			var vdate = Common.GetVdate();
			_db.Execute(
				$"UPDATE {nameof(TranHaibun)} SET EndFlag = 1, RelateNo2 = @0, Vdu = {vdate} "
				+ $"WHERE Id IN ({string.Join(",", group.Select(x => x.Id))})", (int)slipId);
		}
		// 引当は EndFlag が変わったキーぶんを引き直す
		summaryDb.CalcHaibun2Reserve(rows.Select(ReserveKey.From).ToHashSet());
		return created;
	}

	/// <summary>
	/// 配分確定。確定数を反映してから <see cref="CreateShippingSlips"/> で伝票を作る。
	/// <para>
	/// 書き込む前に全行を検証し、1件でも次に当たれば<b>何も書かずに</b>返す（呼び出し元がトランザクションを戻す前提）。
	/// </para>
	/// <list type="bullet">
	/// <item>対象(<c>EndFlag=0</c>)に無い行、<see cref="TranHaibun.Vdu"/> が一覧取得時点と食い違う行 → <see cref="CommitOutcome.Conflict"/></item>
	/// <item>確定できない区分（<see cref="AllocationRules.IsCommittableKubun"/>）→ <see cref="CommitOutcome.InvalidKubun"/></item>
	/// <item>有効在庫を割る倉庫+SKU（<see cref="AllocationRules.FindShortages"/>）→ <see cref="CommitOutcome.Shortage"/></item>
	/// </list>
	/// <para>
	/// 旧状態「確定済み・未出荷」（<see cref="TranHaibun.KakuteiDay"/> が有効で <c>EndFlag=0</c>）の行も対象にし、確定日を上書きする。
	/// </para>
	/// </summary>
	/// <param name="rows">確定する行（Id・一覧取得時点のVdu・確定数）</param>
	/// <param name="denDay">確定日 兼 生成する伝票の在庫計上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="outcome">結果区分。<see cref="CommitOutcome.Success"/> 以外のときは何も書いていない</param>
	/// <param name="shortages">有効在庫を割った倉庫+SKU（<see cref="CommitOutcome.Shortage"/> のときだけ）</param>
	/// <returns>作成した伝票Idの一覧と、完了にした行数・欠品のあった行数</returns>
	public (IReadOnlyList<long> CreatedSlipIds, int CommittedCount, int ShortageRowCount) Commit(
		IReadOnlyCollection<(long Id, long ExpectedVdu, int KakuteiSu)> rows, string denDay, long idShain,
		out CommitOutcome outcome, out IReadOnlyList<ShippingConfirmError> shortages) {
		ArgumentException.ThrowIfNullOrWhiteSpace(denDay);
		outcome = CommitOutcome.Success;
		shortages = [];
		var targets = rows.Where(r => r.Id > 0).DistinctBy(r => r.Id).ToList();
		if (targets.Count == 0) {
			return ([], 0, 0);
		}
		var current = _db.Fetch<TranHaibun>(
			$"where Id in ({string.Join(",", targets.Select(r => r.Id))}) and EndFlag = 0")
			.ToDictionary(x => x.Id);
		// 1件でも「対象に無い」「Vdu不一致」があれば何も書かずに競合として返す(fail-fast)
		foreach (var r in targets) {
			if (!current.TryGetValue(r.Id, out var h) || h.Vdu != r.ExpectedVdu) {
				outcome = CommitOutcome.Conflict;
				return ([], 0, 0);
			}
			if (!AllocationRules.IsCommittableKubun(h.Kubun)) {
				outcome = CommitOutcome.InvalidKubun;
				return ([], 0, 0);
			}
		}
		// 仕入配分(区分0)は入荷済み数の範囲でしか確定できない。判定の直前に、対象の発注の入荷割当を計算し直す
		// （入荷割当は入荷済み数と Vdu を書き換えるが、失敗時は呼び出し元がトランザクションごと戻す）
		var hachuIds = current.Values.Where(x => x.Kubun == (int)EnumHaibun.Hatsukai && x.RelateNo1 > 0)
			.Select(x => (long)x.RelateNo1).Distinct().ToList();
		var arrival = new ArrivalDb(_db);
		if (hachuIds.Count > 0 && arrival.Recalc(hachuIds) > 0) {
			current = _db.Fetch<TranHaibun>(
				$"where Id in ({string.Join(",", targets.Select(r => r.Id))}) and EndFlag = 0")
				.ToDictionary(x => x.Id);
		}
		var commitSu = targets.ToDictionary(r => r.Id, r => AllocationRules.ClampCommitSu(r.KakuteiSu, current[r.Id].Su));
		var notArrived = AllocationRules.FindNotArrived(current.Values, commitSu);
		if (notArrived.Count > 0) {
			outcome = CommitOutcome.NotArrived;
			NotArrivedRows = notArrived;
			return ([], 0, 0);
		}
		// 旧2段階方式の確定(KakuteiDayのUPDATEのみ)は引当を引き直していなかったため、「確定済み・未出荷」行のキーでは
		// 保存済みの引当数が式(AllocationRules.ReservedQty)とずれている可能性がある。検査の前に対象キーを引き直して揃える。
		// 引き直しは冪等で、在庫割れで戻す場合も呼び出し元がトランザクションごと戻す
		new SummaryDb(_db).CalcHaibun2Reserve(current.Values.Select(ReserveKey.From).ToHashSet());
		var found = AllocationRules.FindShortages(BuildStockInputs([.. current.Values], commitSu));
		if (found.Count > 0) {
			outcome = CommitOutcome.Shortage;
			shortages = found;
			return ([], 0, 0);
		}
		var vdate = Common.GetVdate();
		foreach (var (id, su) in commitSu) {
			var h = current[id];
			_db.Execute(
				$"update {nameof(TranHaibun)} set {nameof(TranHaibun.KakuteiDay)} = @0, {nameof(TranHaibun.JitsuSu)} = @1, "
				+ $"{nameof(TranHaibun.ShortSu)} = @2, {nameof(TranHaibun.Vdu)} = {vdate} where {nameof(TranHaibun.Id)} = @3",
				denDay, su, h.Su - su, id);
		}
		var created = CreateShippingSlips(commitSu.Keys, denDay, idShain);
		// 確定で消費した入荷数を差し引いて、残りの入荷を同じ発注のほかの仕入配分へ割り当て直す
		// （欠品で確定した行の余りが、次の優先順位の行へ回る）
		if (hachuIds.Count > 0) {
			arrival.Recalc(hachuIds);
		}
		return (created, commitSu.Count, commitSu.Count(kv => kv.Value < current[kv.Key].Su));
	}

	/// <summary>
	/// 確定対象の行を倉庫+SKUで集計し、<see cref="SummaryRealStock"/> の実在庫・引当数と組み合わせる。
	/// </summary>
	private List<CommitStockInput> BuildStockInputs(List<TranHaibun> targets, Dictionary<long, int> commitSu) {
		var groups = targets
			.GroupBy(h => (h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz))
			.Where(g => g.Sum(h => commitSu[h.Id]) > 0)
			.ToList();
		if (groups.Count == 0) {
			return [];
		}
		var shohinIds = string.Join(",", groups.Select(g => g.Key.Id_Shohin).Distinct());
		var sokoIds = string.Join(",", groups.Select(g => g.Key.Id_Soko).Distinct());
		var stock = _db.Fetch<SummaryRealStock>($"where Id_Shohin in ({shohinIds}) and Id_Soko in ({sokoIds})")
			.ToDictionary(x => (x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz));
		return [.. groups.Select(g => {
			var s = stock.GetValueOrDefault(g.Key);
			return new CommitStockInput(g.Key.Id_Soko, g.Key.Id_Shohin, g.Key.Id_Col, g.Key.Id_Siz,
				s?.Su ?? 0, s?.ReserveQty ?? 0,
				g.Sum(AllocationRules.ReservedQty), g.Sum(h => commitSu[h.Id]));
		})];
	}

	/// <summary>出荷売上とみなす店種区分。1=卸先 / 3=売仕店（決定 I4 / G4）</summary>
	public static bool IsShukka(int tenType) => tenType is 1 or 3;

	/// <summary>
	/// 配分出荷の売上伝票を作る。得意先(店舗、<see cref="MasterTokui"/>)の税計算単位・端数処理をスナップショットし、
	/// 明細の消費税区分を<see cref="MasterShohin.Id_Tax"/>から解決したうえで<see cref="TaxCalculator.Apply"/>で
	/// 税額を確定する。従来はここで消費税を一切計算していなかった(Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md)。
	/// </summary>
	private long CreateUriage(HaibunHeaderKey key,
		List<Tran99Meisai> meisai, long idShain, string denDay,
		MasterTokui? tokui, MasterSysman sysman, Dictionary<long, long> taxIdByShohin) {
		foreach (var m in meisai) {
			m.Id_Tax = m.Id_Shohin > 0 && taxIdByShohin.TryGetValue(m.Id_Shohin, out var found)
				? found
				: TaxCalculator.StandardTaxId;
		}
		var calcUnit = (EnumTaxCalcUnit)(tokui?.TaxCalcUnit ?? 0);
		var rounding = (EnumRounding)(tokui?.TaxRounding ?? 0);
		var totals = TaxCalculator.Apply(meisai, TaxRateResolver.CreateRateResolver(sysman, denDay), calcUnit, rounding);
		var slip = new Tran00Uriage {
			DenDay = denDay,
			KakeDay = denDay,
			Id_Soko = key.Id_Soko,
			Id_Tokui = key.Id_Tenpo,
			Id_Shain = idShain,
			// 出荷売上の RelateNo1 は受注Id（受注残の消化・自動完了に使う規約）。受注配分(区分2)以外の配分は
			// RelateNo1 に発注Id等を持つので、そのまま入れると無関係な受注の残を減らしてしまう（Step 4 レビュー指摘）
			RelateNo1 = key.Kubun == (int)EnumHaibun.Juchu ? key.RelateNo1 : 0,
			IsPay = 1,
			SuTotal = meisai.Sum(x => x.Su),
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = meisai,
			Memo = "配分出荷",
			TaxCalcUnit = tokui?.TaxCalcUnit ?? 0,
			TaxRounding = tokui?.TaxRounding ?? 0,
			TaxableAmount1 = totals.TaxableAmount1,
			TaxableAmount2 = totals.TaxableAmount2,
			TaxableAmount3 = totals.TaxableAmount3,
			Tax1 = totals.Tax1,
			Tax2 = totals.Tax2,
			Tax3 = totals.Tax3,
		};
		slip.Total = Math.Abs(slip.KingakuTotal) + totals.TaxTotal;
		_db.Insert(slip);
		return slip.Id;
	}

	private long CreateIdoOut(HaibunHeaderKey key,
		List<Tran99Meisai> meisai, long idShain, string denDay) {
		var slip = new Tran10IdoOut {
			DenDay = denDay,
			Id_Soko = key.Id_Soko,
			Id_Ido = key.Id_Tenpo,
			Id_Shain = idShain,
			SuTotal = meisai.Sum(x => x.Su),
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = meisai,
			Memo = "配分出荷",
		};
		_db.Insert(slip);
		return slip.Id;
	}

	/// <summary>出荷先(店舗)のMasterTokuiを一括で読む。店種区分(TenType)と税計算単位・端数処理のスナップショットに使う。</summary>
	private Dictionary<long, MasterTokui> LoadTokui(IEnumerable<long> tenpoIds) {
		var ids = tenpoIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return [];
		}
		return _db.Fetch<MasterTokui>($"where Id in ({string.Join(",", ids)})")
			.ToDictionary(x => x.Id);
	}
}
