using CodeShare;
using CvBase;
using CvDomainLogic;
using NCrontab;
using NCrontab.Scheduler;
using ProtoBuf.Grpc;
using System.Diagnostics;
using System.Runtime.ExceptionServices;


namespace CvServer.Services;


public class SchedulerService : CodeShare.IScheduler {
	private const int Success = 0;
	private const int InvalidRequest = 1;
	private const int InvalidCronExpression = 2;
	private const int InvalidTaskId = 3;
	private const int TaskNotFound = 4;
	private const int Canceled = 8;
	private const int InternalError = 9;
	private const string SqliteOptimizeSql = "PRAGMA optimize;";
	private const string SqliteWalCheckpointSql = "PRAGMA wal_checkpoint(TRUNCATE);";
	private const string SqliteVacuumSql = "VACUUM;";
	private const int MaxAutoexecTaskNameLength = 100;
	private const int MaxAutoexecMemoLength = 250;
	private const int WorkFileCleanupTargetAgeHours = 2;
	public const string DailyWalCheckpointCronExpression = "0 2 * * *";
	public const string DailyWalCheckpointTaskName = "SQLite WAL checkpoint";
	public const string WorkFileCleanupCronExpression = "*/10 * * * *";
	public const string WorkFileCleanupTaskName = "Work file cleanup";

	private readonly ILogger<SchedulerService> _logger;
	private readonly NCrontab.Scheduler.IScheduler _scheduler;
	private readonly ExDatabase _db;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private static readonly TimeSpan WorkFileCleanupTargetAge = TimeSpan.FromHours(WorkFileCleanupTargetAgeHours);

	private sealed record AutoexecTaskResult(int ReturnCode, int Count, string Memo);

