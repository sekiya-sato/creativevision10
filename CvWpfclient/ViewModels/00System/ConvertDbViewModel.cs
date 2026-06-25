using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class ConvertDbViewModel : BaseViewModel {
	[ObservableProperty]
	private bool isInitDb;

	[ObservableProperty]
	private bool isRunning;

	[ObservableProperty]
	private int progressValue;

	[ObservableProperty]
	private ObservableCollection<string> streamMessages = [];

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("データベースの変換を開始しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		if (IsRunning) {
			return;
		}
		try {
			IsRunning = true;
			ProgressValue = 0;
			StreamMessages.Clear();
			cancellationToken.ThrowIfCancellationRequested();

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = IsInitDb ? CvFlag.Msg041_ConvertDbInit : CvFlag.Msg040_ConvertDb,
				DataType = typeof(string),
				DataMsg = string.Empty
			};

			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StreamMessages.Insert(0, streamMsg.DataMsg);
				}
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) {
					break;
				}
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		finally {
			IsRunning = false;
		}
	}
}
