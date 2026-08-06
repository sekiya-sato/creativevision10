using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

public partial class StockKakeUpdateViewModel : BaseViewModel {
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

		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "処理を開始します...";
		ClientLib.Cursor2Wait();

		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var requests = CreateSummaryRequests(targetMonths, yymmFrom, yymmTo);
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

	private List<(string Name, CvMsg Message)> CreateSummaryRequests(IReadOnlyList<string> targetMonths, string yymmFrom, string yymmTo) {
		List<(string Name, CvMsg Message)> requests = [];
		if (UpdateTarget is "全て" or "在庫のみ") {
			requests.AddRange(targetMonths.Select(yyyymm => ($"在庫 {yyyymm}", CreateSummaryMessage(
				CvFlag.Msg051_SummaryRealStock,
				typeof(CalcDateParameter),
				new CalcDateParameter(yyyymm)))));
		}
		if (UpdateTarget is "全て" or "売掛のみ") {
			requests.Add(($"売掛 {yymmFrom} ～ {yymmTo}", CreateSummaryMessage(
				CvFlag.Msg052_SummaryUriKake,
				typeof(CalcDateTermParameter),
				new CalcDateTermParameter(yymmFrom, yymmTo))));
		}
		if (UpdateTarget is "全て" or "買掛のみ") {
			requests.Add(($"買掛 {yymmFrom} ～ {yymmTo}", CreateSummaryMessage(
				CvFlag.Msg053_SummaryKaiKake,
				typeof(CalcDateTermParameter),
				new CalcDateTermParameter(yymmFrom, yymmTo))));
		}
		return requests;
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
