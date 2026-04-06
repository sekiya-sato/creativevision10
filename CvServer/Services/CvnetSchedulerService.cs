using CodeShare;
using NCrontab;
using NCrontab.Scheduler;
using ProtoBuf.Grpc;


namespace CvServer.Services;


public class SchedulerService : ICvnetScheduler {
	private const int Success = 0;
	private const int InvalidRequest = 1;
	private const int InvalidCronExpression = 2;
	private const int InvalidTaskId = 3;
	private const int TaskNotFound = 4;
	private const int InternalError = 9;

	private readonly ILogger<SchedulerService> _logger;
	private readonly IScheduler _scheduler;

	public SchedulerService(ILogger<SchedulerService> logger, IScheduler scheduler) {
		_logger = logger;
		_scheduler = scheduler;
	}
	/// <summary>
	/// 追加されたタスクを追加する
	/// </summary>
	/// <param name="msg"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	public Task<SchedulerResult> AddOneTaskAsync(AddSchedulerTaskRequest request, CallContext context = default) {
		if (string.IsNullOrWhiteSpace(request.CronExpression)) {
			return Task.FromResult(new SchedulerResult { Result = InvalidRequest, Detail = "CronExpression が空です。" });
		}

		if (request.TaskType == SchedulerTaskType.Unknown) {
			return Task.FromResult(new SchedulerResult { Result = InvalidRequest, Detail = "TaskType が未指定です。" });
		}

		CrontabSchedule schedule;
		try {
			schedule = CrontabSchedule.Parse(request.CronExpression);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "cron式が不正です。 Cron={CronExpression}", request.CronExpression);
			return Task.FromResult(new SchedulerResult {
				Result = InvalidCronExpression,
				Detail = $"cron式が不正です: {request.CronExpression}",
			});
		}

		try {
			var guid = _scheduler.AddTask(
				crontabSchedule: schedule,
				action: ct => ExecuteTask(request, ct));

			_logger.LogInformation(
				"スケジュール登録: TaskId={TaskId}, TaskType={TaskType}, TaskName={TaskName}, Cron={Cron}",
				guid,
				request.TaskType,
				request.TaskName,
				request.CronExpression);

			return Task.FromResult(new SchedulerResult {
				Result = Success,
				Detail = "正常終了",
				TaskId = guid.ToString(),
			});
		}
		catch (Exception ex) {
			_logger.LogError(ex, "スケジュール登録に失敗しました。 TaskType={TaskType}, TaskName={TaskName}", request.TaskType, request.TaskName);
			return Task.FromResult(new SchedulerResult {
				Result = InternalError,
				Detail = "スケジュール登録に失敗しました。",
			});
		}
	}
	/// <summary>
	/// 追加されたタスクを削除する
	/// [Remove the added task]
	/// </summary>
	/// <param name="msg"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	public Task<SchedulerResult> RemoveOneTaskAsync(RemoveSchedulerTaskRequest request, CallContext context = default) {
		if (!Guid.TryParse(request.TaskId, out var guid)) {
			return Task.FromResult(new SchedulerResult {
				Result = InvalidTaskId,
				Detail = $"TaskId が不正です: {request.TaskId}",
				TaskId = request.TaskId,
			});
		}

		var removed = _scheduler.RemoveTask(guid);
		if (!removed) {
			return Task.FromResult(new SchedulerResult {
				Result = TaskNotFound,
				Detail = $"対象タスクが存在しません: {request.TaskId}",
				TaskId = request.TaskId,
			});
		}

		_logger.LogInformation("スケジュール削除: TaskId={TaskId}", guid);
		return Task.FromResult(new SchedulerResult {
			Result = Success,
			Detail = "正常終了",
			TaskId = guid.ToString(),
		});
	}
	/// <summary>
	/// すべてのタスクを削除する
	/// [Remove all tasks]
	/// </summary>
	/// <param name="context"></param>
	/// <returns></returns>
	public Task<SchedulerResult> RemoveAllTaskAsync(ProtoBuf.Grpc.CallContext context = default) {
		_scheduler.RemoveAllTasks();
		_logger.LogInformation("スケジュール全削除を実行しました。");
		return Task.FromResult(new SchedulerResult { Result = Success, Detail = "正常終了" });
	}

	private void ExecuteTask(AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		switch (request.TaskType) {
			case SchedulerTaskType.LogOnly:
				_logger.LogInformation(
					"スケジュール実行: TaskType={TaskType}, TaskName={TaskName}, Payload={Payload}, Canceled={Canceled}",
					request.TaskType,
					request.TaskName,
					request.Payload,
					cancellationToken.IsCancellationRequested);
				break;
			default:
				_logger.LogWarning("未対応の TaskType です: {TaskType}", request.TaskType);
				break;
		}
	}
}
