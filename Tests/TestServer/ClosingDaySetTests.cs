using System;
using System.Globalization;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

[TestClass]
public class ClosingDaySetTests {
	[TestMethod]
	[DataRow(99, 0, 0, 20, new[] { 99 })]
	[DataRow(10, 20, 99, 20, new[] { 10, 20, 99 })]
	[DataRow(0, 0, 0, 20, new[] { 20 })]
	public void Resolve_ReturnsAscendingEffectiveDays(int shime1, int shime2, int shime3, int ownShime, int[] expected) {
		var actual = ClosingDaySet.Resolve(shime1, shime2, shime3, ownShime);

		CollectionAssert.AreEqual(expected, (System.Collections.ICollection)actual);
	}

	[TestMethod]
	[DataRow(99, 0, 0)]
	[DataRow(10, 20, 99)]
	[DataRow(0, 0, 0)]
	public void Validate_AcceptsValidCombinations(int shime1, int shime2, int shime3) {
		Assert.AreEqual("", ClosingDaySet.Validate(shime1, shime2, shime3));
	}

	[TestMethod]
	[DataRow(20, 10, 0)]
	public void Validate_RejectsDescendingOrder(int shime1, int shime2, int shime3) {
		Assert.AreNotEqual("", ClosingDaySet.Validate(shime1, shime2, shime3));
	}

	[TestMethod]
	[DataRow(0, 20, 0)]
	[DataRow(10, 0, 99)]
	public void Validate_RejectsGapBeforeNonZero(int shime1, int shime2, int shime3) {
		Assert.AreNotEqual("", ClosingDaySet.Validate(shime1, shime2, shime3));
	}

	[TestMethod]
	[DataRow(20, 20, 0)]
	public void Validate_RejectsDuplicate(int shime1, int shime2, int shime3) {
		var error = ClosingDaySet.Validate(shime1, shime2, shime3);
		Assert.AreEqual("同じ締日が重複しています。", error);
	}

	[TestMethod]
	[DataRow(29, 0, 0)]
	[DataRow(0, 0, 32)]
	public void Validate_RejectsOutOfRangeValue(int shime1, int shime2, int shime3) {
		var error = ClosingDaySet.Validate(shime1, shime2, shime3);
		Assert.AreEqual("締日は1〜28日または末日で指定してください。", error);
	}

	[TestMethod]
	[DataRow(28, 99, 0)]
	[DataRow(10, 28, 99)]
	public void Validate_RejectsDay28WithMonthEnd(int shime1, int shime2, int shime3) {
		// 平年2月は28日と末日が同じ日付(DenDay)になり、互いの請求残・支払残を上書きするため併用させない。
		var error = ClosingDaySet.Validate(shime1, shime2, shime3);
		Assert.AreEqual("28日と末日は2月に同じ日付になるため、同時に設定できません。", error);
	}

	[TestMethod]
	[DataRow(27, 99, 0)]
	[DataRow(10, 28, 0)]
	public void Validate_AcceptsDay28OrMonthEndAlone(int shime1, int shime2, int shime3) {
		Assert.AreEqual("", ClosingDaySet.Validate(shime1, shime2, shime3));
	}

	[TestMethod]
	public void GetBillingPeriod_ThrowsWhenDay28AndMonthEndCollideInFebruary() {
		// 既存データに28日と末日の併用が残っていても、空期間の行で請求残を上書きせず例外で止める。
		Assert.ThrowsExactly<InvalidOperationException>(() => ClosingDaySet.GetBillingPeriod("202602", [28, 99], 99));
		// うるう年は28日と29日(末日)で逆転しない。
		var leap = ClosingDaySet.GetBillingPeriod("202402", [28, 99], 99);
		Assert.AreEqual("20240229", leap.DayFrom);
		Assert.AreEqual("20240229", leap.DayTo);
	}

	[TestMethod]
	[DataRow(1, "")]
	[DataRow(28, "")]
	[DataRow(99, "")]
	[DataRow(0, "自社締日は1〜28日または末日で指定してください。")]
	[DataRow(31, "自社締日は1〜28日または末日で指定してください。")]
	public void ValidateOwnShime_AcceptsOnly1To28OrMonthEnd(int shimeBi, string expected) {
		Assert.AreEqual(expected, ClosingDaySet.ValidateOwnShime(shimeBi));
	}

	[TestMethod]
	public void EnsureMasterSaveValid_ChecksClosingDaysOnInsertAndOnChange() {
		// 追加は常に検査する。
		Assert.ThrowsExactly<ArgumentException>(() => ClosingDaySet.EnsureMasterSaveValid(new MasterTokui { Shime1 = 20, Shime2 = 10 }, null));
		Assert.ThrowsExactly<ArgumentException>(() => ClosingDaySet.EnsureMasterSaveValid(new MasterShiire { Shime1 = 28, Shime2 = 99 }, null));
		Assert.ThrowsExactly<ArgumentException>(() => ClosingDaySet.EnsureMasterSaveValid(new MasterSysman { ShimeBi = 0 }, null));
		ClosingDaySet.EnsureMasterSaveValid(new MasterTokui { Shime1 = 10, Shime2 = 20, Shime3 = 99 }, null);
		ClosingDaySet.EnsureMasterSaveValid(new MasterSysman { ShimeBi = 99 }, null);

		// 更新は締日列が変わったときだけ検査し、締日と無関係な更新を既存値で止めない。
		ClosingDaySet.EnsureMasterSaveValid(new MasterTokui { Shime1 = 20, Shime2 = 10 }, new MasterTokui { Shime1 = 20, Shime2 = 10 });
		ClosingDaySet.EnsureMasterSaveValid(new MasterSysman { ShimeBi = 0 }, new MasterSysman { ShimeBi = 0 });
		Assert.ThrowsExactly<ArgumentException>(() => ClosingDaySet.EnsureMasterSaveValid(new MasterTokui { Shime1 = 0, Shime2 = 20 }, new MasterTokui { Shime1 = 99 }));
		Assert.ThrowsExactly<ArgumentException>(() => ClosingDaySet.EnsureMasterSaveValid(new MasterSysman { ShimeBi = 0 }, new MasterSysman { ShimeBi = 99 }));

		// 締日を持たない型は対象外。
		ClosingDaySet.EnsureMasterSaveValid(new MasterShain(), null);
	}

