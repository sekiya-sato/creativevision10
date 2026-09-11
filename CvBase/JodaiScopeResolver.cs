namespace CvBase;

/// <summary>
/// Scope（適用範囲）の競合種別。C1〜C8は設計書2.8の番号に対応する。
/// <para>
/// <see cref="JodaiScopeResolver"/>が検出できるのは<see cref="ScopeOverlapSameRange"/>（C1）・
/// <see cref="ScopeDefinitionOverlap"/>（C2）・<see cref="PriorityResolvedAcrossRangeType"/>（C5）の3種のみ。
/// 残り（C3・C4・C6・C7・C8）はDB参照が要る、または本クラスの責務外（商品重複はNormalize()側）だが、
/// 後続の<c>CvDomainLogic.JodaiConflictChecker</c>（C4/C6/C7/C8）から同じ結果型を使うため、
/// ここで先に値を確保しておく（設計書6.1）。
/// </para>
/// </summary>
public enum EnumJodaiConflictKind : int {
	/// <summary>
	/// C1: 伝票内・スコープ重複。同一店舗が、同一<see cref="TranJodaiScope.RangeType"/>かつ
	/// 同一<see cref="TranJodaiScope.IncExc"/>（対象）のInclude Scope 2件以上に該当し、期間が重なる。
	/// </summary>
	ScopeOverlapSameRange = 1,
	/// <summary>
	/// C2: 伝票内・段階期間の重複。同一範囲（<see cref="TranJodaiScope.RangeType"/>と、
	/// RangeType=1なら<see cref="TranJodaiScope.GroupAxis"/>+<see cref="TranJodaiScope.Id_Group"/>、
	/// RangeType=2なら<see cref="TranJodaiScope.Id_Tenpo"/>が一致）のScope同士で期間が重なる。
	/// </summary>
	ScopeDefinitionOverlap = 2,
	/// <summary>C3: 伝票内・商品重複。<c>Jmeisai</c>に同一Scope×同一商品が複数（<c>TranJodai.Normalize()</c>が自動解消する）。</summary>
	DuplicateItem = 3,
	/// <summary>C4: 他伝票との競合。確定済み<c>DerivedJodai</c>に同一商品×同一店舗×期間重複（DB参照が要る）。</summary>
	OtherSlipConflict = 4,
	/// <summary>
	/// C5: 個別店舗とグループの衝突。C1のうち<see cref="TranJodaiScope.RangeType"/>が異なるもの。
	/// 情報レベルであり、採用されたScopeを<see cref="JodaiConflict.Message"/>に明示する。
	/// </summary>
	PriorityResolvedAcrossRangeType = 5,
	/// <summary>C6: 恒久上代変更との基準不整合。期間内に<c>Kubun=Proper</c>の伝票が別途有効（DB参照が要る）。</summary>
	ProperBaselineMismatch = 6,
	/// <summary>C7: 原価割れ。<c>JodaiNew &lt; MasterShohin.TankaGenka</c>（DB参照が要る）。</summary>
	BelowCost = 7,
	/// <summary>C8: 最低販売価格違反。<c>JodaiNew &lt; MasterConfig.JodaiMinPrice</c>（DB参照が要る）。</summary>
	BelowMinPrice = 8,
}

/// <summary>Scope（適用範囲）の競合の深刻度。</summary>
public enum EnumJodaiConflictSeverity : int {
	/// <summary>エラー。確定不可（利用者が伝票側を修正しない限り確定させるべきではない）。</summary>
	Error = 0,
	/// <summary>警告。確定は妨げないが、利用者に提示して判断を求める。</summary>
	Warning = 1,
	/// <summary>情報。優先順位で機械的に決着した内容を利用者へ明示するための通知。</summary>
	Info = 2,
}

