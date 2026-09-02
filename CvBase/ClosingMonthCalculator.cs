using System.Globalization;

namespace CvBase;

/// <summary>
/// 自社締日を基準に、伝票の計上月と計上月に属する実日付範囲を求める。
/// </summary>
public static class ClosingMonthCalculator {
	/// <summary>計上月に属する実日付範囲。</summary>
	public readonly record struct KakeMonthPeriod(string DayFrom, string DayTo);

	/// <summary>
	/// 計算対象日付の日が締日を超えた場合は翌月、それ以外は当月を返す。
	/// 末締め(99)も同じ比較で必ず当月になる。
	/// </summary>
	public static string CalculateKakeMonth(string targetDay, int shime) {
		if (!DateTime.TryParseExact(targetDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) {
			throw new ArgumentException("計算対象日付はyyyyMMdd形式で指定してください。", nameof(targetDay));
		}
		return CalculateKakeMonth(day, shime);
	}

	/// <inheritdoc cref="CalculateKakeMonth(string, int)"/>
	public static string CalculateKakeMonth(DateTime targetDay, int shime) {
		ValidateShime(shime);
		var month = new DateTime(targetDay.Year, targetDay.Month, 1);
		if (targetDay.Day > shime) {
			month = month.AddMonths(1);
		}
		return month.ToString("yyyyMM", CultureInfo.InvariantCulture);
	}

	/// <summary>単一計上月に属する実日付範囲を返す。</summary>
	public static KakeMonthPeriod GetPeriod(string kakeMonth, int shime) {
		var month = ParseMonth(kakeMonth, nameof(kakeMonth));
		ValidateShime(shime);
		if (shime == (int)Share.EnumShime.DayLast) {
			return new KakeMonthPeriod(
				month.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				month.AddMonths(1).AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
		}

		return new KakeMonthPeriod(
			month.AddMonths(-1).AddDays(shime).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			month.AddDays(shime - 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	/// <summary>計上月範囲に属する連続した実日付範囲を返す。</summary>
	public static KakeMonthPeriod GetPeriodRange(string kakeMonthFrom, string kakeMonthTo, int shime) {
		var from = ParseMonth(kakeMonthFrom, nameof(kakeMonthFrom));
		var to = ParseMonth(kakeMonthTo, nameof(kakeMonthTo));
		if (from > to) {
			throw new ArgumentException("開始計上月は終了計上月以前にしてください。", nameof(kakeMonthFrom));
		}
		var first = GetPeriod(kakeMonthFrom, shime);
		var last = GetPeriod(kakeMonthTo, shime);
		return new KakeMonthPeriod(first.DayFrom, last.DayTo);
	}

	/// <summary>yyyyMMへ月数を加算する。</summary>
	public static string AddMonths(string kakeMonth, int months) =>
		ParseMonth(kakeMonth, nameof(kakeMonth)).AddMonths(months).ToString("yyyyMM", CultureInfo.InvariantCulture);

	/// <summary>運用上有効な自社締日(1～28、99)か検証する。</summary>
	public static void ValidateShime(int shime) {
		if (shime is < 1 or > 28 && shime != (int)Share.EnumShime.DayLast) {
			throw new ArgumentOutOfRangeException(nameof(shime), "自社締日は1から28または99で指定してください。");
		}
	}

	/// <summary>
	/// 指定月における締日の実日付を返す(99は月末、月末を超える指定は<c>Math.Min(shime, 月末日)</c>で丸める)。
	/// <see cref="ClosingDaySet"/> など締日→日付ロジックを要する共通処理向けのヘルパ。
	/// </summary>
	public static DateTime GetClosingDate(DateTime month, int shime) {
		ValidateShime(shime);
		var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
		return new DateTime(month.Year, month.Month, shime == (int)Share.EnumShime.DayLast ? lastDay : Math.Min(shime, lastDay));
	}

	private static DateTime ParseMonth(string value, string parameterName) {
		if (!DateTime.TryParseExact(value + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) {
			throw new ArgumentException("計上月はyyyyMM形式で指定してください。", parameterName);
		}
		return month;
	}
}
