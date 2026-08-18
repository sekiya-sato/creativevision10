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
	private sealed record RebuildRequestSnapshot(string UpdateTarget, string YearMonthFrom, string YearMonthTo, IReadOnlyList<string> TargetMonths);

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

		var snapshot = new RebuildRequestSnapshot(UpdateTarget, yymmFrom, yymmTo, [.. BuildMonthList(yymmFrom, yymmTo)]);
		string confirmMessage = BuildConfirmMessage(snapshot);

		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "締日変更を確認しています...";
		ClientLib.Cursor2Wait();
		try {
			var result = await SummaryRebuildRequestDispatchGate.ExecuteAsync(
				ct => GetRebuildClosingMismatchesAsync(snapshot, ct),
				ct => CreateSummaryRequestDescriptorsAsync(snapshot, ct),
				CreateSummaryRequest,
				async (descriptor, request, requestIndex, requestCount, ct) => {
					StatusMessage = $"{request.Name} の処理中...";
					var coreService = AppGlobal.GetGrpcService<ICoreService>();
					await foreach (var streamMsg in coreService.QueryMsgStreamAsync(request.Message, AppGlobal.GetDefaultCallContext(ct))) {
						if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
							StatusMessage = streamMsg.DataMsg;
						}
						ProgressValue = (int)Math.Round(
							(requestIndex * 100d + Math.Clamp(streamMsg.Progress, 0, 100)) / requestCount,
							MidpointRounding.AwayFromZero);
						if (streamMsg.IsError) {
							throw new InvalidOperationException(streamMsg.DataMsg);
						}
						if (streamMsg.IsCompleted) {
							break;
						}
					}
					ProgressValue = (int)Math.Round((requestIndex + 1) * 100d / requestCount, MidpointRounding.AwayFromZero);
				},
				cancellationToken);
			if (!result.CanStartRequestDispatch) {
				var warning = SummaryRebuildClosingCheck.BuildMismatchWarning(result.Mismatches);
				StatusMessage = warning;
				MessageEx.ShowWarningDialog(warning, owner: ClientLib.GetActiveView(this));
				return;
			}

			ProgressValue = 100;
			StatusMessage = $"{snapshot.UpdateTarget}が完了しました。対象: {snapshot.TargetMonths[0]} ～ {snapshot.TargetMonths[^1]}";
		}
		catch (OperationCanceledException) {
			StatusMessage = "締日変更の確認または処理をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = "締日変更の確認または処理をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"締日変更の確認または処理でエラーが発生しました: {ex.Message}";
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

	private static string BuildConfirmMessage(RebuildRequestSnapshot snapshot) {
		string message = snapshot.TargetMonths.Count == 1
			? $"{snapshot.TargetMonths[0]} の{snapshot.UpdateTarget}を実行しますか？"
			: $"{snapshot.TargetMonths[0]} ～ {snapshot.TargetMonths[^1]} の{snapshot.UpdateTarget}を実行しますか？";
		var closingSummaryConfirmation = SummaryRebuildRequestPlanner.GetClosingSummaryConfirmation(snapshot.UpdateTarget);
		if (!string.IsNullOrEmpty(closingSummaryConfirmation)) {
			message += $"\n{closingSummaryConfirmation}\n{SummaryRebuildClosingCheck.NoSavedSummaryRowNotice}";
		}
		return message;
	}

	private async Task<IReadOnlyList<SummaryRebuildRequestDescriptor>> CreateSummaryRequestDescriptorsAsync(RebuildRequestSnapshot snapshot, CancellationToken cancellationToken) {
		IReadOnlyList<int> uriClosingDays = SummaryRebuildRequestPlanner.RequiresUriClosingDays(snapshot.UpdateTarget)
			? await GetClosingDaysAsync(nameof(MasterTokui), cancellationToken)
			: [];
		IReadOnlyList<int> kaiClosingDays = SummaryRebuildRequestPlanner.RequiresKaiClosingDays(snapshot.UpdateTarget)
			? await GetClosingDaysAsync(nameof(MasterShiire), cancellationToken)
			: [];
		return SummaryRebuildRequestPlanner.CreateDescriptors(
			snapshot.UpdateTarget,
			snapshot.TargetMonths,
			uriClosingDays,
			kaiClosingDays,
			snapshot.YearMonthFrom,
			snapshot.YearMonthTo);
	}

	private static (string Name, CvMsg Message) CreateSummaryRequest(SummaryRebuildRequestDescriptor descriptor) => descriptor switch {
		{ Flag: CvFlag.Msg051_SummaryRealStock, TargetMonth: { } targetMonth } => ($"在庫 {targetMonth}", CreateSummaryMessage(
			CvFlag.Msg051_SummaryRealStock,
			typeof(CalcDateParameter),
			new CalcDateParameter(targetMonth))),
		{ Flag: CvFlag.Msg052_SummaryUriKake } => ($"売掛 {descriptor.YearMonthFrom} ～ {descriptor.YearMonthTo}", CreateSummaryMessage(
			CvFlag.Msg052_SummaryUriKake,
			typeof(CalcDateTermParameter),
			new CalcDateTermParameter(descriptor.YearMonthFrom, descriptor.YearMonthTo))),
		{ Flag: CvFlag.Msg053_SummaryKaiKake } => ($"買掛 {descriptor.YearMonthFrom} ～ {descriptor.YearMonthTo}", CreateSummaryMessage(
			CvFlag.Msg053_SummaryKaiKake,
			typeof(CalcDateTermParameter),
			new CalcDateTermParameter(descriptor.YearMonthFrom, descriptor.YearMonthTo))),
		{ Flag: CvFlag.Msg056_SummaryUriSei, TargetMonth: { } targetMonth, Shime: { } shime } => ($"請求残 {targetMonth} / {SummaryRebuildClosingCheck.FormatShime(shime)}", CreateSummaryMessage(
			CvFlag.Msg056_SummaryUriSei,
			typeof(BillingParameter),
			new BillingParameter(targetMonth, shime, string.Empty, string.Empty, IsReissue: false))),
		{ Flag: CvFlag.Msg057_SummaryKaiShi, TargetMonth: { } targetMonth, Shime: { } shime } => ($"支払残 {targetMonth} / {SummaryRebuildClosingCheck.FormatShime(shime)}", CreateSummaryMessage(
			CvFlag.Msg057_SummaryKaiShi,
			typeof(BillingParameter),
			new BillingParameter(targetMonth, shime, string.Empty, string.Empty, IsReissue: false))),
		_ => throw new InvalidOperationException("再作成要求記述子が不正です。")
	};

	private async Task<IReadOnlyList<SummaryClosingMismatch>> GetRebuildClosingMismatchesAsync(RebuildRequestSnapshot snapshot, CancellationToken cancellationToken) {
		List<SummaryClosingMismatch> mismatches = [];
		if (SummaryRebuildClosingCheck.IncludesUriKake(snapshot.UpdateTarget)) {
			var rows = await QueryClosingSummaryRowsAsync(SummaryRebuildClosingCheck.UriClosingCheckSql,
				snapshot.YearMonthFrom,
				snapshot.YearMonthTo,
				cancellationToken);
			mismatches.AddRange(SummaryRebuildClosingCheck.FindMismatches("売掛", rows));
		}
		if (SummaryRebuildClosingCheck.IncludesKaiKake(snapshot.UpdateTarget)) {
			var rows = await QueryClosingSummaryRowsAsync(SummaryRebuildClosingCheck.KaiClosingCheckSql,
				snapshot.YearMonthFrom,
				snapshot.YearMonthTo,
				cancellationToken);
			mismatches.AddRange(SummaryRebuildClosingCheck.FindMismatches("買掛", rows));
		}
		return mismatches;
	}

	private async Task<List<SummaryClosingCheckRow>> QueryClosingSummaryRowsAsync(string sql, string yymmFrom, string yymmTo, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		return await QuerySqlListAsync<SummaryClosingCheckRow>(sql, [yymmFrom, yymmTo], cancellationToken);
	}

	private async Task<List<int>> GetClosingDaysAsync(string masterTableName, CancellationToken cancellationToken) {
		var rows = await QuerySqlListAsync<SummaryClosingCheckRow>(
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