	public SchedulerService(ILogger<SchedulerService> logger, NCrontab.Scheduler.IScheduler scheduler, ExDatabase db, IConfiguration configuration, IWebHostEnvironment env) {
		_logger = logger;
		_scheduler = scheduler;
		_db = db;
		_configuration = configuration;
		_env = env;
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

	public SchedulerResult RegisterDailySqliteWalCheckpointTask() {
		try {
			var schedule = CrontabSchedule.Parse(DailyWalCheckpointCronExpression);
			var guid = _scheduler.AddTask(
				crontabSchedule: schedule,
				action: ct => ExecuteSqliteWalCheckpointTaskAsync(DailyWalCheckpointTaskName, ct).GetAwaiter().GetResult());

			_logger.LogInformation(
				"SQLite WAL checkpoint の定期実行を登録しました。 TaskId={TaskId}, TaskName={TaskName}, Cron={Cron}",
				guid,
				DailyWalCheckpointTaskName,
				DailyWalCheckpointCronExpression);

			return new SchedulerResult {
				Result = Success,
				Detail = "正常終了",
				TaskId = guid.ToString(),
			};
		}
		catch (Exception ex) {
			_logger.LogError(ex, "SQLite WAL checkpoint の定期実行登録に失敗しました。");
			return new SchedulerResult {
				Result = InternalError,
				Detail = "SQLite WAL checkpoint の定期実行登録に失敗しました。",
			};
		}
	}

	public SchedulerResult RegisterWorkFileCleanupTask() {
		try {
			var schedule = CrontabSchedule.Parse(WorkFileCleanupCronExpression);
			var guid = _scheduler.AddTask(
				crontabSchedule: schedule,
				action: ct => ExecuteWorkFileCleanupTaskAsync(WorkFileCleanupTaskName, ct).GetAwaiter().GetResult());

			_logger.LogInformation(
				"ワークファイル削除の定期実行を登録しました。 TaskId={TaskId}, TaskName={TaskName}, Cron={Cron}, TargetAge={TargetAge}",
				guid,
				WorkFileCleanupTaskName,
				WorkFileCleanupCronExpression,
				WorkFileCleanupTargetAge);

			return new SchedulerResult {
				Result = Success,
				Detail = "正常終了",
				TaskId = guid.ToString(),
			};
		}
		catch (Exception ex) {
			_logger.LogError(ex, "ワークファイル削除の定期実行登録に失敗しました。");
			return new SchedulerResult {
				Result = InternalError,
				Detail = "ワークファイル削除の定期実行登録に失敗しました。",
			};
		}
	}

	private Task ExecuteTaskAsync(AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		var taskName = string.IsNullOrWhiteSpace(request.TaskName)
			? request.TaskType.ToString()
			: request.TaskName.Trim();
		return ExecuteWithAutoexecHistoryAsync(taskName, cancellationToken, ct => ExecuteTaskCoreAsync(request, ct));
	}

	private async Task<AutoexecTaskResult> ExecuteTaskCoreAsync(AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		switch (request.TaskType) {
			case SchedulerTaskType.LogOnly:
				_logger.LogInformation(
					"スケジュール実行: TaskType={TaskType}, TaskName={TaskName}, Payload={Payload}, Canceled={Canceled}",
					request.TaskType,
					request.TaskName,
					request.Payload,
					cancellationToken.IsCancellationRequested);
				return new AutoexecTaskResult(Success, 0, $"LogOnly実行: Payload={request.Payload}");

			case SchedulerTaskType.RunSummary:
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

					var summaryDb = new SummaryDb(_db);
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

			default:
				_logger.LogWarning("未対応の TaskType です: {TaskType}", request.TaskType);
				return new AutoexecTaskResult(InvalidRequest, 0, $"未対応のTaskType: {request.TaskType}");
		}
	}

	private Task ExecuteSqliteWalCheckpointTaskAsync(string taskName, CancellationToken cancellationToken) {
		return ExecuteWithAutoexecHistoryAsync(taskName, cancellationToken, ct => ExecuteSqliteWalCheckpointCoreAsync(taskName, ct));
	}

	private Task<AutoexecTaskResult> ExecuteSqliteWalCheckpointCoreAsync(string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		try {
			var checkpointResult = ExecuteSqliteWalCheckpoint(_db);
			var busy = GetCheckpointLongValue(checkpointResult, "busy");
			var logCount = GetCheckpointLongValue(checkpointResult, "log");
			var checkpointed = GetCheckpointLongValue(checkpointResult, "checkpointed");
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
			return Task.FromResult(new AutoexecTaskResult(busy > 0 ? InternalError : Success, ToHistoryCount(checkpointed), memo));
		}
		catch (Exception ex) {
			_logger.LogError(ex, "WALチェックポイント実行中にエラーが発生しました: TaskName={TaskName}", taskName);
			throw;
		}
	}

	private Task ExecuteWorkFileCleanupTaskAsync(string taskName, CancellationToken cancellationToken) {
		return ExecuteWithAutoexecHistoryAsync(taskName, cancellationToken, ct => ExecuteWorkFileCleanupCoreAsync(taskName, ct));
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

					if (entryInfo is DirectoryInfo directoryInfo) {
						directoryInfo.Delete(true);
					}
					else {
						entryInfo.Delete();
					}

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

	private async Task ExecuteWithAutoexecHistoryAsync(string taskName, CancellationToken cancellationToken, Func<CancellationToken, Task<AutoexecTaskResult>> executeAsync) {
		var startTime = DateTime.Now;
		var stopwatch = Stopwatch.StartNew();
		var history = InsertAutoexecHistory(taskName, startTime);
		var result = new AutoexecTaskResult(Success, 0, "正常終了");
		Exception? caughtException = null;

		try {
			result = await executeAsync(cancellationToken);
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
			UpdateAutoexecHistory(history, DateTime.Now, stopwatch.Elapsed, result);
		}

		if (caughtException != null) {
			ExceptionDispatchInfo.Capture(caughtException).Throw();
		}
	}

	private SysHistAutoexec? InsertAutoexecHistory(string taskName, DateTime startTime) {
		try {
			var vdate = DateTime.Now.ToUniversalTime().Ticks;
			var history = new SysHistAutoexec {
				TaskName = NormalizeAutoexecText(taskName, MaxAutoexecTaskNameLength, "未設定"),
				StartTime = ToAutoexecDateTimeString(startTime),
				ReturnCode = Success,
				Memo = "処理開始",
				Vdc = vdate,
				Vdu = vdate,
			};

			_db.Insert(history);
			return history;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴の開始登録に失敗しました。 TaskName={TaskName}", taskName);
			return null;
		}
	}

	private void UpdateAutoexecHistory(SysHistAutoexec? history, DateTime endTime, TimeSpan elapsedTime, AutoexecTaskResult result) {
		if (history == null) {
			return;
		}

		try {
			history.EndTime = ToAutoexecDateTimeString(endTime);
			history.ElapsedTime = ToHistoryElapsedSeconds(elapsedTime);
			history.ReturnCode = result.ReturnCode;
			history.Count = result.Count;
			history.Memo = NormalizeAutoexecText(result.Memo, MaxAutoexecMemoLength, "処理完了");
			history.Vdu = DateTime.Now.ToUniversalTime().Ticks;

			_db.Update(history, ["EndTime", "ElapsedTime", "ReturnCode", "Count", "Memo", "Vdu"]);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴の終了更新に失敗しました。 Id={Id}, TaskName={TaskName}", history.Id, history.TaskName);
		}
	}

	private static string ToAutoexecDateTimeString(DateTime dateTime) {
		return dateTime.ToString("yyyyMMddHHmmss");
	}

	private static int ToHistoryElapsedSeconds(TimeSpan elapsedTime) {
		if (elapsedTime.TotalSeconds <= 0) {
			return 0;
		}
		if (elapsedTime.TotalSeconds >= int.MaxValue) {
			return int.MaxValue;
		}
		return (int)Math.Ceiling(elapsedTime.TotalSeconds);
	}

	private static int ToHistoryCount(long count) {
		if (count <= 0) {
			return 0;
		}
		if (count >= int.MaxValue) {
			return int.MaxValue;
		}
		return (int)count;
	}

	private static string NormalizeAutoexecText(string? value, int maxLength, string defaultValue) {
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
		EnsureRawExecSucceeded(db.RawExecCmd(SqliteOptimizeSql), SqliteOptimizeSql);
		var result = db.RawExecCmd(SqliteWalCheckpointSql);
		EnsureRawExecSucceeded(result, SqliteWalCheckpointSql);
		if (result.Count == 0) {
			return new Dictionary<string, object>();
		}
		var checkpointResult = result.First();
		if (GetCheckpointLongValue(checkpointResult, "busy") == 0) {
			EnsureRawExecSucceeded(db.RawExecCmd(SqliteVacuumSql), SqliteVacuumSql);
		}
		return checkpointResult;
	}

	private static void EnsureRawExecSucceeded(List<Dictionary<string, object>> result, string sql) {
		if (result.Count == 0) {
			return;
		}

		if (result[0].TryGetValue("Error", out var error) && error != null) {
			throw new InvalidOperationException($"SQLite maintenance SQL failed: {sql} :: {error}");
		}
	}

	private static object? GetCheckpointValue(Dictionary<string, object> checkpointResult, string key) {
		return checkpointResult.TryGetValue(key, out var value) ? value : null;
	}

	private static long GetCheckpointLongValue(Dictionary<string, object> checkpointResult, string key) {
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
