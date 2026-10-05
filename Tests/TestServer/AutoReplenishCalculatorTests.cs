using System;
using System.Linq;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class AutoReplenishCalculatorTests {
	[TestMethod]
	public void Calculate_SourceShortageConsumesPlannedSupplyBeforeStoreDemand() {
		var rows = AutoReplenishCalculator.Calculate(20, 25, 6, 0,
			[new(1, "T01", 0, 8, 0, 0, 0, 0)]);
		CollectionAssert.AreEqual(new[] {
			new AutoReplenishQuantity(0, 5, 0, 5, 0),
			new AutoReplenishQuantity(1, 8, 0, 1, 7),
		}, rows.ToArray());
	}

	[TestMethod]
	public void Calculate_StoreNetStockIncomingAndTransitReduceDemand() {
		var rows = AutoReplenishCalculator.Calculate(3, 0, 1, 0,
			[new(1, "T01", 0, 10, 5, 2, 2, 1)]);
		Assert.AreEqual(new AutoReplenishQuantity(1, 4, 3, 1, 0), rows.Single());
	}

	[TestMethod]
	public void Calculate_SharedSupplyUsesPriorityThenCodeThenIdOnlyOnce() {
		var rows = AutoReplenishCalculator.Calculate(3, 0, 3, 1, [
			new(4, "B", 1, 3, 0, 0, 0, 0),
			new(3, "A", 1, 3, 0, 0, 0, 0),
			new(2, "A", 1, 3, 0, 0, 0, 0),
			new(1, "Z", 0, 2, 0, 0, 0, 0),
		]);
		CollectionAssert.AreEqual(new[] {
			new AutoReplenishQuantity(1, 2, 2, 0, 0),
			new AutoReplenishQuantity(2, 3, 1, 2, 0),
			new AutoReplenishQuantity(3, 3, 0, 2, 1),
			new AutoReplenishQuantity(4, 3, 0, 0, 3),
		}, rows.ToArray());
	}

	[TestMethod]
	public void Calculate_NoDemandReturnsEmptyAndNegativeTransitProvidesNoSupply() {
		Assert.AreEqual(0, AutoReplenishCalculator.Calculate(0, 0, 0, 0,
			[new(1, "T01", 0, 10, 10, 0, 0, 0)]).Count);
		Assert.AreEqual(new AutoReplenishQuantity(1, 2, 0, 0, 2),
			AutoReplenishCalculator.Calculate(0, 0, -5, -2,
				[new(1, "T01", 0, 2, 0, 0, 0, -8)]).Single());
	}

	[TestMethod]
	public void Calculate_QuantityAndIntermediateOverflowAreRejected() {
		Assert.AreEqual(int.MaxValue, AutoReplenishCalculator.Calculate(0, int.MaxValue, 0, 0, []).Single().Su);
		Assert.ThrowsExactly<OverflowException>(() => AutoReplenishCalculator.Calculate(0, (long)int.MaxValue + 1, 0, 0, []));
		Assert.ThrowsExactly<OverflowException>(() => AutoReplenishCalculator.Calculate(long.MinValue, 1, 0, 0, []));
		Assert.ThrowsExactly<OverflowException>(() => AutoReplenishCalculator.Calculate(0, 0, long.MaxValue, 1, []));
	}
}
