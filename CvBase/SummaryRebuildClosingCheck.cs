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
/// 対象年月・締日まで展開済みの再作成要求。クライアントはこの記述子を一対一で表示名とメッセージへ変換する。
/// </summary>
public sealed record SummaryRebuildRequestDescriptor(CvFlag Flag, string YearMonthFrom, string YearMonthTo, string? TargetMonth = null, int? Shime = null);

/// <summary>
/// 再作成対象ごとのメッセージ順序と確認文を一元化する。
/// </summary>
public static class SummaryRebuildRequestPlanner {
	public static bool RequiresUriClosingDays(string updateTarget) => GetFlagOrder(updateTarget).Contains(CvFlag.Msg056_SummaryUriSei);

	public static bool RequiresKaiClosingDays(string updateTarget) => GetFlagOrder(updateTarget).Contains(CvFlag.Msg057_SummaryKaiShi);

	public static bool IncludesUriKake(string updateTarget) => GetFlagOrder(updateTarget).Contains(CvFlag.Msg052_SummaryUriKake);

	public static bool IncludesKaiKake(string updateTarget) => GetFlagOrder(updateTarget).Contains(CvFlag.Msg053_SummaryKaiKake);

	public static string GetClosingSummaryConfirmation(string updateTarget) => (RequiresUriClosingDays(updateTarget), RequiresKaiClosingDays(updateTarget)) switch {
		(true, true) => "請求残・支払残も再作成します。",
		(true, false) => "請求残も再作成します。",
		(false, true) => "支払残も再作成します。",
		_ => string.Empty,
	};

	/// <summary>
	/// 対象年月・実在締日まで展開した実行記述子を、実行順で作成する。
	/// </summary>
	public static IReadOnlyList<SummaryRebuildRequestDescriptor> CreateDescriptors(
		string updateTarget,
		IReadOnlyList<string> targetMonths,
		IReadOnlyList<int> uriClosingDays,
		IReadOnlyList<int> kaiClosingDays,
		string yearMonthFrom,
		string yearMonthTo) {
		ArgumentOutOfRangeException.ThrowIfZero(targetMonths.Count);
		List<SummaryRebuildRequestDescriptor> descriptors = [];
		foreach (var flag in GetFlagOrder(updateTarget)) {
			switch (flag) {
				case CvFlag.Msg051_SummaryRealStock:
					descriptors.AddRange(targetMonths.Select(targetMonth => new SummaryRebuildRequestDescriptor(flag, yearMonthFrom, yearMonthTo, targetMonth)));
					break;
				case CvFlag.Msg052_SummaryUriKake:
				case CvFlag.Msg053_SummaryKaiKake:
					descriptors.Add(new SummaryRebuildRequestDescriptor(flag, yearMonthFrom, yearMonthTo));
					break;
				case CvFlag.Msg056_SummaryUriSei:
					descriptors.AddRange(from targetMonth in targetMonths
						from shime in uriClosingDays
						select new SummaryRebuildRequestDescriptor(flag, yearMonthFrom, yearMonthTo, targetMonth, shime));
					break;
				case CvFlag.Msg057_SummaryKaiShi:
					descriptors.AddRange(from targetMonth in targetMonths
						from shime in kaiClosingDays
						select new SummaryRebuildRequestDescriptor(flag, yearMonthFrom, yearMonthTo, targetMonth, shime));
					break;
			}
		}
		return descriptors;
	}

	private static IReadOnlyList<CvFlag> GetFlagOrder(string updateTarget) => updateTarget switch {
		"全て" => [CvFlag.Msg051_SummaryRealStock, CvFlag.Msg052_SummaryUriKake, CvFlag.Msg053_SummaryKaiKake, CvFlag.Msg056_SummaryUriSei, CvFlag.Msg057_SummaryKaiShi],
		"在庫のみ" => [CvFlag.Msg051_SummaryRealStock],
		"売掛のみ" => [CvFlag.Msg052_SummaryUriKake, CvFlag.Msg056_SummaryUriSei],
		"買掛のみ" => [CvFlag.Msg053_SummaryKaiKake, CvFlag.Msg057_SummaryKaiShi],
		_ => throw new ArgumentOutOfRangeException(nameof(updateTarget), updateTarget, "更新対象が不正です。")
	};
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
/// 締日照会、要求記述子作成、要求作成、送信を直列に実行する境界。
/// </summary>
public static class SummaryRebuildRequestDispatchGate {
	public static async Task<SummaryRebuildDispatchResult<TDescriptor>> ExecuteAsync<TDescriptor, TRequest>(
		Func<CancellationToken, Task<IReadOnlyList<SummaryClosingMismatch>>> getMismatchesAsync,
		Func<CancellationToken, Task<IReadOnlyList<TDescriptor>>> createDescriptorsAsync,
		Func<TDescriptor, TRequest> createRequest,
		Func<TDescriptor, TRequest, int, int, CancellationToken, Task> sendAsync,
		CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		var mismatches = await getMismatchesAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		if (!SummaryRebuildClosingCheck.CanStartRequestDispatch(mismatches)) {
			return new SummaryRebuildDispatchResult<TDescriptor>(mismatches, []);
		}
		var descriptors = await createDescriptorsAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		for (var index = 0; index < descriptors.Count; index++) {
			cancellationToken.ThrowIfCancellationRequested();
			var descriptor = descriptors[index];
			var request = createRequest(descriptor);
			await sendAsync(descriptor, request, index, descriptors.Count, cancellationToken);
		}
		return new SummaryRebuildDispatchResult<TDescriptor>(mismatches, descriptors);
	}
}

/// <summary>
/// 締日照会結果と、送信済みまたは不一致で空の要求記述子列。
/// </summary>
public sealed record SummaryRebuildDispatchResult<TDescriptor>(IReadOnlyList<SummaryClosingMismatch> Mismatches, IReadOnlyList<TDescriptor> Descriptors) {
	public bool CanStartRequestDispatch => SummaryRebuildClosingCheck.CanStartRequestDispatch(Mismatches);
}
