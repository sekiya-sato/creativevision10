namespace CvBase;

/// <summary>
/// 上代一括変更（Scope）の価格方式6種＋丸め＋価格ポイントを計算する純粋クラス。
/// 正典は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 2.7・6.1。
/// <para>
/// 本クラスはDB・ロガー・設定読み出しに一切依存しない <c>static</c> メソッドのみで構成する。
/// <see cref="CvWpfclient"/>（画面プレビュー）・<see cref="CvServer"/>（伝票確定時の展開）・単体テストの
/// いずれからも同一コードを使うため、引数だけで結果が決まることを保証する（設計書6.1）。
/// </para>
/// <para>
/// <b>方式4（<see cref="EnumJodaiPriceMethod.RateOffFromEffective"/>）について</b>:
/// 「実効上代」は伝票作成時点で解決済みの値（<see cref="TranJodaiMeisai.JodaiBase"/>）が
/// <paramref name="baseJodai"/> として渡ってくる前提で計算する。本クラスの内部で
/// <c>DerivedJodai</c> を引き直すことはしない（純粋関数を保つため）。方式1
/// （<see cref="EnumJodaiPriceMethod.RateOff"/>）との違いは「呼び出し側が渡す基準額が
/// 通常上代か実効上代か」だけであり、計算式そのものは同一である（設計書2.7・0.1）。
/// </para>
/// </summary>
public static class JodaiPriceRule {
	/// <summary>
	/// 価格方式1種類ぶんの新価格を計算する。
	/// <para>
	/// 方式0〜4は算出後に<paramref name="roundUnit"/>×<paramref name="roundType"/>で丸める。
	/// ただし<see cref="EnumJodaiPriceMethod.FixedPrice"/>（固定額）だけは現行
	/// <c>MasterJouDaiBulkChangeViewModel.ApplyCalc</c>（<c>CalcType=0</c>の分岐）と同様、
	/// 丸めを適用しない（利用者が入力した確定額をそのまま使う）。設計書2.7の文面は
	/// 「丸めは全方式共通」とあるが、現行の伝票を1円たりとも変えないことを最優先するため、
	/// 実装済みの現行挙動（固定額は丸めない）に合わせた。詳細は本メソッドの呼び出し元の報告を参照。
	/// </para>
	/// <para>
	/// 方式5（<see cref="EnumJodaiPriceMethod.PricePoint"/>）は<b>算出後に</b>丸めの代わりに
	/// <paramref name="pricePoints"/>（価格ポイント表の並び）の最近値へ寄せる。算出は方式1と同じく
	/// <paramref name="rateOff"/>による値下率で行う。設計書2.7の実例「算出値 7,680円 → 7,900円」は
	/// 5.4 の Price Matrix の JK-001（通常上代 12,800、OUTLET 40% OFF）に対応し、
	/// 12,800 × 0.6 = 7,680 が算出値である。<paramref name="baseJodai"/>をそのまま寄せるのではない。
	/// </para>
	/// <para>
	/// 共通の下限: 算出結果が負になる場合は0を下限にする（方式2で値引額<paramref name="amount"/>が
	/// <paramref name="baseJodai"/>を超える場合など、マイナス上代を作らないため）。
	/// </para>
	/// </summary>
	/// <param name="method">価格方式（<see cref="EnumJodaiPriceMethod"/>）。</param>
	/// <param name="baseJodai">
	/// 基準上代。方式1・2・3・5では通常上代（<c>JodaiOld</c>）、方式4では発効日時点の実効上代
	/// （<c>JodaiBase</c>）を、呼び出し側が解決して渡す。
	/// </param>
	/// <param name="fixedPrice">固定額。方式0でのみ使用。</param>
	/// <param name="rateOff">値下率(%)。方式1・4でのみ使用。</param>
	/// <param name="amount">値引額。方式2でのみ使用。</param>
	/// <param name="rateOn">掛率(%)。方式3でのみ使用。</param>
	/// <param name="roundUnit">丸め単位。0:1円 1:10円 2:百円 3:千円。方式0・5では無視する。</param>
	/// <param name="roundType">丸め方法。0:切捨 1:四捨五入 2:切上。方式0・5では無視する。</param>
	/// <param name="pricePoints">
	/// 価格ポイント表（昇順である必要はない）。方式5でのみ使用。<see langword="null"/>または空なら
	/// 方式5は寄せずに<paramref name="baseJodai"/>をそのまま返す。
	/// </param>
	/// <returns>算出後の新上代（円）。0以上。</returns>
	public static int Calculate(
		EnumJodaiPriceMethod method,
		int baseJodai,
		int fixedPrice,
		decimal rateOff,
		int amount,
		decimal rateOn,
		int roundUnit,
		int roundType,
		IReadOnlyList<int>? pricePoints = null) {
		switch (method) {
			case EnumJodaiPriceMethod.FixedPrice:
				// 利用者が入力した確定額をそのまま使う。丸めない（現行ApplyCalcのCalcType=0分岐と同じ）。
				return ClampNonNegative(fixedPrice);
			case EnumJodaiPriceMethod.RateOff:
			case EnumJodaiPriceMethod.RateOffFromEffective:
				// 計算式は同一。基準額が通常上代か実効上代かは呼び出し側がbaseJodaiで使い分ける。
				return ApplyRound(baseJodai * (1m - rateOff / 100m), roundUnit, roundType);
			case EnumJodaiPriceMethod.Amount:
				return ApplyRound(baseJodai - amount, roundUnit, roundType);
			case EnumJodaiPriceMethod.RateOn:
				return ApplyRound(baseJodai * rateOn / 100m, roundUnit, roundType);
			case EnumJodaiPriceMethod.PricePoint:
				// 「算出後に価格ポイント表の最近値へ寄せる」（設計書2.7）。算出は方式1と同じ値下率で行い、
				// 丸めの代わりに寄せる。設計書2.7の実例「算出値 7,680円 → 7,900円」は 5.4 の Price Matrix
				// の JK-001（通常上代 12,800、OUTLET 40% OFF）に対応し、12,800 × 0.6 = 7,680 が算出値である。
				// 基準上代をそのまま寄せるのではない点に注意。
				return SnapToPricePoint(
					ClampNonNegative(baseJodai * (1m - rateOff / 100m)),
					pricePoints ?? []);
			default:
				throw new ArgumentOutOfRangeException(nameof(method), method, "未定義の価格方式です。");
		}
	}

