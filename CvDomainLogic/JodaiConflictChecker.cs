using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 上代一括変更（<see cref="TranJodai"/>）の競合検出のうち、DB参照が要る4種（設計書2.8）を担当する。
/// <para>
/// 伝票内だけで完結する C1・C2・C5 は <see cref="JodaiScopeResolver"/>（<c>CvBase</c>、純粋関数）が担当し、
/// C3（明細の商品重複）は <see cref="TranJodai.Normalize"/> が自動解消する。本クラスが担当するのは
/// <b>C4（他伝票との競合）・C6（恒久上代変更との基準不整合）・C7（原価割れ）・C8（最低販売価格違反）</b>の4つ
/// （設計書6.1「JodaiConflictChecker は CvDomainLogic に置く（DB参照が要るため）」）。
/// </para>
/// <para>
/// 戻り値はすべて <see cref="JodaiScopeResolver"/> と同じ <see cref="JodaiConflict"/> であり、
/// 呼び出し側は <c>JodaiScopeResolver.Resolve(...).Conflicts</c> と本クラスの結果を単純に連結できる。
/// C4/C6/C7/C8 はいずれも設計書2.8の扱いどおり「警告（確定は妨げない）」であり、
/// 機械的に確定不可にするのは resolver 側が返す C1/C2（Error）だけである。
/// </para>
/// <para>
/// クラスの形は同じ <c>CvDomainLogic</c> 層の <see cref="JodaiDb"/> に合わせ、<see cref="ExDatabase"/> を
/// 受け取るインスタンスクラスとした（<c>static</c>にしない。DB参照が本質的な責務であり、
/// <see cref="JodaiDb"/> も同様にインスタンスへ<c>_db</c>/<c>_logger</c>を保持する形を採っている）。
/// </para>
/// </summary>
public class JodaiConflictChecker {
	readonly ExDatabase _db;
	readonly ILogger<JodaiConflictChecker> _logger;

	/// <summary>メッセージに含める代表例の最大件数。全件を返すと画面が破綻するため、件数と代表例に集約する（タスク指示）。</summary>
	const int MaxExamples = 5;

	public JodaiConflictChecker(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<JodaiConflictChecker>();
	}

	/// <summary>
	/// C4・C6・C7・C8をまとめて検出する。<paramref name="tran"/>はJshop/Jmeisaiが解決済み
	/// （<see cref="JodaiScopeResolver.Resolve"/>の結果を<see cref="TranJodai.Jshop"/>へ反映済み）であること。
	/// <para>Jshop・Jmeisaiがともに空の伝票（新規未入力など）は何も検出せず空リストを返す（例外にしない）。</para>
	/// </summary>
	public IReadOnlyList<JodaiConflict> Check(TranJodai tran) {
		var conflicts = new List<JodaiConflict>();
		conflicts.AddRange(CheckOtherSlipConflict(tran));
		conflicts.AddRange(CheckProperBaselineMismatch(tran));
		conflicts.AddRange(CheckBelowCost(tran));
		conflicts.AddRange(CheckBelowMinPrice(tran));
		return conflicts;
	}

	/// <summary>
	/// C4: 他伝票との競合。確定済み<see cref="DerivedJodai"/>に同一商品×同一店舗×期間重複があるかを検出する
	/// （設計書2.8。判定SQLは2.8に例示済み）。
	/// <para>
	/// <see cref="TranJodai.Id"/>が0（まだ保存されていない新規伝票）でも、それ自体が
	/// <see cref="DerivedJodai.Id_Tran"/>に一致することはないため、そのまま自伝票除外の除外条件として使える
	/// （タスク指示「@selfに0を渡せば全件が他伝票になる」）。
	/// </para>
	/// <para>
	/// 対象店舗・対象商品・対象期間は<paramref name="tran"/>の<c>Jshop</c>/<c>Jmeisai</c>から素直に取る
	/// （個々の商品×店舗の組み合わせごとに期間を絞り込む厳密な展開はせず、設計書2.8のSQL例と同様に
	/// 「対象商品全体」×「対象店舗全体」×「全体の期間幅」で緩めに検出する。過検出はあっても見逃しは避ける）。
	/// </para>
	/// </summary>
	public IReadOnlyList<JodaiConflict> CheckOtherSlipConflict(TranJodai tran) {
		if (tran.Jshop.Count == 0 || tran.Jmeisai.Count == 0) {
			return [];
		}

		var (dayFrom, dayTo) = JodaiConflictSql.OverallPeriod(tran.Jshop, tran.DayFrom, tran.DayTo);
		var shohinIds = tran.Jmeisai.Select(c => c.Id_Shohin).Distinct().ToList();
		var tenpoIds = tran.Jshop.Select(c => c.Id_Tenpo).Distinct().ToList();

		// SQL組み立ては画面（④確認タブ）と共有する（CvBase.JodaiConflictSql。設計書6.3・タスク指示）。
		var sql = JodaiConflictSql.BuildOtherSlipConflictSql(shohinIds, tenpoIds, properOnly: false);
		var rows = _db.Fetch<JodaiConflictSql.OtherSlipRow>(sql, tran.Id, tran.TaishoType, dayTo, dayFrom);
		if (rows.Count == 0) {
			return [];
		}

		var examples = rows.Take(MaxExamples)
			.Select(c => $"{c.Code_Shohin} {c.Mei_Shohin}（店舗Id={c.Id_Tenpo}）{c.Jodai}円 [伝票Id={c.Id_Tran}]");
		var message = $"他の伝票の確定済み適用上代（DerivedJodai）と、同一商品×同一店舗×期間で重複しています（{rows.Count}件）。"
			+ $" 例: {string.Join("、", examples)}";
		return [new JodaiConflict(
			EnumJodaiConflictKind.OtherSlipConflict,
			EnumJodaiConflictSeverity.Warning,
			message,
			[],
			rows.Select(c => c.Id_Tenpo).Distinct().Take(MaxExamples).ToList())];
	}

