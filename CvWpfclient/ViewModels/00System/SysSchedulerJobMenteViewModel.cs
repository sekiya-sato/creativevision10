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
		await ReloadAsync(CancellationToken.None);
	}

	/// <summary>一覧を再取得する。F5 / 一覧更新ボタン用。現在の選択行を維持する。</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private Task LoadTasksAsync(CancellationToken ct)
		=> ReloadAsync(ct, SelectedTask?.TaskId);

	/// <summary>
	/// 一覧を再取得し、選択行を復元する。
	/// selectTaskId が見つからない場合は fallbackIndex の位置、それも無ければ先頭行を選択する。
	/// </summary>
	private async Task ReloadAsync(
		CancellationToken ct,
		string? selectTaskId = null,
		int? fallbackIndex = null,
		bool keepMessage = false) {
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
			SelectedTask = ResolveSelection(selectTaskId, fallbackIndex);
			if (!keepMessage) {
				Message = $"ジョブ一覧を取得しました (件数: {Tasks.Count})";
			}
		}
		catch (Exception ex) {
			Message = $"取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>再取得後に選択すべき行を決める。</summary>
	private SchedulerTaskInfo? ResolveSelection(string? taskId, int? fallbackIndex) {
		if (!string.IsNullOrEmpty(taskId)) {
			var match = Tasks.FirstOrDefault(x => x.TaskId == taskId);
			if (match != null) return match;
		}
		if (fallbackIndex is int idx && Tasks.Count > 0) {
			return Tasks[Math.Clamp(idx, 0, Tasks.Count - 1)];
		}
		return Tasks.FirstOrDefault();
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

		var taskId = SelectedTask.TaskId;
		var taskName = SelectedTask.TaskName;

		var request = new UpdateSchedulerTaskRequest {
			TaskId = taskId,
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
			Message = $"スケジュールを更新しました: {taskName}";
			await ReloadAsync(CancellationToken.None, taskId, keepMessage: true);
		}
		catch (Exception ex) {
			Message = $"更新失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[Obsolete]
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

		var taskName = SelectedTask.TaskName;
		var index = Tasks.IndexOf(SelectedTask);

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
			Message = $"ジョブを削除しました: {taskName}";
			await ReloadAsync(CancellationToken.None, null, fallbackIndex: index, keepMessage: true);
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

		var taskId = SelectedTask.TaskId;
		var taskName = SelectedTask.TaskName;

		var request = new SetSchedulerTaskEnabledRequest {
			TaskId = taskId,
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
			Message = $"実行フラグを変更しました: {taskName} → {(next ? "実行する" : "実行しない")}";
			await ReloadAsync(CancellationToken.None, taskId, keepMessage: true);
		}
		catch (Exception ex) {
			Message = $"変更失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>
	/// 選択したジョブのメール送信する/しないフラグを切り替える。
	/// メール送信フラグの変更は必ず確認メッセージを出してからサーバーへ反映する。
	/// </summary>
	[RelayCommand]
	private async Task ToggleTaskSendMailAsync() {
		if (SelectedTask == null) {
			MessageEx.ShowWarningDialog("メール送信する/しないを切り替えるジョブを選択してください", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}

		var next = !SelectedTask.IsSendMail;
		var confirmMessage = $"'{SelectedTask.TaskName}' を【メール送信{(next ? "する" : "しない")}】に変更します。\n変更しますか？";
		var confirm = MessageEx.ShowQuestionDialog(confirmMessage, owner: Helpers.ClientLib.GetActiveView(this));
		if (confirm != MessageBoxResult.Yes) return;

		var taskId = SelectedTask.TaskId;
		var taskName = SelectedTask.TaskName;

		var request = new SetSchedulerTaskSendMailRequest {
			TaskId = taskId,
			IsSendMail = next,
		};

		IsBusy = true;
		Message = "メール送信フラグを変更中...";
		try {
			var result = await _schedulerClient.SetTaskSendMailAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				Message = $"メール送信フラグ変更エラー: {result.Detail}";
				MessageEx.ShowErrorDialog(Message, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			Message = $"メール送信フラグを変更しました: {taskName} → メール送信{(next ? "する" : "しない")}";
			await ReloadAsync(CancellationToken.None, taskId, keepMessage: true);
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
			await ReloadAsync(CancellationToken.None, result.TaskId, keepMessage: true);
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
