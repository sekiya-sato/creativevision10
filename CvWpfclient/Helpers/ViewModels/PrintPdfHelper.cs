/*
# description
PrintPdfHelper は「サーバへ PrintOperation を投げて PDF を生成し、WebPdfView で表示する」までの
共通パイプラインを提供する静的ヘルパーです。

以前は同じ約100行が BaseMenteViewModel / ShopBudgetReportViewModel / ShiireSlipPrintViewModel の
3箇所に重複していました。帳票型画面が今後多数増えるため、実装をここ1箇所へ集約し、
各基底クラス(BaseMenteViewModel / BaseReportViewModel)は自身の Message・ActiveWindow を渡して
委譲するだけにしています（基底クラスへプロパティを引き上げると、独自に Message を持つ既存
ViewModel 群と名前衝突(CS0108/CS0114)するため、あえて引き上げていません）。

# example
await PrintPdfHelper.RunPrintPdfAsync(this, ActiveWindow, m => Message = m, formFile, null, sqlParam, ct);
 */
using CodeShare;
using CvAsset;
using CvBase;
using CvWpfclient.ViewModels.Sub;
using Grpc.Core;
using System.Windows;

namespace CvWpfclient.Helpers;

internal static class PrintPdfHelper {
	/// <summary>
	/// 指定したフォームファイルと印刷データ(CSV または SQL)で PDF を生成し、PDF表示画面を開く。
	/// </summary>
	/// <param name="viewModel">呼び出し元 ViewModel（ClientLib の Window 解決に使用）</param>
	/// <param name="ownerWindow">メッセージダイアログの Owner</param>
	/// <param name="setMessage">進捗・結果メッセージの反映先</param>
	internal static async Task RunPrintPdfAsync(
		object viewModel,
		Window? ownerWindow,
		Action<string> setMessage,
		string? formFile,
		PrintByCsvParam? csvParam,
		QueryListSqlParam? sqlParam,
		CancellationToken ct) {

		ct.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(formFile)) {
			Warn(setMessage, ownerWindow, "印刷フォームファイルが設定されていません");
			return;
		}

		if (csvParam is null && sqlParam is null) {
			Warn(setMessage, ownerWindow, "印刷データが設定されていません");
			return;
		}

		if (csvParam is not null && sqlParam is not null) {
			Warn(setMessage, ownerWindow, "印刷データは CSV と SQL のどちらか一方だけ設定してください");
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			var param = (object?)csvParam ?? sqlParam!;
			var dataType = csvParam is not null ? typeof(PrintByCsvParam) : typeof(QueryListSqlParam);
			var msg = new PrintOperation {
				DataType = dataType,
				DataMsg = Common.SerializeObject(param),
				FormFile = formFile,
			};

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			string? pdfdata = null;
			await foreach (var streamMsg in coreService.PrintPdfAsync(msg, AppGlobal.GetDefaultCallContext(ct))) {
				ct.ThrowIfCancellationRequested();
				setMessage(string.Join(" ", new[] { streamMsg.StatusString, streamMsg.DataMsg }.Where(s => !string.IsNullOrWhiteSpace(s))));
				if (streamMsg.Status == -2) {
					Warn(setMessage, ownerWindow, streamMsg.DataMsg);
					return;
				}
				if (streamMsg.Status < 0) {
					var errorDetail = string.IsNullOrWhiteSpace(streamMsg.DataMsg) ? streamMsg.StatusString : streamMsg.DataMsg;
					Error(setMessage, ownerWindow, $"PDF出力失敗: {errorDetail}");
					return;
				}

				if (streamMsg.IsCompleted) {
					pdfdata = streamMsg.DataMsg;
					break;
				}
			}

			if (string.IsNullOrWhiteSpace(pdfdata)) {
				Warn(setMessage, ownerWindow, "PDF出力結果が取得できませんでした");
				return;
			}

			var viewTitle = string.IsNullOrWhiteSpace(ownerWindow?.Title)
				? "PDF表示"
				: $"{ownerWindow.Title} - PDF表示";
			var view = new Views.Sub.WebPdfView { Title = viewTitle };
			if (view.DataContext is not WebPdfViewModel vm) {
				Error(setMessage, ownerWindow, "PDF表示画面の初期化に失敗しました");
				return;
			}

			vm.Pdfdata = $"{AppGlobal.Url}/wrk/{pdfdata}";
			view.Title += " " + vm.Pdfdata;
			ClientLib.ShowDialogView(view, viewModel, IsDialog: false);
			view.Owner = null;
			setMessage($"PDFを表示しました: {pdfdata}");
		}
		catch (OperationCanceledException cancel) {
			setMessage($"Cancelエラー：{cancel.Message}");
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			setMessage("PDF出力をキャンセルしました");
			return;
		}
		catch (Exception ex) {
			Error(setMessage, ownerWindow, $"PDF出力失敗: {ex.Message}");
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>SelectWinView で単一レコードを選択させる。キャンセル時は null。</summary>
	internal static TResult? ShowSelectDialog<TResult>(object viewModel, Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, viewModel) != true) return null;
		return vm.Current as TResult;
	}

	/// <summary>SelectMultiWinView で複数レコードを選択させる。キャンセル時は null。</summary>
	internal static IReadOnlyList<TResult>? ShowMultiSelectDialog<TResult>(object viewModel, Type tableType, string where, string order, IEnumerable<long>? selectedIds = null, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos, selectedIds: selectedIds);
		if (ClientLib.ShowDialogView(selWin, viewModel) != true) return null;
		return vm.GetSelectedItems<TResult>();
	}

	static void Warn(Action<string> setMessage, Window? owner, string message) {
		setMessage(message);
		MessageEx.ShowWarningDialog(message, owner: owner);
	}

	static void Error(Action<string> setMessage, Window? owner, string message) {
		setMessage(message);
		MessageEx.ShowErrorDialog(message, owner: owner);
	}
}
