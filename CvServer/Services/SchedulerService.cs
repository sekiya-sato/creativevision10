using CodeShare;
using CvBase;
using CvDomainLogic;
using NCrontab;
using NCrontab.Scheduler;
using ProtoBuf.Grpc;


namespace CvServer.Services;


public class SchedulerService : CodeShare.IScheduler {
	private const int Success = 0;
	private const int InvalidRequest = 1;
	private const int InvalidCronExpression = 2;
	private const int InvalidTaskId = 3;
	private const int TaskNotFound = 4;
	private const int InternalError = 9;

	private readonly ILogger<SchedulerService> _logger;
	private readonly NCrontab.Scheduler.IScheduler _scheduler;
	private readonly ExDatabase _db;

	public SchedulerService(ILogger<SchedulerService> logger, NCrontab.Scheduler.IScheduler scheduler, ExDatabase db) {
		_logger = logger;
		_scheduler = scheduler;
		_db = db;
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
				action: ct => ExecuteTaskAsync(request, ct).GetAwaiter().GetResult());

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

	private async Task ExecuteTaskAsync(AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		switch (request.TaskType) {
			case SchedulerTaskType.LogOnly:
				_logger.LogInformation(
					"スケジュール実行: TaskType={TaskType}, TaskName={TaskName}, Payload={Payload}, Canceled={Canceled}",
					request.TaskType,
					request.TaskName,
					request.Payload,
					cancellationToken.IsCancellationRequested);
				break;

			case SchedulerTaskType.RunSummary:
				try {
					string yyyymm = string.IsNullOrWhiteSpace(request.Payload)
						? DateTime.Now.ToString("yyyyMM")
						: request.Payload.Trim();

					_logger.LogInformation(
						"集計開始: TaskName={TaskName}, yyyymm={yyyymm}, Canceled={Canceled}",
						request.TaskName,
						yyyymm,
						cancellationToken.IsCancellationRequested);

					var summaryDb = new SummaryDb(_db);
					var param = new SummaryDateParameter(yyyymm, yyyymm);
					await foreach (var step in summaryDb.SummaryAllAsyncStream(param).WithCancellation(cancellationToken)) {
						if (step.IsCompleted) {
							_logger.LogInformation("集計完了: TaskName={TaskName}, Duration={Duration}",
								request.TaskName, step.ErrorMessage);
						}
						else if (step.IsError) {
							_logger.LogError("集計エラー: Step={Step}, Error={Error}",
								step.StepName, step.ErrorMessage);
						}
						else {
							_logger.LogInformation("集計進捗: Step={Step}, Progress={Progress}, Count={Count}",
								step.StepName, step.Progress, step.Count);
						}
					}
				}
				catch (Exception ex) {
					_logger.LogError(ex, "集計実行中にエラーが発生しました: TaskName={TaskName}", request.TaskName);
				}
				break;

			default:
				_logger.LogWarning("未対応の TaskType です: {TaskType}", request.TaskType);
				break;
		}
	}
}
