using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysExecMiscViewModel : BaseViewModel {
	[ObservableProperty]
	private string resultMessage = string.Empty;

	[ObservableProperty]
	private bool isProcessing;

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
	public async Task Test02(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("Testケース02？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg702_TestCase002 };
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
}
