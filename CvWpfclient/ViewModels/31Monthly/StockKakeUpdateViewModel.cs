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
	[ObservableProperty]
	public partial string YearMonthFrom { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string YearMonthTo { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

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
			? $"{targetMonths[0]} の在庫・掛再更新を実行しますか？"
			: $"{targetMonths[0]} ～ {targetMonths[^1]} の在庫・掛再更新を {targetMonths.Count} ヶ月分実行しますか？";

		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "処理を開始します...";
		ClientLib.Cursor2Wait();

		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			int processedCount = 0;

			foreach (string yyyymm in targetMonths) {
				cancellationToken.ThrowIfCancellationRequested();

				var msg = new CvMsg {
					Code = 0,
					Flag = CvFlag.Msg051_SummaryRealStock,
					DataType = typeof(CalcDateParameter),
					DataMsg = Common.SerializeObject(new CalcDateParameter(yyyymm))
				};

				StatusMessage = $"{yyyymm} の処理中...";
				await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
					if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
						StatusMessage = streamMsg.DataMsg;
					}
					ProgressValue = streamMsg.Progress;
					if (streamMsg.IsCompleted || streamMsg.IsError) {
						break;
					}
				}

				processedCount++;
				ProgressValue = (int)Math.Round(processedCount * 100d / targetMonths.Count, MidpointRounding.AwayFromZero);
			}

			StatusMessage = $"在庫・掛再更新が完了しました。対象: {targetMonths[0]} ～ {targetMonths[^1]}";
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
