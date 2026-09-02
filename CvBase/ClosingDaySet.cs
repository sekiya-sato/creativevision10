using System.Globalization;

namespace CvBase;

/// <summary>
/// 取引先の締日1/2/3(<c>Shime1</c>/<c>Shime2</c>/<c>Shime3</c>)から有効締日リストと締請求期間を解決する。
/// DB非依存の純ロジックであり、<see cref="CvBase"/> から <c>CvDomainLogic</c>・<c>CvWpfclient</c> の双方で共用する。
/// </summary>
public static class ClosingDaySet {
	/// <summary>
	/// 取引先1件の有効締日リストを解決する。戻り値は昇順・1件以上。
	/// <c>shime1 == 0</c>(未設定)のときは自社締日(<c>ownShime</c>)へフォールバックする。
	/// それ以外は shime1/shime2/shime3 から 0 を除いたものを昇順で返す。
	/// </summary>
	public static IReadOnlyList<int> Resolve(int shime1, int shime2, int shime3, int ownShime) {
		if (shime1 == 0) {
			return [ownShime];
		}
		var days = new List<int>(3) { shime1 };
		if (shime2 != 0) days.Add(shime2);
		if (shime3 != 0) days.Add(shime3);
		days.Sort();
		return days;
	}

	/// <summary>
	/// マスタ保守の保存前バリデーション。締日1/2/3の値域・前詰め・昇順・重複を検査する。
	/// 違反があればエラー文を、正常なら空文字を返す。
	/// </summary>
	public static string Validate(int shime1, int shime2, int shime3) {
		// V4: 値域は 0(未設定) / 1〜28 / 99(末日)。EnumShime の ComboBox に29〜31は存在しない(3.5)。
		foreach (var s in new[] { shime1, shime2, shime3 }) {
			if (s != 0 && s is not (>= 1 and <= 28) && s != (int)Share.EnumShime.DayLast) {
				return "締日は1〜28日または末日で指定してください。";
			}
		}
		// V2: 前詰め。0の後ろに0以外を置けない。
		if (shime1 == 0 && shime2 != 0) {
			return "締日は締日1から順に設定してください。締日1が未設定のため締日2は設定できません。";
		}
		if (shime2 == 0 && shime3 != 0) {
			return "締日は締日1から順に設定してください。締日2が未設定のため締日3は設定できません。";
		}
		// V3: 重複禁止。V1(昇順)より先に判定し専用メッセージを出す。
		if ((shime1 != 0 && shime1 == shime2) || (shime2 != 0 && shime2 == shime3) || (shime1 != 0 && shime1 == shime3)) {
			return "同じ締日が重複しています。";
		}
		// V1: 0以外は昇順。
		if ((shime2 != 0 && shime1 >= shime2) || (shime3 != 0 && shime2 >= shime3)) {
			return "締日は小さい順に設定してください。（締日1 < 締日2 < 締日3）";
		}
		return "";
	}

	/// <summary>
	/// 請求月・対象締日・有効締日リストから締請求期間(DayFrom〜DayTo)を求める(3.3)。
	/// <paramref name="days"/> は <see cref="Resolve"/> の戻り値(昇順)を渡すこと。
	/// </summary>
	public static (string DayFrom, string DayTo) GetBillingPeriod(string billingYyyymm, IReadOnlyList<int> days, int targetShime) {
		if (days.Count == 0) {
			throw new ArgumentException("有効締日リストは1件以上指定してください。", nameof(days));
		}
		if (!DateTime.TryParseExact(billingYyyymm, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var billingMonth)) {
			throw new ArgumentException("請求月はyyyyMM形式で指定してください。", nameof(billingYyyymm));
		}
		var index = -1;
		for (var i = 0; i < days.Count; i++) {
			if (days[i] == targetShime) {
				index = i;
				break;
			}
		}
		if (index < 0) {
			throw new ArgumentException("対象締日は有効締日リストに含まれている必要があります。", nameof(targetShime));
		}

		// 対象締日が最小要素なら直前の締めは前月の最大要素、それ以外は同月内の1つ前の要素(3.3)。
		var isPrevMonth = index == 0;
		var prevShime = isPrevMonth ? days[^1] : days[index - 1];
		var prevMonth = isPrevMonth ? billingMonth.AddMonths(-1) : billingMonth;

		var dayTo = ClosingMonthCalculator.GetClosingDate(billingMonth, targetShime);
		var dayFrom = ClosingMonthCalculator.GetClosingDate(prevMonth, prevShime).AddDays(1);
		return (dayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture), dayTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	/// <summary>有効締日リストに対象締日が含まれるか。</summary>
	public static bool Contains(IReadOnlyList<int> days, int targetShime) => days.Contains(targetShime);

	/// <summary>
	/// 別名<paramref name="alias"/>の取引先が締日<paramref name="shimeParam"/>を持つかを判定するSQL断片(4.5)。
	/// <c>Shime1 = 0</c>(未設定)は自社締日(<paramref name="ownShimeParam"/>)へフォールバックする(3.1)。
	/// 値は必ずパラメータバインドで渡すこと(SQL文字列へ埋め込まない)。
	/// </summary>
	public static string ContainsShimeSql(string alias, string shimeParam, string ownShimeParam) =>
		$"({alias}.Shime1 = {shimeParam} OR {alias}.Shime2 = {shimeParam} OR {alias}.Shime3 = {shimeParam}" +
		$" OR ({alias}.Shime1 = 0 AND {ownShimeParam} = {shimeParam}))";
}
