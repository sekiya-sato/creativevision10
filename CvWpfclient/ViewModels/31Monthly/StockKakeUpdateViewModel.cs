using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

public partial class StockKakeUpdateViewModel : BaseViewModel {
	private sealed class ShimeRow {
		public int Shime1 { get; set; }
	}
	private sealed class ClosingSummaryRow {
		public string TorihikiCode { get; set; } = string.Empty;
		public string DayTo { get; set; } = string.Empty;
		public int Shime1 { get; set; }
	}
	private sealed record ClosingMismatch(string KakeType, string TorihikiCode, string SavedDayTo, int CurrentShime);

	public IReadOnlyList<string> UpdateTargets { get; } = ["全て", "在庫のみ", "売掛のみ", "買掛のみ"];

	[ObservableProperty]
	public partial string YearMonthFrom { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string YearMonthTo { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string UpdateTarget { get; set; } = "全て";

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "年月を yyyy/MM 形式で入力し、実行を押してください。";

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(YearMonthFrom, out string yymmFrom)) {
			StatusMessage = $"開始年月の形式が不正です: {YearMonthFrom}";
			MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			return;
		}
		if (!TryParseYearMonth(YearMonthTo, out string yymmTo)) {
			StatusMessage = $"終了年月の形式が不正です: {YearMonthTo}";
			MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			return;
		}
		if (string.Compare(yymmFrom, yymmTo, StringComparison.Ordinal) > 0) {
			StatusMessage = "開始年月は終了年月以前にしてください。";
			MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			return;
		}

		List<string> targetMonths = BuildMonthList(yymmFrom, yymmTo);
		string confirmMessage = targetMonths.Count == 1
			? $"{targetMonths[0]} の{UpdateTarget}を実行しますか？"
			: $"{targetMonths[0]} ～ {targetMonths[^1]} の{UpdateTarget}を実行しますか？";
		if (UpdateTarget is "全て" or "売掛のみ" or "買掛のみ") {
			confirmMessage += "\n請求残・支払残も再作成します。";
		}

		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			StatusMessage = "締日変更を確認しています...";
			var warning = await GetRebuildClosingMismatchWarningAsync(yymmFrom, yymmTo, cancellationToken);
			if (!string.IsNullOrEmpty(warning)) {
				StatusMessage = warning;
				MessageEx.ShowWarningDialog(warning, owner: ClientLib.GetActiveView(this));
				return;
			}
		}
		catch (OperationCanceledException) {
			StatusMessage = "締日変更の確認をキャンセルしました。";
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = "締日変更の確認をキャンセルしました。";
			return;
		}
		catch (Exception ex) {
			StatusMessage = $"締日変更の確認でエラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "処理を開始します...";
		ClientLib.Cursor2Wait();

		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var requests = await CreateSummaryRequestsAsync(targetMonths, yymmFrom, yymmTo, cancellationToken);
			int processedCount = 0;

			foreach (var request in requests) {
				cancellationToken.ThrowIfCancellationRequested();

				StatusMessage = $"{request.Name} の処理中...";
				await foreach (var streamMsg in coreService.QueryMsgStreamAsync(request.Message, AppGlobal.GetDefaultCallContext(cancellationToken))) {
					if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
						StatusMessage = streamMsg.DataMsg;
					}
					ProgressValue = (int)Math.Round(
						(processedCount * 100d + Math.Clamp(streamMsg.Progress, 0, 100)) / requests.Count,
						MidpointRounding.AwayFromZero);
					if (streamMsg.IsError) {
						throw new InvalidOperationException(streamMsg.DataMsg);
					}
					if (streamMsg.IsCompleted) {
						break;
					}
				}

				processedCount++;
				ProgressValue = (int)Math.Round(processedCount * 100d / requests.Count, MidpointRounding.AwayFromZero);
			}

