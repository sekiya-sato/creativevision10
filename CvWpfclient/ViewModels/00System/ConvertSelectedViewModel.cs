using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class ConvertSelectedViewModel : BaseViewModel {
	[ObservableProperty]
	public partial bool IsInitDb { get; set; }

	[ObservableProperty]
	public partial bool IsRunning { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ConvertTaskItem> Tasks { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<string> StreamMessages { get; set; } = [];

	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		try {
			Tasks.Clear();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg043_ConvertList,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code != 0) {
				MessageEx.ShowErrorDialog($"変換プログラム一覧の取得に失敗しました。\n{reply.Option}", owner: ClientLib.GetActiveView(this));
				return;
			}
			var taskNames = Common.DeserializeObject<List<string>>(reply.DataMsg) ?? [];
			foreach (var name in taskNames) {
				Tasks.Add(new ConvertTaskItem(name));
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"変換プログラム一覧の取得中にエラーが発生しました。\n{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	private void SelectAll() {
		foreach (var task in Tasks) {
			task.IsSelected = true;
		}
	}

	[RelayCommand]
	private void ClearSelection() {
		foreach (var task in Tasks) {
			task.IsSelected = false;
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		var selectedTasks = Tasks.Where(t => t.IsSelected).Select(t => t.Name).ToList();
		if (selectedTasks.Count == 0) {
			MessageEx.ShowWarningDialog("変換プログラムを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog("選択した変換プログラムを実行しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
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
				Flag = IsInitDb ? CvFlag.Msg045_ConvertSelectedInit : CvFlag.Msg044_ConvertSelected,
				DataType = typeof(List<string>),
				DataMsg = Common.SerializeObject(selectedTasks)
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

public partial class ConvertTaskItem : ObservableObject {
	[ObservableProperty]
	public partial bool IsSelected { get; set; }

	public string Name { get; }

	public ConvertTaskItem(string name) {
		Name = name;
	}
}
