using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSchedulerJobMenteViewModel : Helpers.BaseViewModel {
	private readonly IScheduler _schedulerClient;

	[ObservableProperty]
	string title = "自動実行ジョブ管理";

	[ObservableProperty]
	ObservableCollection<SchedulerTaskInfo> tasks = new();

	[ObservableProperty]
	SchedulerTaskInfo? selectedTask;

	[ObservableProperty]
	string message = string.Empty;

	[ObservableProperty]
	bool isBusy;

	public SysSchedulerJobMenteViewModel() {
		_schedulerClient = AppGlobal.GetGrpcService<IScheduler>();
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
			var response = await _schedulerClient.GetAllTasksAsync(AppGlobal.GetDefaultCallContext(ct));
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
			var result = await _schedulerClient.RemoveOneTaskAsync(request, AppGlobal.GetDefaultCallContext());
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
			var result = await _schedulerClient.AddOneTaskAsync(request, AppGlobal.GetDefaultCallContext());
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