	/// <summary>
	/// 端数単位・端数方法で丸める（設計書2.7）。中間計算は<see cref="decimal"/>で行い、
	/// <see cref="double"/>を経由しない。
	/// <para>
	/// 現行<c>MasterJouDaiBulkChangeViewModel.ApplyRound</c>（<c>double</c>版）と完全に同じ丸め規則
	/// （四捨五入は<see cref="MidpointRounding.AwayFromZero"/>＝算術丸め。銀行丸めではない）を
	/// <c>decimal</c>で再実装したもの。既存伝票の再計算結果が1円も変わらないことを単体テストで担保する。
	/// </para>
	/// <para>算出結果が負になる場合は0を下限にする（マイナス上代を作らないため）。</para>
	/// </summary>
	/// <param name="value">丸め前の算出値。</param>
	/// <param name="roundUnit">丸め単位。0:1円 1:10円 2:百円 3:千円。未定義値は1円扱い。</param>
	/// <param name="roundType">丸め方法。0:切捨 1:四捨五入 2:切上。未定義値は切捨扱い。</param>
	/// <returns>丸め後の価格（円）。0以上。</returns>
	public static int ApplyRound(decimal value, int roundUnit, int roundType) {
		var scale = roundUnit switch { 1 => 10m, 2 => 100m, 3 => 1000m, _ => 1m };
		var quotient = value / scale;
		var rounded = roundType switch {
			1 => Math.Round(quotient, MidpointRounding.AwayFromZero),
			2 => Math.Ceiling(quotient),
			_ => Math.Floor(quotient),
		};
		var result = rounded * scale;
		return ClampNonNegative(result);
	}

