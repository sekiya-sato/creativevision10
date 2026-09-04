using System.Globalization;

namespace CvBase;

/// <summary>棚卸基準日の解決結果。</summary>
/// <param name="Id_Shop">店舗Id</param>
/// <param name="TanaDay">棚卸基準日 yyyyMMdd</param>
/// <param name="SumMonth">基準日が属する計上月 yyyyMM</param>
/// <param name="DayFrom">計上月の初日 yyyyMMdd</param>
/// <param name="DayTo">計上月の最終日 yyyyMMdd</param>
/// <param name="IsFallback">棚卸日が未設定で計上月末へフォールバックしたか</param>
public readonly record struct StocktakeDay(
	long Id_Shop,
	string TanaDay,
	string SumMonth,
	string DayFrom,
	string DayTo,
	bool IsFallback);

/// <summary>
/// 倉庫(店舗)別の棚卸基準日(<c>Tran60TanaDate.TanaDay</c>)から計上月・計上月末日を解決する。
/// DB非依存の純ロジックであり、<see cref="ClosingMonthCalculator"/> へ日付計算を委譲する。
/// </summary>
public static class StocktakeDaySet {
	/// <summary><c>Tran60TanaDate.TanaDay</c>/<c>FixDay</c>の既定値(未設定)。</summary>
	public const string UnsetDay = "19010101";

	/// <summary>TanaDay が未設定(null/空白/<see cref="UnsetDay"/>/yyyyMMdd形式でない)か。</summary>
	public static bool IsUnset(string? tanaDay) {
		if (string.IsNullOrWhiteSpace(tanaDay) || tanaDay == UnsetDay) {
			return true;
		}
		return !DateTime.TryParseExact(tanaDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
	}

	/// <summary>
	/// 店舗1件の棚卸基準日を解決する(設計書2.1)。
	/// <paramref name="tanaDay"/> が未設定(<see cref="IsUnset"/>)なら、<paramref name="fallbackMonth"/> の
	/// 計上月末日を基準日とし<c>IsFallback=true</c>を返す(月末一括運用へのフォールバック)。
	/// </summary>
	public static StocktakeDay Resolve(long idShop, string? tanaDay, int shime, string fallbackMonth) {
		ClosingMonthCalculator.ValidateShime(shime);
		ValidateMonth(fallbackMonth, nameof(fallbackMonth));

		if (IsUnset(tanaDay)) {
			var fallbackPeriod = ClosingMonthCalculator.GetPeriod(fallbackMonth, shime);
			return new StocktakeDay(idShop, fallbackPeriod.DayTo, fallbackMonth, fallbackPeriod.DayFrom, fallbackPeriod.DayTo, true);
		}

		var sumMonth = ClosingMonthCalculator.CalculateKakeMonth(tanaDay!, shime);
		var period = ClosingMonthCalculator.GetPeriod(sumMonth, shime);
		return new StocktakeDay(idShop, tanaDay!, sumMonth, period.DayFrom, period.DayTo, false);
	}

	private static void ValidateMonth(string value, string parameterName) {
		if (!DateTime.TryParseExact(value + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			throw new ArgumentException("計上月はyyyyMM形式で指定してください。", parameterName);
		}
	}
}
