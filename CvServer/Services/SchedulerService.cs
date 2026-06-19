using CodeShare;
using CvBase;
using CvDomainLogic;
using NCrontab;
using NCrontab.Scheduler;
using ProtoBuf.Grpc;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace CvServer.Services;

public class SchedulerService : ISchedulerService {
	private const int Success = 0;
	private const int InvalidRequest = 1;
	private const int InvalidCronExpression = 2;
	private const int InvalidTaskId = 3;
	private const int TaskNotFound = 4;
	private const int Canceled = 8;
	private const int InternalError = 9;
	private const string SqliteOptimizeSql = "PRAGMA optimize;";
	private const string SqliteWalCheckpointSql = "PRAGMA wal_checkpoint(TRUNCATE);";
	private const int MaxAutoexecTaskNameLength = 100;
	private const int MaxAutoexecMemoLength = 250;
	private const int WorkFileCleanupTargetAgeHours = 2;
	public const string DailyWalCheckpointCronExpression = "0 2 * * *";
	public const string DailyWalCheckpointTaskName = "SQLite WAL checkpoint データベースにWAL履歴を反映させるタスク";
	public const string WorkFileCleanupCronExpression = "*/10 * * * *";
	public const string WorkFileCleanupTaskName = "Work file cleanup ワークフォルダにある古いファイルを削除するタスク";

	public static readonly Guid DailyWalCheckpointTaskId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
	public static readonly Guid WorkFileCleanupTaskId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");

	private readonly ILogger<SchedulerService> _logger;
	private readonly NCrontab.Scheduler.IScheduler _scheduler;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private static readonly TimeSpan WorkFileCleanupTargetAge = TimeSpan.FromHours(WorkFileCleanupTargetAgeHours);

	private sealed record AutoexecTaskResult(int ReturnCode, int Count, string Memo);

	public SchedulerService(ILogger<SchedulerService> logger, NCrontab.Scheduler.IScheduler scheduler, IServiceScopeFactory scopeFactory, IConfiguration configuration, IWebHostEnvironment env) {
		_logger = logger;
		_scheduler = scheduler;
		_scopeFactory = scopeFactory;
		_configuration = configuration;
		_env = env;
	}

	/// <summary>
	/// 追加されたタスクを追加する
	/// </summary>
	public Task<SchedulerResult> AddTaskAsync(AddSchedulerTaskRequest request, CallContext context = default) {
		if (string.IsNullOrWhiteSpace(request.CronExpression)) {
			return Task.FromResult(new SchedulerResult { Result = InvalidRequest, Detail = "CronExpression が空です。" });
		}

		if (request.TaskType == SchedulerTaskType.Unknown) {
			return Task.FromResult(new SchedulerResult { Result = InvalidRequest, Detail = "TaskType が未指定です。" });
		}

		var taskName = string.IsNullOrWhiteSpace(request.TaskName)
			? request.TaskType.ToString()
			: request.TaskName.Trim();

		var result = RegisterTask(taskName, request.CronExpression, (db, ct) => ExecuteTaskCoreAsync(db, request, ct));
		return Task.FromResult(result);
	}