	/// <summary>
	/// 価格ポイント表の中から<paramref name="value"/>に最も近い値へ寄せる（設計書2.7・U2）。
	/// <para>
	/// 上下が等距離のときは<b>上（高い方）を採る</b>。値引きの取りすぎ（利益の過度な圧迫）を
	/// 避けるため、同着なら安全側＝高い方へ丸める方針とした。
	/// </para>
	/// <para><paramref name="pricePoints"/>が空のときは、寄せずに<paramref name="value"/>をそのまま返す（例外にしない）。</para>
	/// </summary>
	/// <param name="value">寄せ元の算出値。</param>
	/// <param name="pricePoints">価格ポイント表（順不同でよい）。</param>
	/// <returns>最近値。表が空なら<paramref name="value"/>そのまま。</returns>
	public static int SnapToPricePoint(int value, IReadOnlyList<int> pricePoints) {
		if (pricePoints.Count == 0) {
			return value;
		}

		var best = pricePoints[0];
		var bestDistance = Math.Abs((long)best - value);
		foreach (var point in pricePoints) {
			var distance = Math.Abs((long)point - value);
			// distance < bestDistance: より近い値を採用。
			// distance == bestDistance && point > best: 等距離なら高い方を優先（値引きの取りすぎ回避）。
			if (distance < bestDistance || (distance == bestDistance && point > best)) {
				best = point;
				bestDistance = distance;
			}
		}
		return best;
	}

	/// <summary>
	/// 価格ポイント表のCSV文字列（<c>MasterMeisho</c> <c>Kubun='PPT'</c>の名称列。設計書U2）を
	/// <see langword="int"/>配列へ解釈する。
	/// <para>
	/// 空要素・前後空白のみの要素・数値でない要素は読み飛ばす。結果は昇順に整列して返す
	/// （<see cref="SnapToPricePoint"/>は順不同でも動くが、表示・デバッグのしやすさのため）。
	/// </para>
	/// </summary>
	/// <param name="csv">カンマ区切りの価格の並び（例 "5900,6900,7900,8900,9900"）。</param>
	/// <returns>昇順に整列した価格の配列。有効な要素が無ければ空配列。</returns>
	public static IReadOnlyList<int> ParsePricePoints(string? csv) {
		if (string.IsNullOrWhiteSpace(csv)) {
			return [];
		}

		var result = new List<int>();
		foreach (var token in csv.Split(',')) {
			var trimmed = token.Trim();
			if (trimmed.Length == 0) {
				continue;
			}
			if (int.TryParse(trimmed, out var parsed)) {
				result.Add(parsed);
			}
		}
		result.Sort();
		return result;
	}

	/// <summary>負値を0へ切り上げる（マイナス上代を作らないための共通下限）。</summary>
	private static int ClampNonNegative(decimal value) => value < 0 ? 0 : (int)value;

	/// <summary>負値を0へ切り上げる（マイナス上代を作らないための共通下限）。</summary>
	private static int ClampNonNegative(int value) => Math.Max(0, value);

	/// <summary>
	/// C7（原価割れ）の判定。<c>JodaiConflictChecker.CheckBelowCost</c>（CvDomainLogic）と
	/// <c>CvWpfclient</c>のPrice Matrix（設計書5.4のセル背景警告）が同じ基準を共有するための純粋関数。
	/// <c>CvWpfclient</c>は<c>CvDomainLogic</c>を参照できない（層1.5はサーバ側）ため、判定の中核だけを
	/// 双方が参照できる<c>CvBase</c>（層1）へ切り出した。
	/// </summary>
	/// <param name="jodaiNew">新上代（判定対象）。</param>
	/// <param name="cost">原価（<see cref="TranJodaiMeisai.TankaGenka"/>の時点値、無ければ<see cref="MasterShohin.TankaGenka"/>）。</param>
	public static bool IsBelowCost(int jodaiNew, int cost) => jodaiNew < cost;

	/// <summary>
	/// C8（最低販売価格違反）の判定。<paramref name="minPrice"/>が0以下（未設定）なら判定しない
	/// （設計書2.8「0 なら判定しない」）。<see cref="IsBelowCost"/>と同じ理由でCvBaseに置く。
	/// </summary>
	/// <param name="jodaiNew">新上代（判定対象）。</param>
	/// <param name="minPrice"><see cref="MasterConfig.NameJodaiMinPrice"/>の設定値。0以下なら判定しない。</param>
	public static bool IsBelowMinPrice(int jodaiNew, int minPrice) => minPrice > 0 && jodaiNew < minPrice;
}
