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

	/// <summary>
	/// 入力計上月が属する会計年度の決算期末月(yyyyMM)を返す。
	/// 評価替えの適用時点=期末(原価4項目 詳細設計 §16.4)で、対象計上月を読み替えるために使う。
	/// <para>
	/// 現在時刻に依存する「未来月」判定は含めない。純粋な年月演算だけにしてあるので、
	/// 設計書§16.4の例(期首4月・入力202608→202703、入力202702→202703)を
	/// 現在時刻に左右されず単体テストで固定できる。
	/// </para>
	/// <para>
	/// 入力月・期首月をともに「西暦0年1月を0とする絶対月インデックス」へ変換し、期首月と同じ剰余を持つ
	/// 直近(入力月以下)の会計年度開始月インデックスを求める。決算期末月はその11か月後(=開始から12か月目)。
	/// 期首月が1月(会計年度=暦年)の場合も含めて破綻しない。
	/// </para>
	/// <para>
	/// サーバー(評価替えの対象期間解決)とクライアント(画面での読み替え結果の事前表示)の双方が使うため、
	/// <see cref="ClosingMonthCalculator"/> と同じく計上月の暦の規則を持つ本クラスへ置く。
	/// </para>
	/// </summary>
	/// <param name="targetMonth">入力計上月 yyyyMM。</param>
	/// <param name="fiscalStartMonth">会計年度の期首月(1〜12)。</param>
	/// <returns>入力計上月が属する会計年度の決算期末月 yyyyMM。</returns>
	public static string ResolveFiscalYearEndMonth(string targetMonth, int fiscalStartMonth) {
		var month = ParseMonth(targetMonth, nameof(targetMonth));
		if (fiscalStartMonth is < 1 or > 12) {
			throw new ArgumentOutOfRangeException(nameof(fiscalStartMonth), fiscalStartMonth, "期首月は1〜12で指定してください。");
		}
		var inputIdx = (month.Year * 12) + (month.Month - 1);
		var startMonth0 = fiscalStartMonth - 1;
		var offset = ((inputIdx - startMonth0) % 12 + 12) % 12;
		var fiscalEndIdx = inputIdx - offset + 11;
		return $"{fiscalEndIdx / 12:D4}{fiscalEndIdx % 12 + 1:D2}";
	}
}