	/// <summary>
	/// 追加されたタスクを削除する
	/// </summary>
	public Task<SchedulerResult> RemoveTaskAsync(RemoveSchedulerTaskRequest request, CallContext context = default) {
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
	/// </summary>
	public Task<SchedulerResult> RemoveAllTasksAsync(CallContext context = default) {
		_scheduler.RemoveAllTasks();
		_logger.LogInformation("スケジュール全削除を実行しました。");
		return Task.FromResult(new SchedulerResult { Result = Success, Detail = "正常終了" });
	}

	public SchedulerResult RegisterDailySqliteWalCheckpointTask() {
		return RegisterTask(
			DailyWalCheckpointTaskName,
			DailyWalCheckpointCronExpression,
			(db, ct) => ExecuteSqliteWalCheckpointCoreAsync(db, DailyWalCheckpointTaskName, ct),
			DailyWalCheckpointTaskId);
	}

	public SchedulerResult RegisterWorkFileCleanupTask() {
		return RegisterTask(
			WorkFileCleanupTaskName,
			WorkFileCleanupCronExpression,
			(_, ct) => ExecuteWorkFileCleanupCoreAsync(WorkFileCleanupTaskName, ct),
			WorkFileCleanupTaskId);
	}

	public Task<GetSchedulerTasksResponse> GetTasksAsync(CallContext context = default) {
		var tasks = _scheduler.GetTasks();
		var result = new GetSchedulerTasksResponse { Result = Success, Detail = "正常終了" };
		foreach (var task in tasks) {
			result.Tasks.Add(new SchedulerTaskInfo {
				TaskId = task.Id.ToString(),
				TaskName = task.Name ?? string.Empty,
				CronExpression = task.CrontabSchedule.ToString(),
				NextOccurrence = task.CrontabSchedule.GetNextOccurrence(DateTime.Now),
				IsSystemTask = IsSystemTask(task.Name),
			});
		}
		return Task.FromResult(result);
	}

	public Task<SchedulerResult> UpdateTaskAsync(UpdateSchedulerTaskRequest request, CallContext context = default) {
		if (!Guid.TryParse(request.TaskId, out var guid)) {
			return Task.FromResult(new SchedulerResult { Result = InvalidTaskId, Detail = $"TaskId が不正です: {request.TaskId}", TaskId = request.TaskId });
		}

		CrontabSchedule schedule;
		try {
			schedule = CrontabSchedule.Parse(request.CronExpression);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "cron式が不正です。 Cron={CronExpression}", request.CronExpression);
			return Task.FromResult(new SchedulerResult { Result = InvalidCronExpression, Detail = $"cron式が不正です: {request.CronExpression}", TaskId = request.TaskId });
		}

		try {
			_scheduler.UpdateTask(guid, schedule);
			_logger.LogInformation("スケジュール更新: TaskId={TaskId}, Cron={Cron}", guid, request.CronExpression);
			return Task.FromResult(new SchedulerResult { Result = Success, Detail = "正常終了", TaskId = guid.ToString() });
		}
		catch (InvalidOperationException) {
			return Task.FromResult(new SchedulerResult { Result = TaskNotFound, Detail = $"対象タスクが存在しません: {request.TaskId}", TaskId = request.TaskId });
		}
		catch (Exception ex) {
			_logger.LogError(ex, "スケジュール更新に失敗しました。 TaskId={TaskId}", guid);
			return Task.FromResult(new SchedulerResult { Result = InternalError, Detail = "スケジュール更新に失敗しました。", TaskId = request.TaskId });
		}
	}

	private static bool IsSystemTask(string? taskName) {
		if (string.IsNullOrWhiteSpace(taskName))
			return false;
		return taskName.Equals(DailyWalCheckpointTaskName, StringComparison.OrdinalIgnoreCase)
			|| taskName.Equals(WorkFileCleanupTaskName, StringComparison.OrdinalIgnoreCase);
	}

	private SchedulerResult RegisterTask(string taskName, string cronExpression, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executor, Guid? taskId = null) {
		CrontabSchedule schedule;
		try {
			schedule = CrontabSchedule.Parse(cronExpression);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "cron式が不正です。 Cron={CronExpression}", cronExpression);
			return new SchedulerResult {
				Result = InvalidCronExpression,
				Detail = $"cron式が不正です: {cronExpression}",
			};
		}

