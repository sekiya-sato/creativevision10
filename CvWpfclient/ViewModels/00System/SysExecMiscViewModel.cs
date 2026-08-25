using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysExecMiscViewModel : BaseViewModel {
	[ObservableProperty]
	public partial string ResultMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task Test01(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("環境変数取得？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg003_GetEnv };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply?.DataMsg != null && reply?.DataType != null) {
				ResultMessage = reply.DataMsg;
			}
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task MasterVColumnResyncAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("マスタ名称の複製列(V*列)を現在のマスタ内容で再同期しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			// 数分かかることがあるため、開始時刻を先に見せておく
			ResultMessage = $"V*列の再同期を実行中です。{Environment.NewLine}開始 {DateTime.Now:yyyy/MM/dd HH:mm:ss}";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg047_MasterVColumnResync,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"V*列の再同期に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = $"V*列の再同期が完了しました。{Environment.NewLine}{reply.DataMsg}";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"V*列の再同期中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 明細Taxが未設定のTran系伝票へ明細別消費税を投入する（移行・既存データ救済用の一時処理）。
	/// 明細Tax合計が0の伝票だけが対象なので、二重実行しても結果は変わらない。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task TranTaxRebuildAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog(
				"明細の消費税が未設定の伝票（売上/店舗売上/仕入/受注/発注）へ、商品ごとの消費税区分で明細税額を投入します。"
				+ $"{Environment.NewLine}ヘッダの消費税・総合計も明細合計で再計算されます。実行しますか？",
				owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			// 全伝票の走査になり数分かかることがあるため、開始時刻を先に見せておく
			ResultMessage = $"伝票税額再更新を実行中です。{Environment.NewLine}開始 {DateTime.Now:yyyy/MM/dd HH:mm:ss}";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg059_TranTaxRebuild,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"伝票税額再更新に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = $"伝票税額再更新が完了しました。{Environment.NewLine}{reply.DataMsg}";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"伝票税額再更新中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task MasterShohinMeishoRebuildAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("MasterShohinのId_Col/Id_Sizが0のデータから名称マスタを再構築しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ResultMessage = "商品名称マスタ再構築を実行中です。";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg046_MasterShohinMeishoRebuild,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"商品名称マスタ再構築に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = "商品名称マスタ再構築が完了しました。";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"商品名称マスタ再構築中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}
}
