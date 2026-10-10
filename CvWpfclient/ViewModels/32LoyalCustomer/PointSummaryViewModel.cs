using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>
/// ポイント再計算: 店舗売上から指定年月(伝票日付の暦月)のポイント台帳・残高を再計算する。ポイント失効も実行する
/// </summary>
public partial class PointSummaryViewModel : BaseViewModel {
	[ObservableProperty]
	public partial string YearMonthFrom { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string YearMonthTo { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>失効基準日（yyyy/MM/dd）。既定は前日</summary>
	[ObservableProperty]
	public partial string ExpireBaseDay { get; set; } = DateTime.Today.AddDays(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "年月を yyyy/MM 形式で入力し、実行を押してください。";

	/// <summary>処理中。再計算と失効を同時に実行させないため、両コマンドの実行可否も更新する</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand), nameof(ExpireCommand))]
	public partial bool IsProcessing { get; set; }

	bool CanRun() => !IsProcessing;

	/// <summary>実行中の処理（再計算・失効のどちらか）を中止する</summary>
	[RelayCommand]
	private void CancelRunning() {
		if (ExecuteCommand.IsRunning) ExecuteCancelCommand.Execute(null);
		if (ExpireCommand.IsRunning) ExpireCancelCommand.Execute(null);
	}

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRun))]
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

		string termText = yymmFrom == yymmTo ? yymmFrom : $"{yymmFrom} ～ {yymmTo}";
		if (MessageEx.ShowQuestionDialog($"{termText} のポイント再計算を実行しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg063_PointRecalc,
			DataType = typeof(CalcDateTermParameter),
			DataMsg = Common.SerializeObject(new CalcDateTermParameter(yymmFrom, yymmTo))
		};
		await RunStreamAsync(message, "ポイント再計算", $"対象: {termText}", cancellationToken);
	}

	/// <summary>
	/// ポイント失効: 基準日時点で最終購入日から失効月数経過・退会した顧客の残高を失効させる。同じ基準日の再実行は二重に失効しない
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRun))]
	private async Task ExpireAsync(CancellationToken cancellationToken) {
		var day = ExpireBaseDay?.Trim().Replace("/", string.Empty, StringComparison.Ordinal) ?? string.Empty;
		if (day.Length != 8 || !DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			StatusMessage = $"失効基準日の形式が不正です: {ExpireBaseDay}";
			MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"基準日 {day} でポイント失効を実行しますか？\n最終購入日から失効月数を経過した顧客・退会顧客の残高を失効します。", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg067_PointExpire,
			DataType = typeof(PointExpireParameter),
			DataMsg = Common.SerializeObject(new PointExpireParameter(day))
		};
		await RunStreamAsync(message, "ポイント失効", $"基準日: {day}", cancellationToken);
	}

	/// <summary>ストリーム処理を実行し、進捗とメッセージを表示する</summary>
	private async Task RunStreamAsync(CvMsg message, string title, string targetText, CancellationToken cancellationToken) {
		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = $"{title}を開始しています...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var completed = false;
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StatusMessage = streamMsg.DataMsg;
				}
				ProgressValue = Math.Clamp(streamMsg.Progress, 0, 100);
				if (streamMsg.IsError) {
					throw new InvalidOperationException(streamMsg.DataMsg);
				}
				if (streamMsg.IsCompleted) {
					completed = true;
					break;
				}
			}
			// 完了通知を受け取らずにストリームが閉じた場合は成功扱いにしない
			if (!completed) {
				throw new InvalidOperationException("完了通知を受信できませんでした。結果が不明のため、台帳・残高を確認してください。");
			}
			ProgressValue = 100;
			StatusMessage = $"{title}が完了しました。{targetText}";
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{title}をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{title}をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"{title}でエラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// yyyy/MM または yyyyMM を検証し yyyyMM へ正規化する
	/// </summary>
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
}