/// <summary>
/// Scope（適用範囲）の競合1件（設計書2.8）。
/// <para>
/// <see cref="JodaiScopeResolver"/>（C1・C2・C5）と、後続の<c>CvDomainLogic.JodaiConflictChecker</c>
/// （C4・C6・C7・C8。DB参照が要るもの）の両方が同じ型で結果を返せるよう、汎用的な形にしてある。
/// 種別ごとに関係する情報の意味は異なるため、<see cref="ScopeNos"/>・<see cref="TenpoIds"/>は
/// 該当が無ければ空配列でよい（<see langword="null"/>にはしない）。
/// </para>
/// </summary>
/// <param name="Kind">競合種別（C1〜C8）。</param>
/// <param name="Severity">深刻度。</param>
/// <param name="Message">利用者向けメッセージ（日本語。店舗名・Scope名など具体的な内容を含む）。</param>
/// <param name="ScopeNos">関係する<see cref="TranJodaiScope.No"/>の一覧。</param>
/// <param name="TenpoIds">関係する店舗（<see cref="MasterTokui.Id"/>）の一覧。</param>
public sealed record JodaiConflict(
	EnumJodaiConflictKind Kind,
	EnumJodaiConflictSeverity Severity,
	string Message,
	IReadOnlyList<int> ScopeNos,
	IReadOnlyList<long> TenpoIds);

/// <summary>
/// <see cref="JodaiScopeResolver.Resolve"/>の結果。
/// </summary>
/// <param name="Jshop">
/// 解決済みの「店舗×Scope」行。<see cref="TranJodai.Jshop"/>へそのまま設定できる形（設計書2.5・2.6）。
/// 段階値下げ（設計書2.6）があるため、1店舗が複数行を持つことがある。
/// </param>
/// <param name="Conflicts">解決の過程で検出した競合（C1・C2・C5）。</param>
public sealed record JodaiScopeResolution(
	IReadOnlyList<TranJodaiShop> Jshop,
	IReadOnlyList<JodaiConflict> Conflicts);