	/// <summary>
	/// C6: 恒久上代変更との基準不整合。期間内に<see cref="EnumJodaiKubun.Proper"/>(=0)の伝票が
	/// 別途有効（＝確定済み<see cref="DerivedJodai"/>に存在）であれば警告する（設計書2.8）。
	/// <para>
	/// 対象・期間の絞り込み方はC4と同じ（<see cref="CheckOtherSlipConflict"/>参照）。C4との違いは
	/// <c>Kubun = Proper</c>で絞ることだけ。
	/// </para>
	/// </summary>
	public IReadOnlyList<JodaiConflict> CheckProperBaselineMismatch(TranJodai tran) {
		if (tran.Jshop.Count == 0 || tran.Jmeisai.Count == 0) {
			return [];
		}

		var (dayFrom, dayTo) = JodaiConflictSql.OverallPeriod(tran.Jshop, tran.DayFrom, tran.DayTo);
		var shohinIds = tran.Jmeisai.Select(c => c.Id_Shohin).Distinct().ToList();
		var tenpoIds = tran.Jshop.Select(c => c.Id_Tenpo).Distinct().ToList();

		// SQL組み立ては画面（④確認タブ）と共有する（CvBase.JodaiConflictSql。CheckOtherSlipConflictとの違いはproperOnly=trueだけ）。
		var sql = JodaiConflictSql.BuildOtherSlipConflictSql(shohinIds, tenpoIds, properOnly: true);
		var rows = _db.Fetch<JodaiConflictSql.OtherSlipRow>(sql, tran.Id, tran.TaishoType, dayTo, dayFrom);
		if (rows.Count == 0) {
			return [];
		}

		var examples = rows.Take(MaxExamples)
			.Select(c => $"{c.Code_Shohin} {c.Mei_Shohin}（店舗Id={c.Id_Tenpo}）恒久上代={c.Jodai}円 [伝票Id={c.Id_Tran}]");
		var message = $"期間内に恒久上代変更（Kubun=Proper）の伝票が別途有効です（{rows.Count}件）。"
			+ $" 例: {string.Join("、", examples)}";
		return [new JodaiConflict(
			EnumJodaiConflictKind.ProperBaselineMismatch,
			EnumJodaiConflictSeverity.Warning,
			message,
			[],
			rows.Select(c => c.Id_Tenpo).Distinct().Take(MaxExamples).ToList())];
	}

