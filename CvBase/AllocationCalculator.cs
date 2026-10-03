namespace CvBase;

/// <summary>比率按分の端数処理</summary>
public enum AllocationRounding {
	/// <summary>切捨。余りは配らない（旧CV.netと同じ）</summary>
	Floor = 0,
	/// <summary>四捨五入。合計が総数を超える場合は小数部の小さい行から1ずつ減らして収める</summary>
	Round = 1,
}

/// <summary>
/// 配分の按分計算（同数・比率）。DBに依存しない純粋関数だけを置き、画面(CvWpfclient)と単体テストの両方から使う。
/// <para>
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step3_在庫配分入力_詳細設計.md` 3.4 を参照する。
/// </para>
/// </summary>
public static class AllocationCalculator {
	/// <summary>
	/// 同数展開。行の並び順に1行ずつ <c>min(同数, 残り)</c> を入れ、残りが無くなったら止める。
	/// </summary>
	/// <param name="perDestination">1配分先あたりの数</param>
	/// <param name="available">配れる総数（有効在庫など）</param>
	/// <param name="destinationCount">配分先の数</param>
	/// <returns>配分先ごとの数（入力の並び順）</returns>
	public static int[] Equal(int perDestination, int available, int destinationCount) {
		var result = new int[Math.Max(destinationCount, 0)];
		var remaining = Math.Max(available, 0);
		var per = Math.Max(perDestination, 0);
		for (int i = 0; i < result.Length && remaining > 0 && per > 0; i++) {
			result[i] = Math.Min(per, remaining);
			remaining -= result[i];
		}
		return result;
	}

	/// <summary>
	/// 比率按分。各行に <c>総数 × 比率 ÷ 比率合計</c> を端数処理して入れる。合計は総数を超えない。
	/// 比率の合計が0以下なら均等（全行の比率を1）として扱う。総数が0以下なら全て0。
	/// </summary>
	/// <param name="total">配る総数</param>
	/// <param name="weights">配分先ごとの比率（負値は0とみなす）</param>
	/// <param name="rounding">端数処理</param>
	public static int[] ByRatio(int total, IReadOnlyList<decimal> weights, AllocationRounding rounding) {
		var result = new int[weights.Count];
		if (total <= 0 || weights.Count == 0) return result;
		var w = weights.Select(x => x > 0 ? x : 0m).ToArray();
		var sum = w.Sum();
		if (sum <= 0) {
			w = [.. w.Select(_ => 1m)];
			sum = w.Length;
		}
		var raw = w.Select(x => total * x / sum).ToArray();
		for (int i = 0; i < raw.Length; i++) {
			result[i] = rounding == AllocationRounding.Round
				? (int)Math.Round(raw[i], MidpointRounding.AwayFromZero)
				: (int)Math.Floor(raw[i]);
		}
		// 四捨五入で総数を超えたら、小数部の小さい行（切り上げ幅の大きい行）から1ずつ減らす
		var over = result.Sum() - total;
		if (over > 0) {
			var order = Enumerable.Range(0, raw.Length)
				.Where(i => result[i] > 0)
				.OrderBy(i => raw[i] - Math.Floor(raw[i]))
				.ThenByDescending(i => i)
				.ToList();
			for (int k = 0; k < order.Count && over > 0; k++) {
				result[order[k]]--;
				over--;
			}
		}
		return result;
	}
}