/// <summary>
/// 上代一括変更（Scope）の「Scope＋店舗一覧 → 実店舗の解決」を行う純粋クラス。
/// 正典は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 2.5・2.6・2.8・6.1、および
/// 同設計書の字面の曖昧さを解消したタスク指示（本クラスのコメントはその確定仕様に従う）。
/// <para>
/// 本クラスはDB・ロガー・設定読み出しに一切依存しない<c>static</c>メソッドのみで構成する
/// （<see cref="JodaiPriceRule"/>と同じ方針。設計書6.1）。
/// </para>
/// <para>
/// <b>対象系統（<see cref="TranJodai.TaishoType"/>）による店舗の絞り込みは本クラスの責務にしない。</b>
/// 呼び出し側が対象系統で絞った店舗リスト（<paramref name="stores"/>相当）を渡す前提とする
/// （設計書3.1「価格グループはTenTypeに関わらず設定できるものとし、Scope側でTaisoTypeによる系統の
/// 絞り込みを行う」に対応。純粋関数を保つため、本クラスの内部で<c>TenType</c>を見ない）。
/// </para>
/// </summary>
public static class JodaiScopeResolver {
	/// <summary>
	/// Scope一覧と店舗一覧から、実店舗ごとの適用Scopeを解決し、同時に競合（C1・C2・C5）を列挙する。
	/// <para>
	/// <b>解決の要点（段階値下げがあるため「1店舗→1 Scope」は期間が重ならない集合に対してのみ成立する）</b>:
	/// ある店舗<c>t</c>と、<c>t</c>が該当するInclude Scope<c>S</c>について、<c>t</c>が該当する別のScope
	/// <c>T</c>（対象・除外を問わない）が存在し、<c>T</c>の期間が<c>S</c>の期間と重なり、かつ<c>T</c>が
	/// 優先順位（<see cref="RangeType"/>降順 → <see cref="IncExc"/>降順(除外が対象より優先) →
	/// <see cref="Odr"/>降順 → <c>Jscope</c>内の並び順(後の要素が勝ち)）で<c>S</c>に勝つ場合、
	/// <c>S</c>は採用しない（superseded）。そうでなければ<c>(t, S)</c>を<see cref="TranJodaiShop"/>の
	/// 1行として採用する。
	/// </para>
	/// </summary>
	/// <param name="stores">
	/// 解決対象の店舗一覧。呼び出し側が対象系統（<see cref="TranJodai.TaishoType"/>）で絞った
	/// <see cref="MasterTokui"/>のリストを渡すこと。<see langword="null"/>または空でも例外にせず空の結果を返す。
	/// </param>
	/// <param name="scopes">
	/// 伝票の<see cref="TranJodai.Jscope"/>相当のScope一覧。並び順が同点解消（後の要素が勝ち）に使われるため、
	/// 呼び出し側は伝票内の並び順のまま渡すこと。<see langword="null"/>または空でも例外にせず空の結果を返す。
	/// </param>
	/// <returns>解決済みの<c>Jshop</c>行と、検出した競合（C1・C2・C5）。</returns>
	public static JodaiScopeResolution Resolve(IReadOnlyList<MasterTokui>? stores, IReadOnlyList<TranJodaiScope>? scopes) {
		stores ??= [];
		scopes ??= [];
		if (scopes.Count == 0) {
			return new JodaiScopeResolution([], []);
		}

		var jshop = new List<TranJodaiShop>();
		var conflicts = new List<JodaiConflict>();

		// C2はJscopeだけで判定できるため、店舗が空でも検出する（タスク指示）。
		conflicts.AddRange(DetectScopeDefinitionOverlaps(scopes));

		foreach (var store in stores) {
			// この店舗がどのScope(索引付き)に該当するかを、伝票内の並び順を保ったまま集める。
			// 並び順のindexは同点解消の最終段（Jscope内の並び順）に使うため、Whereで詰めても保持する。
			var matches = new List<(TranJodaiScope Scope, int Index)>();
			for (var i = 0; i < scopes.Count; i++) {
				if (StoreMatchesScope(store, scopes[i])) {
					matches.Add((scopes[i], i));
				}
			}
			if (matches.Count == 0) {
				continue;
			}

			// C1・C5: 該当した Scope 同士を総当たりし、期間が重なる組み合わせを分類する。
			for (var i = 0; i < matches.Count; i++) {
				for (var j = i + 1; j < matches.Count; j++) {
					var a = matches[i];
					var b = matches[j];
					if (!PeriodsOverlap(a.Scope, b.Scope)) {
						continue;
					}

					if (a.Scope.RangeType == b.Scope.RangeType
						&& a.Scope.IncExc == (int)EnumJodaiIncExc.Include
						&& b.Scope.IncExc == (int)EnumJodaiIncExc.Include) {
						conflicts.Add(new JodaiConflict(
							EnumJodaiConflictKind.ScopeOverlapSameRange,
							EnumJodaiConflictSeverity.Error,
							$"店舗「{store.Code} {store.Name}」が同一範囲（{RangeTypeLabel(a.Scope.RangeType)}）の"
								+ $"{DescribeScope(a.Scope)}と{DescribeScope(b.Scope)}の両方に該当し、期間が重なっています。"
								+ "このままでは確定できません。",
							[a.Scope.No, b.Scope.No],
							[store.Id]));
					}

					if (a.Scope.RangeType != b.Scope.RangeType) {
						var aWins = ComparePriority(a.Scope, a.Index, b.Scope, b.Index) > 0;
						var winner = aWins ? a.Scope : b.Scope;
						var loser = aWins ? b.Scope : a.Scope;
						conflicts.Add(new JodaiConflict(
							EnumJodaiConflictKind.PriorityResolvedAcrossRangeType,
							EnumJodaiConflictSeverity.Info,
							$"店舗「{store.Code} {store.Name}」は期間の重なる複数のScope（{DescribeScope(loser)}と"
								+ $"{DescribeScope(winner)}）に該当しましたが、範囲の優先順位により{DescribeScope(winner)}が採用されました。",
							[winner.No, loser.No],
							[store.Id]));
					}
				}
			}

			// Jshop行の確定: 該当したInclude Scopeのうち、期間が重なる勝者に負けなかったものだけを採用する。
			foreach (var (scope, index) in matches) {
				if (scope.IncExc != (int)EnumJodaiIncExc.Include) {
					continue;
				}

				var superseded = matches.Any(other =>
					other.Scope != scope
					&& PeriodsOverlap(scope, other.Scope)
					&& ComparePriority(other.Scope, other.Index, scope, index) > 0);
				if (superseded) {
					continue;
				}

				jshop.Add(new TranJodaiShop {
					Id_Tenpo = store.Id,
					Code_Tenpo = store.Code,
					Mei_Tenpo = store.Name,
					No_Scope = scope.No,
					DayFrom = scope.DayFrom,
					DayTo = scope.DayTo,
				});
			}
		}

		return new JodaiScopeResolution(jshop, conflicts);
	}

