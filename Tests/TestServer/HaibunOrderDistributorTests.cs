using System.Linq;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 受注配分(商品別)の割り付け規則（<see cref="HaibunOrderDistributor"/>）の単体テスト。
/// HaibunOrderDistributor.FillByStock / Distribute の割付規則を固定する。
/// </summary>
[TestClass]
public class HaibunOrderDistributorTests {
	[TestMethod]
	public void Distribute_FillsOldestOrderFirstUpToZan() {
		var result = HaibunOrderDistributor.Distribute(7, [
			new(Id_Juchu: 30, JuchuDay: "20260903", ZanSu: 5, Tanka: 300, Jodai: 0, Gedai: 0),
			new(Id_Juchu: 10, JuchuDay: "20260901", ZanSu: 4, Tanka: 100, Jodai: 0, Gedai: 0),
			new(Id_Juchu: 20, JuchuDay: "20260901", ZanSu: 2, Tanka: 200, Jodai: 0, Gedai: 0),
		]);

		CollectionAssert.AreEqual(new long[] { 10, 20, 30 }, result.Select(x => x.Id_Juchu).ToArray(), "受注日→受注Idの古い順");
		CollectionAssert.AreEqual(new[] { 4, 2, 1 }, result.Select(x => x.Su).ToArray());
		CollectionAssert.AreEqual(new[] { 100, 200, 300 }, result.Select(x => x.Tanka).ToArray(), "単価は各受注の明細から");
	}

	[TestMethod]
	public void Distribute_OverflowBecomesUnlinkedRowWithNewestPrice() {
		var result = HaibunOrderDistributor.Distribute(10, [
			new(10, "20260901", 3, 100, 1000, 50),
			new(20, "20260905", 2, 120, 1200, 60),
		]);

		Assert.AreEqual(3, result.Count);
		Assert.AreEqual(0, result[2].Id_Juchu, "受注残を超えた分は受注に紐付かない");
		Assert.AreEqual(5, result[2].Su);
		Assert.AreEqual(120, result[2].Tanka, "最も新しい受注の単価");
		Assert.AreEqual(1200, result[2].Jodai);
		Assert.AreEqual(10, result.Sum(x => x.Su), "合計は配分数と一致する");
	}

	[TestMethod]
	public void Distribute_SkipsZeroZanAndReturnsEmptyForZero() {
		Assert.AreEqual(0, HaibunOrderDistributor.Distribute(0, [new(10, "20260901", 5, 100, 0, 0)]).Count);
		var result = HaibunOrderDistributor.Distribute(2, [
			new(10, "20260901", 0, 100, 0, 0),
			new(20, "20260902", -1, 100, 0, 0),
			new(30, "20260903", 5, 100, 0, 0),
		]);
		Assert.AreEqual(1, result.Count);
		Assert.AreEqual(30, result[0].Id_Juchu);
		Assert.AreEqual(2, result[0].Su);
	}

	[TestMethod]
	public void FillByStock_AllocatesInPriorityOrderWithinStock() {
		CollectionAssert.AreEqual(new[] { 3, 4, 1, 0 }, HaibunOrderDistributor.FillByStock(8, [3, 4, 5, 2]));
		CollectionAssert.AreEqual(new[] { 3, 0, 4 }, HaibunOrderDistributor.FillByStock(10, [3, 0, 4]), "在庫が余れば受注残まで");
		CollectionAssert.AreEqual(new[] { 0, 0 }, HaibunOrderDistributor.FillByStock(-2, [3, 4]), "有効在庫がマイナスなら割り当てない");
	}
}