	/// <summary>
	/// C7: 原価割れ。<see cref="TranJodaiMeisai.JodaiNew"/>が原価を下回る明細を検出する（設計書2.8）。
	/// <para>
	/// 原価は<see cref="TranJodaiMeisai.TankaGenka"/>（設計書3.4の原価割れ判定用の時点値スナップショット）を
	/// 優先して使う。0（Step 1b以前の伝票で未設定、またはNPocoの既定値）のときだけ
	/// <see cref="MasterShohin.TankaGenka"/>を引く（タスク指示で確定済みの仕様）。
	/// </para>
	/// <para>
	/// <b>原価は<see cref="MasterShohin.TankaGenka"/>を直接見てよい。</b>原価4項目対応で入った
	/// <c>MasterSysman.CostMethod</c>は「原価をどう再計算するバッチを走らせるか」の設定であり、
	/// バッチが常に解決結果を<c>MasterShohin.TankaGenka</c>へ書き戻すため、参照側（本クラス）は
	/// <c>TankaGenka</c>を読めば足りる（<c>CostMethod</c>で分岐するのは<c>CostUpdateDb</c>等のバッチ側だけ。調査済み・確定事項）。
	/// </para>
	/// <para>
	/// 明細ごとに1件返すと大量になるため、種別ごとに1件へ集約し、件数と代表例（先頭数件の商品CD・商品名・価格）を
	/// メッセージに含める（タスク指示。件数だけでは利用者が判断できず、全件を返すと画面が破綻するため）。
	/// </para>
	/// </summary>
	public IReadOnlyList<JodaiConflict> CheckBelowCost(TranJodai tran) {
		if (tran.Jmeisai.Count == 0) {
			return [];
		}

		// 時点値(TankaGenka)が0の明細だけ、マスタの原価をまとめて引く（明細ごとに1クエリ流すのを避ける。JodaiDbと同じ方針）。
		var needsLookup = tran.Jmeisai.Where(c => c.TankaGenka <= 0).Select(c => c.Id_Shohin).Distinct().ToList();
		var masterCost = new Dictionary<long, int>();
		if (needsLookup.Count > 0) {
			var sql = $"SELECT Id, ifnull(TankaGenka,0) AS TankaGenka FROM {nameof(MasterShohin)} WHERE Id IN ({string.Join(",", needsLookup)})";
			masterCost = _db.Fetch<ShohinCostRow>(sql).ToDictionary(c => c.Id, c => c.TankaGenka);
		}

		var violations = new List<(TranJodaiMeisai Meisai, int Cost)>();
		foreach (var meisai in tran.Jmeisai) {
			var cost = meisai.TankaGenka > 0 ? meisai.TankaGenka : masterCost.GetValueOrDefault(meisai.Id_Shohin, 0);
			// 判定の中核はJodaiPriceRule.IsBelowCostへ共有する（CvWpfclientのPrice Matrixセル警告と同じ基準にするため。
			// CvWpfclientはCvDomainLogicを参照できないので、双方が参照できるCvBase側へ切り出した）。
			if (JodaiPriceRule.IsBelowCost(meisai.JodaiNew, cost)) {
				violations.Add((meisai, cost));
			}
		}
		if (violations.Count == 0) {
			return [];
		}

		var examples = violations.Take(MaxExamples)
			.Select(c => $"{c.Meisai.Code_Shohin} {c.Meisai.Mei_Shohin} 新{c.Meisai.JodaiNew}円<原価{c.Cost}円");
		var message = $"原価割れの明細が{violations.Count}件あります。 例: {string.Join("、", examples)}";
		return [new JodaiConflict(
			EnumJodaiConflictKind.BelowCost,
			EnumJodaiConflictSeverity.Warning,
			message,
			violations.Select(c => c.Meisai.No_Scope).Distinct().Take(MaxExamples).ToList(),
			[])];
	}

	/// <summary>
	/// C8: 最低販売価格違反。<see cref="TranJodaiMeisai.JodaiNew"/>が<see cref="MasterConfig.NameJodaiMinPrice"/>を
	/// 下回る明細を検出する（設計書2.8）。設定値が0（未設定含む）なら判定自体を行わない。
	/// <para>
	/// <see cref="MasterConfig"/>に行が無くても既定値0として動く。<see cref="JodaiDb.GetKeepDays"/>と同じ
	/// 「未設定・不正値ならフォールバック」の方針に揃える（タスク指示）。
	/// </para>
	/// <para>C7と同様、種別ごとに1件へ集約し、件数と代表例をメッセージに含める。</para>
	/// </summary>
	public IReadOnlyList<JodaiConflict> CheckBelowMinPrice(TranJodai tran) {
		if (tran.Jmeisai.Count == 0) {
			return [];
		}

		var minPrice = GetJodaiMinPrice();
		if (minPrice <= 0) {
			return [];
		}

		// 判定の中核はJodaiPriceRule.IsBelowMinPriceへ共有する（CheckBelowCostと同じ理由）。
		var violations = tran.Jmeisai.Where(c => JodaiPriceRule.IsBelowMinPrice(c.JodaiNew, minPrice)).ToList();
		if (violations.Count == 0) {
			return [];
		}

		var examples = violations.Take(MaxExamples)
			.Select(c => $"{c.Code_Shohin} {c.Mei_Shohin} 新{c.JodaiNew}円<最低{minPrice}円");
		var message = $"最低販売価格（{minPrice}円）を下回る明細が{violations.Count}件あります。 例: {string.Join("、", examples)}";
		return [new JodaiConflict(
			EnumJodaiConflictKind.BelowMinPrice,
			EnumJodaiConflictSeverity.Warning,
			message,
			violations.Select(c => c.No_Scope).Distinct().Take(MaxExamples).ToList(),
			[])];
	}

	/// <summary>
	/// <see cref="MasterConfig"/>の<see cref="MasterConfig.NameJodaiMinPrice"/>を読む。
	/// 未設定・不正値なら0（判定しない）を返す（<see cref="JodaiDb.GetKeepDays"/>と同じ方針）。
	/// </summary>
	public int GetJodaiMinPrice() {
		var val = _db.FirstOrDefault<string>($"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", MasterConfig.NameJodaiMinPrice);
		return int.TryParse(val, out var price) && price >= 0 ? price : 0;
	}

	/// <summary>C7のマスタ原価まとめ取得の受け取り用。</summary>
	public class ShohinCostRow {
		public long Id { get; set; }
		public int TankaGenka { get; set; }
	}
}
