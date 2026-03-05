using CodeShare;
using NCrontab;
using NCrontab.Scheduler;
using ProtoBuf.Grpc;


namespace Cvnet10Server.Services;


public class SchedulerService : ICvnetScheduler {
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
	public Task<SchedulerResult> AddOneTaskAsync(SchedulerMessage msg, CallContext context = default) {
		var guid = _scheduler.AddTask(
		  crontabSchedule: CrontabSchedule.Parse(msg.CronExpression),
		  action: ct => { _logger.LogError($"{DateTime.Now:O} -> Task runs !!!!"); });
		return Task.FromResult(new SchedulerResult { Result = 0, Detail = $"正常終了 guid={guid}" });
	}
	/// <summary>
	/// 追加されたタスクを削除する
	/// [Remove the added task]
	/// </summary>
	/// <param name="msg"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	public Task<SchedulerResult> RemoveOneTaskAsync(SchedulerMessage msg, CallContext context = default) {
		var retBool = false;
		if (Guid.TryParse(msg.CronExpression, out var guid)) {
			retBool = _scheduler.RemoveTask(guid);
		}
		return Task.FromResult(new SchedulerResult { Result = 0, Detail = $"正常終了 guid={guid}" });
	}
	/// <summary>
	/// すべてのタスクを削除する
	/// [Remove all tasks]
	/// </summary>
	/// <param name="context"></param>
	/// <returns></returns>
	public Task<SchedulerResult> RemoveAllTaskAsync(ProtoBuf.Grpc.CallContext context = default) {
		_scheduler.RemoveAllTasks();
		return Task.FromResult(new SchedulerResult { Result = 0 });
	}
}
