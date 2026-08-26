using System;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class ClosingMonthCalculatorTests {
	[TestMethod]
	[DataRow("20260720", 20, "202607")]
	[DataRow("20260721", 20, "202608")]
	[DataRow("20260820", 20, "202608")]
	[DataRow("20260821", 20, "202609")]
	[DataRow("20260801", 1, "202608")]
	[DataRow("20260802", 1, "202609")]
	[DataRow("20280228", 28, "202802")]
	[DataRow("20280229", 28, "202803")]
	[DataRow("20260831", 99, "202608")]
	public void CalculateKakeMonth_UsesDayGreaterThanShime(string targetDay, int shime, string expected) {
		Assert.AreEqual(expected, ClosingMonthCalculator.CalculateKakeMonth(targetDay, shime));
	}

	[TestMethod]
	[DataRow("202608", 20, "20260721", "20260820")]
	[DataRow("202601", 20, "20251221", "20260120")]
	[DataRow("202608", 99, "20260801", "20260831")]
	public void GetPeriod_ReturnsClosingDateRange(string kakeMonth, int shime, string expectedFrom, string expectedTo) {
		var actual = ClosingMonthCalculator.GetPeriod(kakeMonth, shime);

		Assert.AreEqual(expectedFrom, actual.DayFrom);
		Assert.AreEqual(expectedTo, actual.DayTo);
	}

	[TestMethod]
	public void GetPeriodRange_ReturnsContinuousOuterRange() {
		var actual = ClosingMonthCalculator.GetPeriodRange("202608", "202609", 20);

		Assert.AreEqual("20260721", actual.DayFrom);
		Assert.AreEqual("20260920", actual.DayTo);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(29)]
	[DataRow(31)]
	public void CalculateKakeMonth_RejectsUnsupportedShime(int shime) {
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			ClosingMonthCalculator.CalculateKakeMonth("20260801", shime));
	}
}