	/// <summary>
	/// 店舗<paramref name="store"/>がScope<paramref name="scope"/>の範囲に該当するかどうかを判定する。
	/// <para>
	/// <see cref="EnumJodaiIncExc"/>（対象・除外）は範囲該当の判定に関与しない。「該当するか」と
	/// 「該当したときに対象にするか除外するか」は別の軸であるため、除外Scopeも同じ規則で該当判定する
	/// （優先順位比較・C1/C2/C5の検出で除外Scopeも母集団に含める必要があるため）。
	/// </para>
	/// </summary>
	private static bool StoreMatchesScope(MasterTokui store, TranJodaiScope scope) {
		switch (scope.RangeType) {
			case (int)EnumJodaiRangeType.All:
				return true;
			case (int)EnumJodaiRangeType.PriceGroup:
				// Id_Group=0(未設定)のときは該当店舗なし。既存の得意先は全軸0のままであり、
				// 未設定の店舗を価格グループScopeへ巻き込まないため（設計書2.4・タスク指示）。
				if (scope.Id_Group == 0) {
					return false;
				}
				return GroupAxisValue(store, scope.GroupAxis) == scope.Id_Group;
			case (int)EnumJodaiRangeType.Store:
				return store.Id == scope.Id_Tenpo;
			default:
				return false;
		}
	}

	/// <summary>Scopeの<see cref="TranJodaiScope.GroupAxis"/>が示す軸の店舗側の値を返す。</summary>
	private static long GroupAxisValue(MasterTokui store, int groupAxis) => groupAxis switch {
		(int)EnumJodaiGroupAxis.PriceGroup => store.Id_PriceGroup,
		(int)EnumJodaiGroupAxis.PriceArea => store.Id_PriceArea,
		(int)EnumJodaiGroupAxis.PriceChannel => store.Id_PriceChannel,
		_ => 0,
	};

	/// <summary>
	/// 2つのScopeの適用期間が重なっているかどうかを判定する（両端Inclusive。設計書0.1）。
	/// <c>DayFrom</c>/<c>DayTo</c>は<c>yyyyMMdd</c>の固定長文字列であり、桁数が揃っているため
	/// 序数（<see cref="StringComparer.Ordinal"/>）比較がそのまま日付の大小比較になる。
	/// </summary>
	private static bool PeriodsOverlap(TranJodaiScope a, TranJodaiScope b) =>
		string.CompareOrdinal(a.DayFrom, b.DayTo) <= 0 && string.CompareOrdinal(b.DayFrom, a.DayTo) <= 0;

