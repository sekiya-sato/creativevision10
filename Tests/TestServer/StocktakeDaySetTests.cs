using System;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class StocktakeDaySetTests {
	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow(StocktakeDaySet.UnsetDay)]
	[DataRow("not-a-date")]
	[DataRow("20261301")]
	public void IsUnset_DetectsUnsetOrInvalidValues(string? tanaDay) {
		Assert.IsTrue(StocktakeDaySet.IsUnset(tanaDay));
	}

	[TestMethod]
	[DataRow("20260825")]
	[DataRow("19010102")]
	public void IsUnset_AcceptsValidDates(string tanaDay) {
		Assert.IsFalse(StocktakeDaySet.IsUnset(tanaDay));
	}

	[TestMethod]
	// 末締め(99)。8月下旬の棚卸は8月計上のまま。
	[DataRow("20260825", 99, "202608", "202608", "20260831")]
	// 20日締め。8月下旬の棚卸は9月計上へ繰り越す(設計書2.1)。
	[DataRow("20260825", 20, "202609", "202609", "20260920")]
	// 20日締め境界: 締日当日はまだ当月扱い。
	[DataRow("20260820", 20, "202608", "202608", "20260820")]
	// 20日締め境界: 締日の翌日から翌月扱い。
	[DataRow("20260821", 20, "202609", "202609", "20260920")]
	public void Resolve_UsesClosingMonthCalculatorWhenTanaDaySet(string tanaDay, int shime, string fallbackMonth, string expectedSumMonth, string expectedDayTo) {
		var actual = StocktakeDaySet.Resolve(1, tanaDay, shime, fallbackMonth);

		Assert.AreEqual(1, actual.Id_Shop);
		Assert.AreEqual(tanaDay, actual.TanaDay);
		Assert.AreEqual(expectedSumMonth, actual.SumMonth);
		Assert.AreEqual(expectedDayTo, actual.DayTo);
		Assert.IsFalse(actual.IsFallback);
	}

	[TestMethod]
	[DataRow(null, 99, "202609", "20260930")]
	[DataRow("", 99, "202609", "20260930")]
	[DataRow(StocktakeDaySet.UnsetDay, 99, "202609", "20260930")]
	[DataRow("not-a-date", 99, "202609", "20260930")]
	[DataRow(null, 20, "202609", "20260920")]
	[DataRow(StocktakeDaySet.UnsetDay, 20, "202609", "20260920")]
	public void Resolve_FallsBackToFallbackMonthEndWhenTanaDayUnset(string? tanaDay, int shime, string fallbackMonth, string expectedDayTo) {
		var actual = StocktakeDaySet.Resolve(2, tanaDay, shime, fallbackMonth);

		Assert.AreEqual(2, actual.Id_Shop);
		Assert.AreEqual(fallbackMonth, actual.SumMonth);
		Assert.AreEqual(expectedDayTo, actual.DayTo);
		Assert.AreEqual(expectedDayTo, actual.TanaDay);
		Assert.IsTrue(actual.IsFallback);
	}

	[TestMethod]
	[DataRow("20260101", 1)]
	[DataRow("20260228", 15)]
	[DataRow("20260825", 20)]
	[DataRow("20260828", 28)]
	[DataRow("20260831", 99)]
	[DataRow("20260215", 1)]
	[DataRow("20260101", 99)]
	public void Resolve_SatisfiesInvariant_DayFromLessOrEqualTanaDayLessOrEqualDayTo(string tanaDay, int shime) {
		var actual = StocktakeDaySet.Resolve(3, tanaDay, shime, "202601");

		var period = ClosingMonthCalculator.GetPeriod(actual.SumMonth, shime);
		Assert.IsTrue(string.CompareOrdinal(period.DayFrom, actual.TanaDay) <= 0);
		Assert.IsTrue(string.CompareOrdinal(actual.TanaDay, actual.DayTo) <= 0);
		Assert.AreEqual(period.DayTo, actual.DayTo);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(29)]
	[DataRow(98)]
	[DataRow(-1)]
	public void Resolve_RejectsInvalidShime(int shime) {
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			StocktakeDaySet.Resolve(1, "20260825", shime, "202609"));
	}

	[TestMethod]
	[DataRow("2026")]
	[DataRow("202613")]
	[DataRow("")]
	public void Resolve_RejectsInvalidFallbackMonth(string fallbackMonth) {
		Assert.ThrowsExactly<ArgumentException>(() =>
			StocktakeDaySet.Resolve(1, null, 20, fallbackMonth));
	}
}
