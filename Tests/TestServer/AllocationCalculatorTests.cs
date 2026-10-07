using System.Linq;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 配分の按分計算（<see cref="AllocationCalculator"/>）の単体テスト。
/// 同数は入力順に残数を配り、比率は切捨／四捨五入・合計0・負の重み・超過補正の結果を固定する。
/// </summary>
[TestClass]
public class AllocationCalculatorTests {
	[TestMethod]
	public void Equal_FillsFromTopAndStopsWhenStockRunsOut() {
		CollectionAssert.AreEqual(new[] { 2, 2, 1, 0 }, AllocationCalculator.Equal(2, 5, 4));
		CollectionAssert.AreEqual(new[] { 3, 3 }, AllocationCalculator.Equal(3, 100, 2), "在庫が余れば同数まで");
		CollectionAssert.AreEqual(new[] { 0, 0 }, AllocationCalculator.Equal(3, -1, 2));
		CollectionAssert.AreEqual(new[] { 0, 0 }, AllocationCalculator.Equal(0, 10, 2));
		Assert.AreEqual(0, AllocationCalculator.Equal(1, 10, 0).Length);
	}

	[TestMethod]
	public void ByRatio_FloorLeavesRemainderUnallocated() {
		// 10 × (5,3,2)/10 = 5,3,2 ちょうど
		CollectionAssert.AreEqual(new[] { 5, 3, 2 }, AllocationCalculator.ByRatio(10, [5m, 3m, 2m], AllocationRounding.Floor));
		// 10 × 1/3 = 3.33.. → 切捨で 3,3,3（余り1は配らない）
		CollectionAssert.AreEqual(new[] { 3, 3, 3 }, AllocationCalculator.ByRatio(10, [1m, 1m, 1m], AllocationRounding.Floor));
	}

	[TestMethod]
	public void ByRatio_RoundNeverExceedsTotal() {
		// 10 × (1,1,1,1)/4 = 2.5 → 四捨五入で 3×4=12 になるので、後ろの行から2つ減らして10に収める
		var result = AllocationCalculator.ByRatio(10, [1m, 1m, 1m, 1m], AllocationRounding.Round);
		Assert.AreEqual(10, result.Sum());
		CollectionAssert.AreEqual(new[] { 3, 3, 2, 2 }, result);
		// 7 × (0.6, 0.4) = 4.2, 2.8 → 4, 3 = 7
		CollectionAssert.AreEqual(new[] { 4, 3 }, AllocationCalculator.ByRatio(7, [0.6m, 0.4m], AllocationRounding.Round));
	}

	[TestMethod]
	public void ByRatio_ZeroWeightsBecomeEqualAndNegativeIsZero() {
		CollectionAssert.AreEqual(new[] { 2, 2 }, AllocationCalculator.ByRatio(4, [0m, 0m], AllocationRounding.Floor), "比率が全て0なら均等");
		CollectionAssert.AreEqual(new[] { 0, 4 }, AllocationCalculator.ByRatio(4, [-3m, 1m], AllocationRounding.Floor), "負の比率は0");
		CollectionAssert.AreEqual(new[] { 0, 0 }, AllocationCalculator.ByRatio(0, [1m, 1m], AllocationRounding.Round));
		Assert.AreEqual(0, AllocationCalculator.ByRatio(5, [], AllocationRounding.Round).Length);
	}
}
