using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvWpfclient.Helpers;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSchedulerJobMenteViewModel : Helpers.BaseViewModel {
	private readonly GrpcChannel _schedulerChannel;
	private readonly ISchedulerService _schedulerClient;

	[ObservableProperty]
	public partial string Title { get; set; } = "自動実行ジョブ管理";

	[ObservableProperty]
	public partial ObservableCollection<SchedulerTaskInfo> Tasks { get; set; } = new();

	[ObservableProperty]
	public partial SchedulerTaskInfo? SelectedTask { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	public SysSchedulerJobMenteViewModel() {
		_schedulerChannel = CreateSchedulerChannel();
		_schedulerClient = _schedulerChannel.CreateGrpcService<ISchedulerService>();
	}

	protected override void OnExit() {
		_schedulerChannel.Dispose();
		base.OnExit();
	}

	private static GrpcChannel CreateSchedulerChannel() {
		var socketsHandler = new SocketsHttpHandler {
			PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
			KeepAlivePingDelay = TimeSpan.FromSeconds(60),
			KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
			EnableMultipleHttp2Connections = true,
			KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
		};

		HttpMessageHandler handler = socketsHandler;
		var subPath = Common.ExtractSubPath(AppGlobal.Url);
		if (!string.IsNullOrEmpty(subPath)) {
			handler = new GrpcSubPathHandler(subPath) {
				InnerHandler = handler,
			};
		}

		var httpClient = new HttpClient(handler) {
			Timeout = Timeout.InfiniteTimeSpan,
		};
		return GrpcChannel.ForAddress(AppGlobal.Url, new GrpcChannelOptions {
			HttpClient = httpClient,
		});
	}

	[RelayCommand]
	public async Task Init() {
		await LoadTasksAsync(CancellationToken.None);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task LoadTasksAsync(CancellationToken ct) {
		IsBusy = true;
		Message = "一覧を取得中...";
		try {
			var response = await _schedulerClient.GetTasksAsync(AppGlobal.GetDefaultCallContext(ct));
			if (response.Result != 0) {
				Message = $"取得エラー: {response.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Tasks = new ObservableCollection<SchedulerTaskInfo>(response.Tasks);
			Message = $"ジョブ一覧を取得しました (件数: {Tasks.Count})";
		}
		catch (Exception ex) {
			Message = $"取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task EditCronAsync() {
		if (SelectedTask == null) {
			MessageEx.ShowWarningDialog("編集するジョブを選択してください", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}

		var dialog = new Views._00System.SysSchedulerCronEditView();
		if (dialog.DataContext is not SysSchedulerCronEditViewModel vm) return;

		vm.TaskName = SelectedTask.TaskName;
		vm.CronExpression = SelectedTask.CronExpression;

		if (Helpers.ClientLib.ShowDialogView(dialog, this, true) != true) return;

		var request = new UpdateSchedulerTaskRequest {
			TaskId = SelectedTask.TaskId,
			CronExpression = vm.CronExpression,
		};

		IsBusy = true;
		Message = "スケジュールを更新中...";
		try {
			var result = await _schedulerClient.UpdateTaskAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				Message = $"更新エラー: {result.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Message = $"スケジュールを更新しました: {SelectedTask.TaskName}";
			await LoadTasksAsync(CancellationToken.None);
		}
		catch (Exception ex) {
			Message = $"更新失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task DeleteTaskAsync() {
		if (SelectedTask == null) {
			MessageEx.ShowWarningDialog("削除するジョブを選択してください", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}

		if (SelectedTask.IsSystemTask) {
			var confirm = MessageEx.ShowQuestionDialog(
				$"システムジョブ '{SelectedTask.TaskName}' を削除します。\nこれはサーバーの正常動作に影響を与える可能性があります。\n本当に削除しますか？",
				owner: Helpers.ClientLib.GetActiveView(this));
			if (confirm != MessageBoxResult.Yes) return;
		}
		else {
			var confirm = MessageEx.ShowQuestionDialog(
				$"'{SelectedTask.TaskName}' を削除しますか？",
				owner: Helpers.ClientLib.GetActiveView(this));
			if (confirm != MessageBoxResult.Yes) return;
		}

		var request = new RemoveSchedulerTaskRequest {
			TaskId = SelectedTask.TaskId,
		};

		IsBusy = true;
		Message = "ジョブを削除中...";
		try {
			var result = await _schedulerClient.RemoveTaskAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				Message = $"削除エラー: {result.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Message = $"ジョブを削除しました: {SelectedTask.TaskName}";
			await LoadTasksAsync(CancellationToken.None);
		}
		catch (Exception ex) {
			Message = $"削除失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>
	/// 選択したジョブの実行する/しないフラグを切り替える。
	/// 実行フラグの変更は必ず確認メッセージを出してからサーバーへ反映する。
	/// </summary>
	[RelayCommand]
	private async Task ToggleTaskEnabledAsync() {
		if (SelectedTask == null) {
			MessageEx.ShowWarningDialog("実行する/しないを切り替えるジョブを選択してください", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}

		var next = !SelectedTask.IsEnabled;

		string confirmMessage;
		if (next) {
			confirmMessage = $"'{SelectedTask.TaskName}' を【実行する】に変更します。\n設定した起動時間で自動実行されるようになります。\n変更しますか？";
			if (SelectedTask.CheckMinInterval) {
				confirmMessage += $"\nこのジョブは負荷の高い処理です。起動間隔は{SelectedTask.MinIntervalMinutes}分以上必要です。";
			}
		}
		else {
			confirmMessage = $"'{SelectedTask.TaskName}' を【実行しない】に変更します。\nスケジュール時刻になっても実行されなくなります。\n変更しますか？";
		}

		var confirm = MessageEx.ShowQuestionDialog(confirmMessage, owner: Helpers.ClientLib.GetActiveView(this));
		if (confirm != MessageBoxResult.Yes) return;

		var request = new SetSchedulerTaskEnabledRequest {
			TaskId = SelectedTask.TaskId,
			IsEnabled = next,
		};

		IsBusy = true;
		Message = "実行フラグを変更中...";
		try {
			var result = await _schedulerClient.SetTaskEnabledAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				Message = $"実行フラグ変更エラー: {result.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Message = $"実行フラグを変更しました: {SelectedTask.TaskName} → {(next ? "実行する" : "実行しない")}";
			await LoadTasksAsync(CancellationToken.None);
		}
		catch (Exception ex) {
			Message = $"変更失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task AddTaskAsync() {
		var dialog = new Views._00System.SysSchedulerCronEditView();
		if (dialog.DataContext is not SysSchedulerCronEditViewModel vm) return;

		vm.TaskName = "新規ジョブ";
		vm.CronExpression = "0 2 * * *";

		if (Helpers.ClientLib.ShowDialogView(dialog, this, true) != true) return;

		var request = new AddSchedulerTaskRequest {
			TaskName = vm.TaskName,
			CronExpression = vm.CronExpression,
			TaskType = SchedulerTaskType.LogOnly,
			Payload = string.Empty,
		};

		IsBusy = true;
		Message = "ジョブを登録中...";
		try {
			var result = await _schedulerClient.AddTaskAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				Message = $"登録エラー: {result.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Message = $"ジョブを登録しました: {result.TaskId}";
			await LoadTasksAsync(CancellationToken.None);
		}
		catch (Exception ex) {
			Message = $"登録失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private void OpenHistory() {
		var view = new Views._00System.SysAutoExecHistoryView();
		view.Show();
	}
}