	/// <summary>
	/// 同一店舗が複数Scopeに該当したときの優先順位を比較する（設計書2.5・タスク指示）。
	/// 比較キーは<see cref="TranJodaiScope.RangeType"/>降順 → <see cref="TranJodaiScope.IncExc"/>降順
	/// （除外(1)が対象(0)より優先） → <see cref="TranJodaiScope.Odr"/>降順 → <c>Jscope</c>内の並び順
	/// （<paramref name="xIndex"/>/<paramref name="yIndex"/>が大きい＝後の要素が勝ち）の順。
	/// </summary>
	/// <returns><paramref name="x"/>が勝てば正、<paramref name="y"/>が勝てば負、理論上同点なら0。</returns>
	private static int ComparePriority(TranJodaiScope x, int xIndex, TranJodaiScope y, int yIndex) {
		var byRangeType = x.RangeType.CompareTo(y.RangeType);
		if (byRangeType != 0) {
			return byRangeType;
		}
		var byIncExc = x.IncExc.CompareTo(y.IncExc);
		if (byIncExc != 0) {
			return byIncExc;
		}
		var byOdr = x.Odr.CompareTo(y.Odr);
		if (byOdr != 0) {
			return byOdr;
		}
		return xIndex.CompareTo(yIndex);
	}

	/// <summary>
	/// C2（伝票内・段階期間の重複）を検出する。<c>Jshop</c>を介さず<c>Jscope</c>だけで判定できる
	/// （設計書2.8・タスク指示）ため、店舗一覧より先に呼べる独立したチェックとして分離してある。
	/// </summary>
	private static List<JodaiConflict> DetectScopeDefinitionOverlaps(IReadOnlyList<TranJodaiScope> scopes) {
		var conflicts = new List<JodaiConflict>();
		for (var i = 0; i < scopes.Count; i++) {
			for (var j = i + 1; j < scopes.Count; j++) {
				var a = scopes[i];
				var b = scopes[j];
				if (!IsSameRange(a, b) || !PeriodsOverlap(a, b)) {
					continue;
				}
				conflicts.Add(new JodaiConflict(
					EnumJodaiConflictKind.ScopeDefinitionOverlap,
					EnumJodaiConflictSeverity.Error,
					$"同一範囲（{RangeTypeLabel(a.RangeType)}）の{DescribeScope(a)}と{DescribeScope(b)}の適用期間が"
						+ "重なっています。このままでは確定できません。",
					[a.No, b.No],
					[]));
			}
		}
		return conflicts;
	}

	/// <summary>
	/// C2判定用の「同一範囲」を判定する。<see cref="TranJodaiScope.IncExc"/>は範囲の同一性に関与しない
	/// （対象・除外の2つのScopeが同じ範囲を指すことは普通にあり得るため。設計書2.5の入力例がそれにあたる）。
	/// </summary>
	private static bool IsSameRange(TranJodaiScope a, TranJodaiScope b) {
		if (a.RangeType != b.RangeType) {
			return false;
		}
		return a.RangeType switch {
			(int)EnumJodaiRangeType.PriceGroup => a.GroupAxis == b.GroupAxis && a.Id_Group == b.Id_Group,
			(int)EnumJodaiRangeType.Store => a.Id_Tenpo == b.Id_Tenpo,
			// 全店(RangeType=0)は軸を持たないため、RangeTypeが一致すれば常に同一範囲。
			_ => true,
		};
	}

	/// <summary>メッセージ用のScope表示（例 "Scope#2「OUTLET」"）。</summary>
	private static string DescribeScope(TranJodaiScope scope) => $"Scope#{scope.No}「{scope.Name}」";

	/// <summary>メッセージ用の<see cref="EnumJodaiRangeType"/>の日本語表記。</summary>
	private static string RangeTypeLabel(int rangeType) => rangeType switch {
		(int)EnumJodaiRangeType.All => "全店",
		(int)EnumJodaiRangeType.PriceGroup => "価格グループ",
		(int)EnumJodaiRangeType.Store => "個別店舗",
		_ => $"RangeType={rangeType}",
	};
}
