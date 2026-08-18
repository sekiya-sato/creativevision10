using System.Globalization;

namespace CvBase;

/// <summary>
/// 請求残・支払残の再作成前に、保存済み締日と現在マスタ締日を照合するためのSQL結果行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// </summary>
public sealed class SummaryClosingCheckRow {
	public string TorihikiCode { get; set; } = string.Empty;
	public string DayTo { get; set; } = string.Empty;
	public int Shime1 { get; set; }
}

/// <summary>
/// 保存済み締日と現在マスタ締日の不一致。
/// </summary>
public sealed record SummaryClosingMismatch(string KakeType, string TorihikiCode, string SavedDayTo, int CurrentShime);

/// <summary>
/// 在庫・売掛・買掛再作成における締日変更検査の共通規則。
/// </summary>
public static class SummaryRebuildClosingCheck {
	public const string NoSavedSummaryRowNotice = "保存済み集計行がない場合は、締日変更を検出できません。";
	public const string ManualRecalculationGuidance = "マスタ締日が変更されています。請求計算／支払計算画面で対象を手動再計算し、旧締日の残データを確認してから再実行してください";

	public static bool IncludesUriKake(string updateTarget) => updateTarget is "全て" or "売掛のみ";

	public static bool IncludesKaiKake(string updateTarget) => updateTarget is "全て" or "買掛のみ";

	/// <summary>
	/// 締日照会が正常に完了した後に、再作成要求の作成へ進めるかを返す。
	/// 照会の例外・キャンセルは呼出元へ伝播させるため、この判定へ到達せず要求を送信しない。
	/// </summary>
	public static bool CanStartRequestDispatch(IReadOnlyList<SummaryClosingMismatch> mismatches) => mismatches.Count == 0;

	/// <summary>
	/// 保存済みの締日が、その年月の現在締日と異なる行を返す。締日または年月が不正な行も安全のため不一致として扱う。
	/// </summary>
	public static List<SummaryClosingMismatch> FindMismatches(string kakeType, IEnumerable<SummaryClosingCheckRow> rows) =>
		[.. rows.Where(row => !TryGetExpectedClosingDay(row.DayTo, row.Shime1, out var expectedDay) || row.DayTo != expectedDay)
			.Select(row => new SummaryClosingMismatch(kakeType, row.TorihikiCode, row.DayTo, row.Shime1))];

	public static bool TryGetExpectedClosingDay(string savedDayTo, int shime, out string expectedDay) {
		expectedDay = string.Empty;
		if (savedDayTo.Length < 6 || !DateTime.TryParseExact(savedDayTo[..6] + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) {
			return false;
		}
		if (shime == 99) {
			expectedDay = new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			return true;
		}
		if (shime is < 1 or > 31) {
			return false;
		}
		expectedDay = new DateTime(month.Year, month.Month, Math.Min(shime, DateTime.DaysInMonth(month.Year, month.Month))).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	public static string BuildMismatchWarning(IReadOnlyList<SummaryClosingMismatch> mismatches) {
		if (mismatches.Count == 0) return string.Empty;
		var lines = mismatches.Take(5).Select(mismatch =>
			$"{mismatch.KakeType}: {mismatch.TorihikiCode} / 保存締日 {mismatch.SavedDayTo} / 現在締日 {FormatShime(mismatch.CurrentShime)}");
		var remain = mismatches.Count > 5 ? $"\nほか{mismatches.Count - 5}件" : string.Empty;
		return $"締日変更を検出したため、再更新を開始しません。\n{string.Join("\n", lines)}{remain}\n{ManualRecalculationGuidance}";
	}

	public static string FormatShime(int shime) => shime switch {
		99 => "末日",
		>= 1 and <= 31 => $"{shime:00}日",
		_ => $"不正({shime})"
	};
}
