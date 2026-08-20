using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 請求計算・支払計算の共通ViewModel。
/// </summary>
public abstract partial class BaseBillingCalculationViewModel : BaseViewModel {
	public sealed record ShimeOption(int Value, string Name);

	protected abstract CvFlag TargetFlag { get; }
	protected abstract string ActionName { get; }
	protected abstract string TorihikiName { get; }
	protected abstract string MasterTableName { get; }
	protected virtual bool SupportsReissue => false;
	protected virtual string InitialMessage => "請求月・締日・コード範囲を指定して実行してください。";
	/// <summary>親（請求先／支払先）と子（得意先／仕入先）の締日不一致検査（E7）を行うか。既定は行う。</summary>
	protected virtual bool ChecksPaysakiClosing => true;

	[ObservableProperty]
	public partial string BillingMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	[ObservableProperty]
	public partial ObservableCollection<ShimeOption> ShimeItems { get; set; } = [];
	[ObservableProperty]
	public partial int SelectedShime { get; set; }
	[ObservableProperty]
	public partial string TorihikiCodeFrom { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string TorihikiCodeTo { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string WarningMessage { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "初期化中です...";
	[ObservableProperty]
	public partial bool IsProcessing { get; set; }
	[ObservableProperty]
	public partial int ProgressValue { get; set; }
	[ObservableProperty]
	public partial bool IsReissue { get; set; }

	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		try {
			var rows = await QuerySqlListAsync<SummaryClosingCheckRow>(
				$"SELECT DISTINCT Shime1 FROM {MasterTableName} WHERE Shime1 BETWEEN 1 AND 31 OR Shime1 = 99 ORDER BY Shime1",
				[], cancellationToken);
			ShimeItems = new ObservableCollection<ShimeOption>(rows
				.Select(x => x.Shime1)
				.Select(x => new ShimeOption(x, x == 99 ? "末日" : $"{x:00}日")));
			SelectedShime = ShimeItems.FirstOrDefault()?.Value ?? 0;
			StatusMessage = ShimeItems.Count == 0
				? $"{TorihikiName}マスタに有効な締日がありません。"
				: InitialMessage;
		}
		catch (OperationCanceledException) {
			StatusMessage = "初期化をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"締日取得に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(BillingMonth, out var yyyymm)) {
			ShowWarning($"{ActionName}月の形式が不正です: {BillingMonth}");
			return;
		}
		if (SelectedShime is < 1 or > 31 && SelectedShime != 99) {
			ShowWarning("締日を選択してください。");
			return;
		}
		var codeFrom = TorihikiCodeFrom.Trim();
		var codeTo = TorihikiCodeTo.Trim();
		if (codeFrom.Length > 0 && codeTo.Length > 0 && string.CompareOrdinal(codeFrom, codeTo) > 0) {
			ShowWarning($"{TorihikiName}コード範囲の開始と終了が逆です。");
			return;
		}

		WarningMessage = await GetPreExecuteWarningAsync(codeFrom, codeTo, cancellationToken);
		if (!string.IsNullOrEmpty(WarningMessage)) {
			MessageEx.ShowWarningDialog(WarningMessage, owner: ClientLib.GetActiveView(this));
		}
		var target = $"{yyyymm} / {(SelectedShime == 99 ? "末日" : $"{SelectedShime:00}日")} / {FormatRange(codeFrom, codeTo)}";
		if (MessageEx.ShowQuestionDialog($"{target} の{ActionName}を実行しますか？",
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = $"{ActionName}を開始します...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = TargetFlag,
				DataType = typeof(BillingParameter),
				DataMsg = Common.SerializeObject(new BillingParameter(yyyymm, SelectedShime, codeFrom, codeTo, SupportsReissue && IsReissue)),
			};
			var stepMessage = string.Empty;
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StatusMessage = streamMsg.DataMsg;
					if (!streamMsg.IsCompleted) stepMessage = streamMsg.DataMsg;
				}
				ProgressValue = Math.Clamp(streamMsg.Progress, 0, 100);
				if (streamMsg.IsError) throw new InvalidOperationException(streamMsg.DataMsg);
				if (streamMsg.IsCompleted) break;
			}
			ProgressValue = 100;
			StatusMessage = $"{ActionName}が完了しました。{target}\n{stepMessage}";
			MessageEx.ShowInformationDialog($"{ActionName}が完了しました。\n{target}\n{ExtractCount(stepMessage)} 件を処理しました。",
				owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{ActionName}をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{ActionName}をキャンセルしました。";
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

	/// <summary>
	/// 実行前の警告文を作成する。既定は親子締日不一致（E7）検査。ブロックはしない。
	/// </summary>
	protected virtual async Task<string> GetPreExecuteWarningAsync(string codeFrom, string codeTo, CancellationToken cancellationToken) {
		if (!ChecksPaysakiClosing) return string.Empty;
		List<string> parameters = [];
		var where = "WHERE c.Id_Paysaki <> 0 AND c.Shime1 = @0 AND p.Shime1 <> c.Shime1";
		parameters.Add(SelectedShime.ToString(CultureInfo.InvariantCulture));
		if (codeFrom.Length > 0) {
			where += $" AND c.Code >= @{parameters.Count}";
			parameters.Add(codeFrom);
		}
		if (codeTo.Length > 0) {
			where += $" AND c.Code <= @{parameters.Count}";
			parameters.Add(codeTo);
		}
		var sql = PaysakiClosingCheck.BuildRangeCheckSql(MasterTableName, where);
		var rows = await QuerySqlListAsync<PaysakiClosingCheckRow>(sql, parameters, cancellationToken);
		var mismatches = PaysakiClosingCheck.FindMismatches(rows);
		return PaysakiClosingCheck.BuildMismatchWarning(PaysakiParentLabel, TorihikiName, mismatches);
	}

	/// <summary>親（請求先／支払先）の呼称。警告文の主語に使う。</summary>
	protected abstract string PaysakiParentLabel { get; }

	protected async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list
			? list.Cast<T>().ToList()
			: [];
	}

	private static bool TryParseYearMonth(string input, out string yyyymm) {
		yyyymm = string.Empty;
		var normalized = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal);
		if (normalized.Length != 6 || !DateTime.TryParseExact(normalized + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymm = normalized;
		return true;
	}

	private static string FormatRange(string codeFrom, string codeTo) => (codeFrom, codeTo) switch {
		("", "") => "全件",
		(_, "") => $"{codeFrom} 以降",
		("", _) => $"{codeTo} 以前",
		_ => $"{codeFrom} ～ {codeTo}",
	};

	private static string ExtractCount(string message) {
		const string marker = "件数=";
		var pos = message.IndexOf(marker, StringComparison.Ordinal);
		if (pos < 0) return "0";
		var digits = new string([.. message[(pos + marker.Length)..].TakeWhile(char.IsDigit)]);
		return digits.Length == 0 ? "0" : int.Parse(digits, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture);
	}

	private void ShowWarning(string message) {
		StatusMessage = message;
		MessageEx.ShowWarningDialog(message, owner: ClientLib.GetActiveView(this));
	}
}
