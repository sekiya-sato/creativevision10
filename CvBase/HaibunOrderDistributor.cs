namespace CvBase;

/// <summary>受注1件×SKU の受注残（割り付け先の候補）</summary>
/// <param name="Id_Juchu">受注Id</param>
/// <param name="JuchuDay">受注日 yyyyMMdd（割り付け順に使う）</param>
/// <param name="ZanSu">配分できる受注残（受注 − 出荷売上 − 修正できない配分）</param>
/// <param name="Tanka">受注明細の単価</param>
/// <param name="Jodai">受注明細の上代</param>
/// <param name="Gedai">受注明細の下代</param>
public readonly record struct HaibunOrderZan(long Id_Juchu, string JuchuDay, int ZanSu, int Tanka, int Jodai, int Gedai);

/// <summary>割り付け結果の1行。<see cref="Id_Juchu"/>=0 は受注残を超えた分（受注に紐付かない配分）</summary>
public readonly record struct HaibunOrderShare(long Id_Juchu, int Su, int Tanka, int Jodai, int Gedai);

/// <summary>
/// 受注配分(商品別)の割り付け規則。DBに依存しない純粋関数だけを置き、画面(CvWpfclient)と単体テストの両方から使う。
/// <para>
/// 割付規則は本クラスの FillByStock / Distribute、受入条件は Tests/TestServer/HaibunOrderDistributorTests.cs を参照する。
/// </para>
/// </summary>
public static class HaibunOrderDistributor {
	/// <summary>
	/// 得意先1件×SKU の配分数を、その得意先の受注へ <b>受注日・受注Idの古い順</b> に、各受注の受注残を上限として割り付ける。
	/// 割り付け切れない超過分は <c>Id_Juchu=0</c> の1行にし、最も新しい受注の単価・上代・下代を使う。
	/// </summary>
	/// <param name="su">配分数。0以下なら空</param>
	/// <param name="orders">同じ得意先・同じSKUの受注残</param>
	public static IReadOnlyList<HaibunOrderShare> Distribute(int su, IEnumerable<HaibunOrderZan> orders) {
		if (su <= 0) return [];
		var sorted = orders
			.OrderBy(x => x.JuchuDay, StringComparer.Ordinal)
			.ThenBy(x => x.Id_Juchu)
			.ToList();
		var result = new List<HaibunOrderShare>();
		var remaining = su;
		foreach (var order in sorted) {
			if (remaining == 0) break;
			var share = Math.Min(remaining, Math.Max(order.ZanSu, 0));
			if (share <= 0) continue;
			result.Add(new HaibunOrderShare(order.Id_Juchu, share, order.Tanka, order.Jodai, order.Gedai));
			remaining -= share;
		}
		if (remaining > 0) {
			var newest = sorted.Count > 0 ? sorted[^1] : default;
			result.Add(new HaibunOrderShare(0, remaining, newest.Tanka, newest.Jodai, newest.Gedai));
		}
		return result;
	}

	/// <summary>
	/// 有効在庫の範囲で、優先順に受注残を割り当てる（「在庫内で受注日順に読込」）。
	/// </summary>
	/// <param name="available">有効在庫。0以下なら全て0</param>
	/// <param name="zanInPriorityOrder">優先順（受注日の古い得意先から）に並べた受注残</param>
	/// <returns>入力と同じ順の割当数</returns>
	public static int[] FillByStock(int available, IReadOnlyList<int> zanInPriorityOrder) {
		var result = new int[zanInPriorityOrder.Count];
		var remaining = Math.Max(available, 0);
		for (int i = 0; i < zanInPriorityOrder.Count && remaining > 0; i++) {
			var take = Math.Min(Math.Max(zanInPriorityOrder[i], 0), remaining);
			result[i] = take;
			remaining -= take;
		}
		return result;
	}
}