			StatusMessage = $"{UpdateTarget}が完了しました。対象: {targetMonths[0]} ～ {targetMonths[^1]}";
		}
		catch (OperationCanceledException) {
			StatusMessage = "処理をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = "処理をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"エラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	private static bool TryParseYearMonth(string input, out string yyyymm) {
		yyyymm = string.Empty;
		if (string.IsNullOrWhiteSpace(input)) {
			return false;
		}
		string trimmed = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal);
		if (trimmed.Length != 6 || !DateTime.TryParseExact(trimmed + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymm = trimmed;
		return true;
	}

	private async Task<List<(string Name, CvMsg Message)>> CreateSummaryRequestsAsync(IReadOnlyList<string> targetMonths, string yymmFrom, string yymmTo, CancellationToken cancellationToken) {
		List<(string Name, CvMsg Message)> requests = [];
		bool includesUriKake = UpdateTarget is "全て" or "売掛のみ";
		bool includesKaiKake = UpdateTarget is "全て" or "買掛のみ";
		if (UpdateTarget is "全て" or "在庫のみ") {
			requests.AddRange(targetMonths.Select(yyyymm => ($"在庫 {yyyymm}", CreateSummaryMessage(
				CvFlag.Msg051_SummaryRealStock,
				typeof(CalcDateParameter),
				new CalcDateParameter(yyyymm)))));
		}
		if (includesUriKake) {
			requests.Add(($"売掛 {yymmFrom} ～ {yymmTo}", CreateSummaryMessage(
				CvFlag.Msg052_SummaryUriKake,
				typeof(CalcDateTermParameter),
				new CalcDateTermParameter(yymmFrom, yymmTo))));
		}
		if (includesKaiKake) {
			requests.Add(($"買掛 {yymmFrom} ～ {yymmTo}", CreateSummaryMessage(
				CvFlag.Msg053_SummaryKaiKake,
				typeof(CalcDateTermParameter),
				new CalcDateTermParameter(yymmFrom, yymmTo))));
		}
		if (includesUriKake) {
			var shimes = await GetClosingDaysAsync(nameof(MasterTokui), cancellationToken);
			requests.AddRange(from yyyymm in targetMonths
				from shime in shimes
				select ($"請求残 {yyyymm} / {FormatShime(shime)}", CreateSummaryMessage(
					CvFlag.Msg056_SummaryUriSei,
					typeof(BillingParameter),
					new BillingParameter(yyyymm, shime, string.Empty, string.Empty, IsReissue: false))));
		}
		if (includesKaiKake) {
			var shimes = await GetClosingDaysAsync(nameof(MasterShiire), cancellationToken);
			requests.AddRange(from yyyymm in targetMonths
				from shime in shimes
				select ($"支払残 {yyyymm} / {FormatShime(shime)}", CreateSummaryMessage(
					CvFlag.Msg057_SummaryKaiShi,
					typeof(BillingParameter),
					new BillingParameter(yyyymm, shime, string.Empty, string.Empty, IsReissue: false))));
		}
		return requests;
	}

	private async Task<string> GetRebuildClosingMismatchWarningAsync(string yymmFrom, string yymmTo, CancellationToken cancellationToken) {
		List<ClosingMismatch> mismatches = [];
		if (UpdateTarget is "全て" or "売掛のみ") {
			var rows = await QueryClosingSummaryRowsAsync(
				$"""
SELECT t.Code AS TorihikiCode, s.DayTo, t.Shime1
FROM {nameof(SummaryUriSei)} AS s
INNER JOIN {nameof(MasterTokui)} AS t ON t.Id = s.Id_Tokui
WHERE substr(s.DayTo, 1, 6) BETWEEN @0 AND @1
ORDER BY t.Code, s.DayTo
""",
				yymmFrom,
				yymmTo,
				cancellationToken);
			mismatches.AddRange(FindClosingMismatches("売掛", rows));
		}
		if (UpdateTarget is "全て" or "買掛のみ") {
			var rows = await QueryClosingSummaryRowsAsync(
				$"""
SELECT t.Code AS TorihikiCode, s.DayTo, t.Shime1
FROM {nameof(SummaryKaiShi)} AS s
INNER JOIN {nameof(MasterShiire)} AS t ON t.Id = s.Id_Shiire
WHERE substr(s.DayTo, 1, 6) BETWEEN @0 AND @1
ORDER BY t.Code, s.DayTo
""",
				yymmFrom,
				yymmTo,
				cancellationToken);
			mismatches.AddRange(FindClosingMismatches("買掛", rows));
		}
		return BuildClosingMismatchWarning(mismatches);
	}

	private async Task<List<ClosingSummaryRow>> QueryClosingSummaryRowsAsync(string sql, string yymmFrom, string yymmTo, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		return await QuerySqlListAsync<ClosingSummaryRow>(sql, [yymmFrom, yymmTo], cancellationToken);
	}

	private static List<ClosingMismatch> FindClosingMismatches(string kakeType, IEnumerable<ClosingSummaryRow> rows) =>
		[.. rows.Where(row => !TryGetExpectedClosingDay(row.DayTo, row.Shime1, out var expectedDay) || row.DayTo != expectedDay)
			.Select(row => new ClosingMismatch(kakeType, row.TorihikiCode, row.DayTo, row.Shime1))];

	private static bool TryGetExpectedClosingDay(string savedDayTo, int shime, out string expectedDay) {
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

	private static string BuildClosingMismatchWarning(IReadOnlyList<ClosingMismatch> mismatches) {
		if (mismatches.Count == 0) return string.Empty;
		var lines = mismatches.Take(5).Select(mismatch =>
			$"{mismatch.KakeType}: {mismatch.TorihikiCode} / 保存締日 {mismatch.SavedDayTo} / 現在締日 {FormatShime(mismatch.CurrentShime)}");
		var remain = mismatches.Count > 5 ? $"\nほか{mismatches.Count - 5}件" : string.Empty;
		return $"締日変更を検出したため、再更新を開始しません。\n{string.Join("\n", lines)}{remain}\nマスタ締日が変更されています。請求計算／支払計算画面で対象を手動再計算し、旧締日の残データを確認してから再実行してください";
	}

	private async Task<List<int>> GetClosingDaysAsync(string masterTableName, CancellationToken cancellationToken) {
		var rows = await QuerySqlListAsync<ShimeRow>(
			$"SELECT DISTINCT Shime1 FROM {masterTableName} WHERE Shime1 BETWEEN 1 AND 31 OR Shime1 = 99 ORDER BY Shime1",
			[],
			cancellationToken);
		return rows.Select(x => x.Shime1).ToList();
	}

	private async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
		cancellationToken.ThrowIfCancellationRequested();
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList rows
			? rows.Cast<T>().ToList()
			: [];
	}

	private static CvMsg CreateSummaryMessage(CvFlag flag, Type dataType, object parameter) => new() {
		Code = 0,
		Flag = flag,
		DataType = dataType,
		DataMsg = Common.SerializeObject(parameter)
	};

	private static string FormatShime(int shime) => shime switch {
		99 => "末日",
		>= 1 and <= 31 => $"{shime:00}日",
		_ => $"不正({shime})"
	};

	private static List<string> BuildMonthList(string yyyymmFrom, string yyyymmTo) {
		List<string> list = [];
		DateTime current = DateTime.ParseExact(yyyymmFrom + "01", "yyyyMMdd", CultureInfo.InvariantCulture);
		DateTime end = DateTime.ParseExact(yyyymmTo + "01", "yyyyMMdd", CultureInfo.InvariantCulture);

		while (current <= end) {
			list.Add(current.ToString("yyyyMM", CultureInfo.InvariantCulture));
			current = current.AddMonths(1);
		}

		return list;
	}
}
