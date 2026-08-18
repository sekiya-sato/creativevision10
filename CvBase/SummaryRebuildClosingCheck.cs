using CodeShare;
using System.Globalization;

namespace CvBase;

/// <summary>
/// 請求残・支払残の再作成前に、保存済み締日と現在マスタ締日を照合するためのSQL結果行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// </summary>
public sealed class SummaryClosingCheckRow {
	public string TorihikiCode { get; set; } = string.Empty;
	public string? DayTo { get; set; }
	public int Shime1 { get; set; }
}

/// <summary>
/// 保存済み締日と現在マスタ締日の不一致。
/// </summary>
public sealed record SummaryClosingMismatch(string KakeType, string TorihikiCode, string? SavedDayTo, int CurrentShime);

/// <summary>
/// 再作成するメッセージ種別と、対象年月・締日ごとの展開規則。
/// </summary>
public sealed record SummaryRebuildRequestPlanStep(CvFlag Flag, bool IsPerMonth, bool IsPerClosingDay);

/// <summary>
/// 再作成対象ごとのメッセージ順序と確認文を一元化する。
/// </summary>
public static class SummaryRebuildRequestPlanner {
	public static IReadOnlyList<SummaryRebuildRequestPlanStep> CreatePlan(string updateTarget) => updateTarget switch {
		"全て" => [
			new(CvFlag.Msg051_SummaryRealStock, true, false),
			new(CvFlag.Msg052_SummaryUriKake, false, false),
			new(CvFlag.Msg053_SummaryKaiKake, false, false),
			new(CvFlag.Msg056_SummaryUriSei, true, true),
			new(CvFlag.Msg057_SummaryKaiShi, true, true),
		],
		"在庫のみ" => [new(CvFlag.Msg051_SummaryRealStock, true, false)],
		"売掛のみ" => [
			new(CvFlag.Msg052_SummaryUriKake, false, false),
			new(CvFlag.Msg056_SummaryUriSei, true, true),
		],
		"買掛のみ" => [
			new(CvFlag.Msg053_SummaryKaiKake, false, false),
			new(CvFlag.Msg057_SummaryKaiShi, true, true),
		],
		_ => throw new ArgumentOutOfRangeException(nameof(updateTarget), updateTarget, "更新対象が不正です。")
	};

	public static bool IncludesUriKake(string updateTarget) => CreatePlan(updateTarget).Any(step => step.Flag == CvFlag.Msg052_SummaryUriKake);

	public static bool IncludesKaiKake(string updateTarget) => CreatePlan(updateTarget).Any(step => step.Flag == CvFlag.Msg053_SummaryKaiKake);

	public static string GetClosingSummaryConfirmation(string updateTarget) => (IncludesUriKake(updateTarget), IncludesKaiKake(updateTarget)) switch {
		(true, true) => "請求残・支払残も再作成します。",
		(true, false) => "請求残も再作成します。",
		(false, true) => "支払残も再作成します。",
		_ => string.Empty,
	};

	/// <summary>
	/// 締日数まで展開した実行フラグ順。要求組立とテストで同じ計画を使用する。
	/// </summary>
	public static IReadOnlyList<CvFlag> CreateFlagPlan(string updateTarget, int monthCount, int uriClosingCount, int kaiClosingCount) {
		ArgumentOutOfRangeException.ThrowIfNegative(monthCount);
		ArgumentOutOfRangeException.ThrowIfNegative(uriClosingCount);
		ArgumentOutOfRangeException.ThrowIfNegative(kaiClosingCount);
		List<CvFlag> flags = [];
		foreach (var step in CreatePlan(updateTarget)) {
			var count = step.IsPerMonth ? monthCount : 1;
			if (step.IsPerClosingDay) {
				count *= step.Flag == CvFlag.Msg056_SummaryUriSei ? uriClosingCount : kaiClosingCount;
			}
			for (var index = 0; index < count; index++) {
				flags.Add(step.Flag);
			}
		}
		return flags;
	}
}

/// <summary>
/// 在庫・売掛・買掛再作成における締日変更検査の共通規則。
/// </summary>
public static class SummaryRebuildClosingCheck {
	public const string NoSavedSummaryRowNotice = "保存済み集計行がない場合は、締日変更を検出できません。";
	public const string ManualRecalculationGuidance = "マスタ締日が変更されています。請求計算／支払計算画面で対象を手動再計算し、旧締日の残データを確認してから再実行してください";
	public const string UriClosingCheckSql = """
SELECT t.Code AS TorihikiCode, s.DayTo, t.Shime1
FROM SummaryUriSei AS s
INNER JOIN MasterTokui AS t ON t.Id = s.Id_Tokui
WHERE substr(s.DenDay, 1, 6) BETWEEN @0 AND @1
ORDER BY t.Code, s.DenDay, s.Id
""";
	public const string KaiClosingCheckSql = """
SELECT t.Code AS TorihikiCode, s.DayTo, t.Shime1
FROM SummaryKaiShi AS s
INNER JOIN MasterShiire AS t ON t.Id = s.Id_Shiire
WHERE substr(s.DenDay, 1, 6) BETWEEN @0 AND @1
ORDER BY t.Code, s.DenDay, s.Id
""";

	public static bool IncludesUriKake(string updateTarget) => SummaryRebuildRequestPlanner.IncludesUriKake(updateTarget);

	public static bool IncludesKaiKake(string updateTarget) => SummaryRebuildRequestPlanner.IncludesKaiKake(updateTarget);

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

	public static bool TryGetExpectedClosingDay(string? savedDayTo, int shime, out string expectedDay) {
		expectedDay = string.Empty;
		if (string.IsNullOrWhiteSpace(savedDayTo) || savedDayTo.Length != 8 || !DateTime.TryParseExact(savedDayTo[..6] + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) {
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

/// <summary>
/// 締日照会の完了前には再作成要求を作らない送信境界。
/// </summary>
public static class SummaryRebuildRequestDispatchGate {
	public static async Task<SummaryRebuildRequestPreparation<TRequest>> PrepareAsync<TRequest>(
		Func<CancellationToken, Task<IReadOnlyList<SummaryClosingMismatch>>> getMismatchesAsync,
		Func<CancellationToken, Task<IReadOnlyList<TRequest>>> createRequestsAsync,
		CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		var mismatches = await getMismatchesAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		if (!SummaryRebuildClosingCheck.CanStartRequestDispatch(mismatches)) {
			return new SummaryRebuildRequestPreparation<TRequest>(mismatches, []);
		}
		var requests = await createRequestsAsync(cancellationToken);
		return new SummaryRebuildRequestPreparation<TRequest>(mismatches, requests);
	}
}

/// <summary>
/// 締日照会結果と、送信可能な場合だけ作成された要求列。
/// </summary>
public sealed record SummaryRebuildRequestPreparation<TRequest>(IReadOnlyList<SummaryClosingMismatch> Mismatches, IReadOnlyList<TRequest> Requests) {
	public bool CanStartRequestDispatch => SummaryRebuildClosingCheck.CanStartRequestDispatch(Mismatches);
}