		try {
			Guid guid;
			var scheduledTask = new AsyncScheduledTask(
				taskId ?? Guid.NewGuid(),
				taskName,
				schedule,
				ct => ExecuteScheduledTaskWithScopeAsync(taskName, ct, executor));
			if (taskId.HasValue) {
				guid = taskId.Value;
				_scheduler.AddTask(scheduledTask);
			}
			else {
				guid = scheduledTask.Id;
				_scheduler.AddTask(scheduledTask);
			}

			_logger.LogInformation(
				"スケジュール登録: TaskId={TaskId}, TaskName={TaskName}, Cron={Cron}",
				guid,
				taskName,
				cronExpression);

			return new SchedulerResult {
				Result = Success,
				Detail = "正常終了",
				TaskId = guid.ToString(),
			};
		}
		catch (Exception ex) {
			_logger.LogError(ex, "スケジュール登録に失敗しました。 TaskName={TaskName}", taskName);
			return new SchedulerResult {
				Result = InternalError,
				Detail = "スケジュール登録に失敗しました。",
			};
		}
	}

	private async Task ExecuteScheduledTaskWithScopeAsync(string taskName, CancellationToken cancellationToken, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executeAsync) {
		using var scope = _scopeFactory.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
		await ExecuteWithAutoexecHistoryAsync(db, taskName, cancellationToken, executeAsync);
	}

	private async Task<AutoexecTaskResult> ExecuteTaskCoreAsync(ExDatabase db, AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		return request.TaskType switch {
			SchedulerTaskType.LogOnly => await ExecuteLogOnlyAsync(request, cancellationToken),
			SchedulerTaskType.RunSummary => await ExecuteRunSummaryAsync(db, request, cancellationToken),
			_ => new AutoexecTaskResult(InvalidRequest, 0, $"未対応のTaskType: {request.TaskType}"),
		};
	}

	private Task<AutoexecTaskResult> ExecuteLogOnlyAsync(AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		_logger.LogInformation(
			"スケジュール実行: TaskType={TaskType}, TaskName={TaskName}, Payload={Payload}, Canceled={Canceled}",
			request.TaskType,
			request.TaskName,
			request.Payload,
			cancellationToken.IsCancellationRequested);
		return Task.FromResult(new AutoexecTaskResult(Success, 0, $"LogOnly実行: Payload={request.Payload}"));
	}

	private async Task<AutoexecTaskResult> ExecuteRunSummaryAsync(ExDatabase db, AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		var processedCount = 0;
		var returnCode = Success;
		var memo = string.Empty;
		try {
			string yyyymm = string.IsNullOrWhiteSpace(request.Payload)
				? DateTime.Now.ToString("yyyyMM")
				: request.Payload.Trim();
			memo = $"集計対象={yyyymm}";

			_logger.LogInformation(
				"集計開始: TaskName={TaskName}, yyyymm={yyyymm}, Canceled={Canceled}",
				request.TaskName,
				yyyymm,
				cancellationToken.IsCancellationRequested);

			var summaryDb = new SummaryDb(db);
			var param = new SummaryDateParameter(yyyymm, yyyymm);
			await foreach (var step in summaryDb.SummaryAllAsyncStream(param).WithCancellation(cancellationToken)) {
				if (step.IsCompleted) {
					memo = $"集計完了: yyyymm={yyyymm}, Duration={step.ErrorMessage}";
					_logger.LogInformation("集計完了: TaskName={TaskName}, Duration={Duration}",
						request.TaskName, step.ErrorMessage);
				}
				else if (step.IsError) {
					returnCode = InternalError;
					memo = $"集計エラー: Step={step.StepName}, Error={step.ErrorMessage}";
					_logger.LogError("集計エラー: Step={Step}, Error={Error}",
						step.StepName, step.ErrorMessage);
				}
				else {
					processedCount = step.Count;
					_logger.LogInformation("集計進捗: Step={Step}, Progress={Progress}, Count={Count}",
						step.StepName, step.Progress, step.Count);
				}
			}
		}
		catch (Exception ex) {
			_logger.LogError(ex, "集計実行中にエラーが発生しました: TaskName={TaskName}", request.TaskName);
			return new AutoexecTaskResult(InternalError, processedCount, $"集計例外: {ex.Message}");
		}
		return new AutoexecTaskResult(returnCode, processedCount, memo);
	}

	private Task<AutoexecTaskResult> ExecuteSqliteWalCheckpointCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		var checkpointResult = ExecuteSqliteWalCheckpoint(db);
		var busy = Helpers.GetCheckpointLongValue(checkpointResult, "busy");
		var logCount = Helpers.GetCheckpointLongValue(checkpointResult, "log");
		var checkpointed = Helpers.GetCheckpointLongValue(checkpointResult, "checkpointed");
		var memo = $"WALチェックポイント: Busy={busy}, Log={logCount}, Checkpointed={checkpointed}";

		if (busy > 0) {
			_logger.LogWarning(
				"WALチェックポイントは一部保留されました: TaskName={TaskName}, Busy={Busy}, Log={Log}, Checkpointed={Checkpointed}",
				taskName,
				busy,
				logCount,
				checkpointed);
		}
		else {
			_logger.LogInformation(
				"WALチェックポイント完了: TaskName={TaskName}, Busy={Busy}, Log={Log}, Checkpointed={Checkpointed}",
				taskName,
				busy,
				logCount,
				checkpointed);
		}
		return Task.FromResult(new AutoexecTaskResult(busy > 0 ? InternalError : Success, Helpers.ToHistoryCount(checkpointed), memo));
	}

	private Task<AutoexecTaskResult> ExecuteWorkFileCleanupCoreAsync(string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		var outputDir = ResolvePrintOutputDir();
		if (!Directory.Exists(outputDir)) {
			_logger.LogInformation("ワークファイル削除をスキップしました。 TaskName={TaskName}, OutputDir={OutputDir}, Reason=DirectoryNotFound", taskName, outputDir);
			return Task.FromResult(new AutoexecTaskResult(Success, 0, $"スキップ: OutputDir={outputDir}, Reason=DirectoryNotFound"));
		}

		var threshold = DateTime.Now - WorkFileCleanupTargetAge;
		var deletedCount = 0;
		var skippedCount = 0;
		var failedCount = 0;
		var scanErrorMessage = string.Empty;

		try {
			foreach (var entryPath in Directory.EnumerateFileSystemEntries(outputDir, "*", SearchOption.TopDirectoryOnly)) {
				cancellationToken.ThrowIfCancellationRequested();

				try {
					var entryInfo = File.GetAttributes(entryPath).HasFlag(FileAttributes.Directory)
						? new DirectoryInfo(entryPath)
						: new FileInfo(entryPath) as FileSystemInfo;
					var latestFileTime = entryInfo.LastWriteTime > entryInfo.CreationTime
						? entryInfo.LastWriteTime
						: entryInfo.CreationTime;

					if (latestFileTime > threshold) {
						skippedCount++;
						continue;
					}
					if (entryInfo is FileInfo fileInfo && (latestFileTime < threshold.AddDays(-1))) {
						skippedCount++;// ファイルは1日以上前のものだけ削除する
						continue;
					}

					if (entryInfo is DirectoryInfo directoryInfo)
						directoryInfo.Delete(true);
					else
						entryInfo.Delete();

					deletedCount++;
				}
				catch (Exception ex) {
					failedCount++;
					_logger.LogWarning(ex, "ワークファイル/フォルダ削除に失敗しました。 TaskName={TaskName}, Path={Path}", taskName, entryPath);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			_logger.LogInformation("ワークファイル/フォルダ削除をキャンセルしました。 TaskName={TaskName}, OutputDir={OutputDir}", taskName, outputDir);
			throw;
		}
		catch (Exception ex) {
			scanErrorMessage = ex.Message;
			_logger.LogError(ex, "ワークフォルダの走査に失敗しました。 TaskName={TaskName}, OutputDir={OutputDir}", taskName, outputDir);
		}

		_logger.LogInformation(
			"ワークファイル/フォルダ削除完了: TaskName={TaskName}, OutputDir={OutputDir}, Threshold={Threshold}, Deleted={Deleted}, Skipped={Skipped}, Failed={Failed}",
			taskName,
			outputDir,
			threshold,
			deletedCount,
			skippedCount,
			failedCount);

		var returnCode = failedCount > 0 || !string.IsNullOrWhiteSpace(scanErrorMessage) ? InternalError : Success;
		var memo = string.IsNullOrWhiteSpace(scanErrorMessage)
			? $"ワークファイル削除: OutputDir={outputDir}, Deleted={deletedCount}, Skipped={skippedCount}, Failed={failedCount}"
			: $"ワークファイル削除走査失敗: OutputDir={outputDir}, Deleted={deletedCount}, Skipped={skippedCount}, Failed={failedCount}, Error={scanErrorMessage}";

		return Task.FromResult(new AutoexecTaskResult(returnCode, deletedCount, memo));
	}

	private async Task ExecuteWithAutoexecHistoryAsync(ExDatabase db, string taskName, CancellationToken cancellationToken, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executeAsync) {
		var startTime = DateTime.Now;
		var stopwatch = Stopwatch.StartNew();
		var history = InsertAutoexecHistory(db, taskName, startTime);
		var result = new AutoexecTaskResult(Success, 0, "正常終了");
		Exception? caughtException = null;

		try {
			result = await executeAsync(db, cancellationToken);
		}
		catch (OperationCanceledException ex) {
			caughtException = ex;
			result = new AutoexecTaskResult(Canceled, 0, $"キャンセル: {ex.Message}");
		}
		catch (Exception ex) {
			caughtException = ex;
			result = new AutoexecTaskResult(InternalError, 0, $"例外: {ex.Message}");
		}
		finally {
			stopwatch.Stop();
			UpdateAutoexecHistory(db, history, DateTime.Now, stopwatch.Elapsed, result);
		}

		if (caughtException != null) {
			ExceptionDispatchInfo.Capture(caughtException).Throw();
		}
	}

	private SysHistAutoexec? InsertAutoexecHistory(ExDatabase db, string taskName, DateTime startTime) {
		try {
			var vdate = DateTime.Now.ToUniversalTime().Ticks;
			var history = new SysHistAutoexec {
				TaskName = Helpers.NormalizeAutoexecText(taskName, MaxAutoexecTaskNameLength, "未設定"),
				StartTime = Helpers.ToAutoexecDateTimeString(startTime),
				ReturnCode = Success,
				Memo = "処理開始",
				Vdc = vdate,
				Vdu = vdate,
			};

			db.Insert(history);
			return history;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴の開始登録に失敗しました。 TaskName={TaskName}", taskName);
			return null;
		}
	}

	private void UpdateAutoexecHistory(ExDatabase db, SysHistAutoexec? history, DateTime endTime, TimeSpan elapsedTime, AutoexecTaskResult result) {
		if (history == null) {
			return;
		}

		try {
			history.EndTime = Helpers.ToAutoexecDateTimeString(endTime);
			history.ElapsedTime = Helpers.ToHistoryElapsedSeconds(elapsedTime);
			history.ReturnCode = result.ReturnCode;
			history.Count = result.Count;
			history.Memo = Helpers.NormalizeAutoexecText(result.Memo, MaxAutoexecMemoLength, "処理完了");
			history.Vdu = DateTime.Now.ToUniversalTime().Ticks;

			db.Update(history, ["EndTime", "ElapsedTime", "ReturnCode", "Count", "Memo", "Vdu"]);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴の終了更新に失敗しました。 Id={Id}, TaskName={TaskName}", history.Id, history.TaskName);
		}
	}

	private string ResolvePrintOutputDir() {
		var printServer = _configuration.GetSection("PrintServer");
		var contentRootPath = _env.ContentRootPath;
		var configuredBaseDir = printServer.GetValue<string>("PrintBaseDir") ?? ".";
		var configuredOutputDir = printServer.GetValue<string>("PrintOutputDir") ?? ".";
		var resolvedBaseDir = Path.GetFullPath(Path.IsPathRooted(configuredBaseDir)
			? configuredBaseDir
			: Path.Combine(contentRootPath, configuredBaseDir));

		return Path.GetFullPath(Path.IsPathRooted(configuredOutputDir)
			? configuredOutputDir
			: Path.Combine(resolvedBaseDir, configuredOutputDir));
	}

	public static Dictionary<string, object> ExecuteSqliteWalCheckpoint(ExDatabase db) {
		Helpers.EnsureRawExecSucceeded(db.RawExecCmd(SqliteOptimizeSql), SqliteOptimizeSql);
		var result = db.RawExecCmd(SqliteWalCheckpointSql);
		Helpers.EnsureRawExecSucceeded(result, SqliteWalCheckpointSql);
		if (result.Count == 0) {
			return new Dictionary<string, object>();
		}
		var checkpointResult = result.First();
		return checkpointResult;
	}

	private static class Helpers {
		public static string ToAutoexecDateTimeString(DateTime dateTime) {
			return dateTime.ToString("yyyyMMddHHmmss");
		}

		public static int ToHistoryElapsedSeconds(TimeSpan elapsedTime) {
			if (elapsedTime.TotalSeconds <= 0) {
				return 0;
			}
			if (elapsedTime.TotalSeconds >= int.MaxValue) {
				return int.MaxValue;
			}
			return (int)Math.Ceiling(elapsedTime.TotalSeconds);
		}

		public static int ToHistoryCount(long count) {
			if (count <= 0) {
				return 0;
			}
			if (count >= int.MaxValue) {
				return int.MaxValue;
			}
			return (int)count;
		}

		public static string NormalizeAutoexecText(string? value, int maxLength, string defaultValue) {
			var text = string.IsNullOrWhiteSpace(value)
				? defaultValue
				: value.Replace("\r", " ").Replace("\n", " ").Trim();
			if (text.Length <= maxLength) {
				return text;
			}
			if (maxLength <= 3) {
				return text[..maxLength];
			}
			return text[..(maxLength - 3)] + "...";
		}

		public static void EnsureRawExecSucceeded(List<Dictionary<string, object>> result, string sql) {
			if (result.Count == 0) {
				return;
			}

			if (result[0].TryGetValue("Error", out var error) && error != null) {
				throw new InvalidOperationException($"SQLite maintenance SQL failed: {sql} :: {error}");
			}
		}

		public static object? GetCheckpointValue(Dictionary<string, object> checkpointResult, string key) {
			return checkpointResult.TryGetValue(key, out var value) ? value : null;
		}

		public static long GetCheckpointLongValue(Dictionary<string, object> checkpointResult, string key) {
			var value = GetCheckpointValue(checkpointResult, key);
			return value switch {
				byte number => number,
				short number => number,
				int number => number,
				long number => number,
				string text when long.TryParse(text, out var parsed) => parsed,
				_ => 0,
			};
		}
	}
}