	[TestMethod]
	public void IsClosingDayColumn_DeniesClosingDayPartialUpdate() {
		Assert.IsTrue(ClosingDaySet.IsClosingDayColumn(typeof(MasterTokui), nameof(MasterTokui.Shime2)));
		Assert.IsTrue(ClosingDaySet.IsClosingDayColumn(typeof(MasterShiire), nameof(MasterShiire.Shime1)));
		Assert.IsTrue(ClosingDaySet.IsClosingDayColumn(typeof(MasterSysman), nameof(MasterSysman.ShimeBi)));
		Assert.IsFalse(ClosingDaySet.IsClosingDayColumn(typeof(MasterTokui), nameof(MasterTokui.PayDay)));
		Assert.IsFalse(ClosingDaySet.IsClosingDayColumn(typeof(MasterShain), "Shime1"));
	}

	[TestMethod]
	// 3.3 境界例(請求月 202609)。
	[DataRow("202609", new[] { 99 }, 99, "20260901", "20260930")]
	[DataRow("202609", new[] { 20 }, 20, "20260821", "20260920")]
	[DataRow("202609", new[] { 10, 20, 99 }, 10, "20260901", "20260910")]
	[DataRow("202609", new[] { 10, 20, 99 }, 20, "20260911", "20260920")]
	[DataRow("202609", new[] { 10, 20, 99 }, 99, "20260921", "20260930")]
	[DataRow("202609", new[] { 20, 99 }, 20, "20260901", "20260920")]
	[DataRow("202609", new[] { 20, 99 }, 99, "20260921", "20260930")]
	[DataRow("202609", new[] { 28 }, 28, "20260829", "20260928")]
	// 2月は28日が月末のため 20260228 + 1日 = 20260301。
	[DataRow("202603", new[] { 28 }, 28, "20260301", "20260328")]
	// うるう年(202402は29日月)でも28締めの丸めは変わらない。
	[DataRow("202402", new[] { 28 }, 28, "20240129", "20240228")]
	// うるう年の末日締め。DayToが29日になり、翌月の開始日は 20240229 + 1日 = 20240301。
	[DataRow("202402", new[] { 99 }, 99, "20240201", "20240229")]
	[DataRow("202403", new[] { 99 }, 99, "20240301", "20240331")]
	// 年またぎ: 前月が前年になる。
	[DataRow("202601", new[] { 99 }, 99, "20260101", "20260131")]
	[DataRow("202601", new[] { 10 }, 10, "20251211", "20260110")]
	public void GetBillingPeriod_MatchesDesignedBoundaryTable(string billingYyyymm, int[] days, int targetShime, string expectedFrom, string expectedTo) {
		var actual = ClosingDaySet.GetBillingPeriod(billingYyyymm, days, targetShime);

		Assert.AreEqual(expectedFrom, actual.DayFrom);
		Assert.AreEqual(expectedTo, actual.DayTo);
	}

	[TestMethod]
	[DataRow("202609", 10)]
	[DataRow("202609", 20)]
	[DataRow("202609", 99)]
	[DataRow("202402", 28)]
	[DataRow("202601", 99)]
	public void GetBillingPeriod_MatchesLegacySingleShimeCalculation(string billingYyyymm, int shime) {
		// 単一締日のときは現行の「前月の同じ締日+1」と完全に一致すること(回帰防止)。
		// CvDomainLogic/SummaryDb.cs:1141-1151 の GetClosingPeriod と同じ算式を期待値として並べる。
		var billingMonth = DateTime.ParseExact(billingYyyymm, "yyyyMM", CultureInfo.InvariantCulture);
		var expectedDayTo = ClosingMonthCalculator.GetClosingDate(billingMonth, shime);
		var expectedDayFrom = ClosingMonthCalculator.GetClosingDate(billingMonth.AddMonths(-1), shime).AddDays(1);

		var actual = ClosingDaySet.GetBillingPeriod(billingYyyymm, new[] { shime }, shime);

		Assert.AreEqual(expectedDayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture), actual.DayFrom);
		Assert.AreEqual(expectedDayTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture), actual.DayTo);
	}

	[TestMethod]
	[DataRow(new[] { 10, 20, 99 }, 20, true)]
	[DataRow(new[] { 10, 20, 99 }, 30, false)]
	public void Contains_ChecksMembership(int[] days, int targetShime, bool expected) {
		Assert.AreEqual(expected, ClosingDaySet.Contains(days, targetShime));
	}

	[TestMethod]
	public void ContainsShimeSql_BuildsFallbackAwareFragment() {
		var actual = ClosingDaySet.ContainsShimeSql("t", "@2", "@7");

		Assert.AreEqual("(t.Shime1 = @2 OR t.Shime2 = @2 OR t.Shime3 = @2 OR (t.Shime1 = 0 AND @7 = @2))", actual);
	}
}
