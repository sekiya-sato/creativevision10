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
	private const int IntervalTooShort = 5;
	private const int Canceled = 8;
	private const int InternalError = 9;
	private const string SqliteOptimizeSql = "PRAGMA optimize;";
	private const string SqliteWalCheckpointSql = "PRAGMA wal_checkpoint(TRUNCATE);";
	private const int MaxAutoexecTaskNameLength = 100;
	private const int MaxAutoexecMemoLength = 250;
	private const int WorkFileCleanupTargetAgeHours = 2;
	/// <summary>起動間隔の下限チェックを行うかどうかの内部フラグ（重い処理の連続実行を防ぐ）</summary>
	public const bool MinIntervalCheckEnabled = true;
	/// <summary>起動間隔の下限（分）。重い処理はこれより短い間隔では設定させない</summary>
	public const int MinIntervalMinutes = 60;
	/// <summary>起動間隔算出のための発生列挙回数の上限（暴走防止。毎分実行cronは31日で44640件になる）</summary>
	private const int MaxIntervalOccurrenceCount = 20000;
	/// <summary>起動間隔算出の対象期間（日数）</summary>
	private const int IntervalLookaheadDays = 31;

	public const string DailyWalCheckpointCronExpression = "0 2 * * *";
	public const string DailyWalCheckpointTaskName = "SQLite WAL checkpoint データベースにWAL履歴を反映させるタスク";
	public const string WorkFileCleanupCronExpression = "30 0,12 * * *";
	public const string WorkFileCleanupTaskName = "Work file cleanup ワークフォルダにある古いファイルを削除するタスク";
	public const string MonthlyResummaryCronExpression = "10 1 * * *";
	public const string MonthlyResummaryTaskName = "在庫 売掛 買掛 の当月と前月 を再集計するタスク";
	public const string JodaiPurgeCronExpression = "40 1 * * *";
	public const string JodaiPurgeTaskName = "上代 適用期間が過ぎた適用上代(DerivedJodai)を削除するタスク";
	public const string MasterShohinMeishoRebuildCronExpression = "20 3 * * *";
	public const string MasterShohinMeishoRebuildTaskName = "商品名称再構築 MasterShohinのId_Col/Id_Sizが0のデータから名称マスタを再構築するタスク";
	public const string MasterVColumnResyncCronExpression = "40 3 * * *";
	public const string MasterVColumnResyncTaskName = "V*列再同期 マスタ名称の複製列(V*列)を現在のマスタ内容で再同期するタスク";
	public const string TranTaxRebuildCronExpression = "0 4 * * *";
	public const string TranTaxRebuildTaskName = "伝票税額再更新 対象6伝票の期首日以降を取引先マスタの現在の税設定で再計算するタスク";

	public static readonly Guid DailyWalCheckpointTaskId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
	public static readonly Guid WorkFileCleanupTaskId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
	public static readonly Guid MonthlyResummaryTaskId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
	public static readonly Guid JodaiPurgeTaskId = Guid.Parse("d4e5f6a7-b8c9-0123-def0-234567890123");
	public static readonly Guid MasterShohinMeishoRebuildTaskId = Guid.Parse("e5f6a7b8-c9d0-1234-ef01-345678901234");
	public static readonly Guid MasterVColumnResyncTaskId = Guid.Parse("f6a7b8c9-d0e1-2345-f012-456789012345");
	public static readonly Guid TranTaxRebuildTaskId = Guid.Parse("a7b8c9d0-e1f2-3456-0123-567890123456");

	/// <summary>ジョブを識別するキー（<see cref="MasterConfig"/> の Name に使う固定文字列）</summary>
	public const string JobKeyWalCheckpoint = "WalCheckpoint";
	public const string JobKeyWorkFileCleanup = "WorkFileCleanup";
	public const string JobKeyMonthlyResummary = "MonthlyResummary";
	public const string JobKeyJodaiPurge = "JodaiPurge";
	public const string JobKeyMasterShohinMeishoRebuild = "MasterShohinMeishoRebuild";
	public const string JobKeyMasterVColumnResync = "MasterVColumnResync";
	public const string JobKeyTranTaxRebuild = "TranTaxRebuild";

	/// <summary>システムジョブ1件の定義（TaskId・設定キー・名称・既定cron・既定の実行フラグ・起動間隔チェックの有無）</summary>
	public sealed record SchedulerJobDefinition(
		Guid TaskId,
		string JobKey,
		string TaskName,
		string DefaultCronExpression,
		bool DefaultEnabled,
		bool CheckMinInterval);

	/// <summary>
	/// サーバが自動登録するシステムジョブの一覧。
	/// 実行フラグ・cron式の永続値は <see cref="SchedulerJobConfigDb"/> 経由で参照し、未設定ならここの既定値を使う。
	/// </summary>
	public static readonly IReadOnlyList<SchedulerJobDefinition> SystemJobDefinitions = [
		new(DailyWalCheckpointTaskId, JobKeyWalCheckpoint, DailyWalCheckpointTaskName, DailyWalCheckpointCronExpression, true, false),
		new(WorkFileCleanupTaskId, JobKeyWorkFileCleanup, WorkFileCleanupTaskName, WorkFileCleanupCronExpression, true, false),
		new(MonthlyResummaryTaskId, JobKeyMonthlyResummary, MonthlyResummaryTaskName, MonthlyResummaryCronExpression, true, false),
		new(JodaiPurgeTaskId, JobKeyJodaiPurge, JodaiPurgeTaskName, JodaiPurgeCronExpression, true, false),
		new(MasterShohinMeishoRebuildTaskId, JobKeyMasterShohinMeishoRebuild, MasterShohinMeishoRebuildTaskName, MasterShohinMeishoRebuildCronExpression, false, true),
		new(MasterVColumnResyncTaskId, JobKeyMasterVColumnResync, MasterVColumnResyncTaskName, MasterVColumnResyncCronExpression, false, true),
		new(TranTaxRebuildTaskId, JobKeyTranTaxRebuild, TranTaxRebuildTaskName, TranTaxRebuildCronExpression, false, true),
	];

	private readonly ILogger<SchedulerService> _logger;
	private readonly NCrontab.Scheduler.IScheduler _scheduler;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private static readonly TimeSpan WorkFileCleanupTargetAge = TimeSpan.FromHours(WorkFileCleanupTargetAgeHours);

	private sealed record AutoexecTaskResult(int ReturnCode, int Count, string Memo);

	/// <summary>
	/// 再集計の区分（在庫/売掛/買掛）ごとの実行定義
	/// </summary>
	private sealed record ResummaryGroup(string Label, Func<CalcDateTermParameter, IAsyncEnumerable<StreamStepProgress>> CreateStream);

	private sealed record SummaryStreamResult(int Count, string? ErrorMessage);

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
		var def = FindDefinition(JobKeyWalCheckpoint);
		return RegisterSystemJob(def, (db, ct) => ExecuteSqliteWalCheckpointCoreAsync(db, def.TaskName, ct));
	}

	public SchedulerResult RegisterWorkFileCleanupTask() {
		var def = FindDefinition(JobKeyWorkFileCleanup);
		return RegisterSystemJob(def, (_, ct) => ExecuteWorkFileCleanupCoreAsync(def.TaskName, ct));
	}

	/// <summary>
	/// 在庫・売掛・買掛の当月/前月を再集計するタスクを登録する
	/// </summary>
	public SchedulerResult RegisterMonthlyResummaryTask() {
		var def = FindDefinition(JobKeyMonthlyResummary);
		return RegisterSystemJob(def, (db, ct) => ExecuteMonthlyResummaryCoreAsync(db, def.TaskName, ct));
	}

	/// <summary>
	/// 期限切れの適用上代(<see cref="DerivedJodai"/>)を削除するタスクを登録する。
	/// <para>
	/// 保持日数は <see cref="MasterConfig"/> の <see cref="JodaiDb.ConfigKeepDaysName"/>（既定90日）。
	/// 伝票(<see cref="TranJodai"/>)は残るので、必要になれば <see cref="JodaiDb.Rebuild"/> で復元できる。
	/// プロパー(P)区分は DayTo="99991231" のため削除対象にならない。
	/// </para>
	/// </summary>
	public SchedulerResult RegisterJodaiPurgeTask() {
		var def = FindDefinition(JobKeyJodaiPurge);
		return RegisterSystemJob(def, (db, ct) => ExecuteJodaiPurgeCoreAsync(db, def.TaskName, ct));
	}

	/// <summary>
	/// 商品名称マスタ再構築（MasterShohinのId_Col/Id_Sizが0のデータから名称マスタを再構築）を登録する。
	/// 重い処理のため既定は無効(IsEnabled=false)で、起動間隔の下限チェック対象。
	/// </summary>
	public SchedulerResult RegisterMasterShohinMeishoRebuildTask() {
		var def = FindDefinition(JobKeyMasterShohinMeishoRebuild);
		return RegisterSystemJob(def, (db, ct) => ExecuteMasterShohinMeishoRebuildCoreAsync(db, def.TaskName, ct));
	}

	/// <summary>
	/// マスタ名称の複製列(V*列)を現在のマスタ内容で再同期するタスクを登録する。
	/// 重い処理のため既定は無効(IsEnabled=false)で、起動間隔の下限チェック対象。
	/// </summary>
	public SchedulerResult RegisterMasterVColumnResyncTask() {
		var def = FindDefinition(JobKeyMasterVColumnResync);
		return RegisterSystemJob(def, (db, ct) => ExecuteMasterVColumnResyncCoreAsync(db, def.TaskName, ct));
	}

	/// <summary>
	/// 対象6伝票の期首日以降を取引先マスタの現在の税設定で再計算するタスクを登録する。
	/// 重い処理のため既定は無効(IsEnabled=false)で、起動間隔の下限チェック対象。
	/// </summary>
	public SchedulerResult RegisterTranTaxRebuildTask() {
		var def = FindDefinition(JobKeyTranTaxRebuild);
		return RegisterSystemJob(def, (db, ct) => ExecuteTranTaxRebuildCoreAsync(db, def.TaskName, ct));
	}

	public Task<GetSchedulerTasksResponse> GetTasksAsync(CallContext context = default) {
		var tasks = _scheduler.GetTasks();
		var result = new GetSchedulerTasksResponse { Result = Success, Detail = "正常終了" };

		SchedulerJobConfigDb? configDb = null;
		IServiceScope? scope = null;
		try {
			scope = _scopeFactory.CreateScope();
			configDb = new SchedulerJobConfigDb(scope.ServiceProvider.GetRequiredService<ExDatabase>());
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "スケジュールタスク一覧取得時に永続設定の取得に失敗しました。既定値を使用します。");
		}

		try {
			foreach (var task in tasks) {
				var def = FindDefinitionByTaskId(task.Id);
				var checkMinInterval = def != null && def.CheckMinInterval && MinIntervalCheckEnabled;
				bool isEnabled;
				if (def == null) {
					isEnabled = true;
				}
				else {
					bool? persisted = null;
					try {
						persisted = configDb?.GetEnabled(def.TaskId);
					}
					catch (Exception ex) {
						_logger.LogWarning(ex, "実行フラグの取得に失敗しました。 JobKey={JobKey}", def.JobKey);
					}
					isEnabled = persisted ?? def.DefaultEnabled;
				}

				result.Tasks.Add(new SchedulerTaskInfo {
					TaskId = task.Id.ToString(),
					TaskName = task.Name ?? string.Empty,
					CronExpression = task.CrontabSchedule.ToString(),
					NextOccurrence = task.CrontabSchedule.GetNextOccurrence(DateTime.Now),
					IsSystemTask = IsSystemTask(task.Name),
					IsEnabled = isEnabled,
					CheckMinInterval = checkMinInterval,
					MinIntervalMinutes = checkMinInterval ? MinIntervalMinutes : 0,
				});
			}
		}
		finally {
			scope?.Dispose();
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

		var def = FindDefinitionByTaskId(guid);
		if (ViolatesMinInterval(def, schedule, out var actualMinutes)) {
			return Task.FromResult(new SchedulerResult {
				Result = IntervalTooShort,
				Detail = $"起動間隔が短すぎます: 最小間隔={actualMinutes}分, 下限={MinIntervalMinutes}分。1時間以上あける設定にしてください。",
				TaskId = request.TaskId,
			});
		}

		try {
			_scheduler.UpdateTask(guid, schedule);
			_logger.LogInformation("スケジュール更新: TaskId={TaskId}, Cron={Cron}", guid, request.CronExpression);

			if (def != null) {
				try {
					using var scope = _scopeFactory.CreateScope();
					var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
					new SchedulerJobConfigDb(db).SetCron(def.TaskId, request.CronExpression);
				}
				catch (Exception ex) {
					_logger.LogWarning(ex, "cron式の永続化に失敗しました。 JobKey={JobKey}, Cron={Cron}", def.JobKey, request.CronExpression);
				}
			}

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

	/// <summary>
	/// スケジュールタスクの実行する/しないフラグを設定する。システムジョブ以外は対象外。
	/// 起動間隔チェック対象のジョブを有効化する場合は、現在の登録cron(無ければ永続値、それも無ければ既定cron)で間隔検証を行う。
	/// </summary>
	public Task<SchedulerResult> SetTaskEnabledAsync(SetSchedulerTaskEnabledRequest request, CallContext context = default) {
		if (!Guid.TryParse(request.TaskId, out var guid)) {
			return Task.FromResult(new SchedulerResult { Result = InvalidTaskId, Detail = $"TaskId が不正です: {request.TaskId}", TaskId = request.TaskId });
		}

		var def = FindDefinitionByTaskId(guid);
		if (def == null) {
			return Task.FromResult(new SchedulerResult { Result = TaskNotFound, Detail = "実行フラグを設定できるのはシステムジョブのみです。", TaskId = request.TaskId });
		}

		try {
			if (request.IsEnabled) {
				var scheduledTask = _scheduler.GetTasks().FirstOrDefault(t => t.Id == guid);
				var schedule = scheduledTask?.CrontabSchedule;
				if (schedule == null) {
					// スケジュール未登録の場合は永続値、それも無ければ既定cronで検証する
					var cronExpression = def.DefaultCronExpression;
					try {
						using var scope = _scopeFactory.CreateScope();
						var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
						cronExpression = new SchedulerJobConfigDb(db).GetCron(def.TaskId) ?? def.DefaultCronExpression;
					}
					catch (Exception ex) {
						_logger.LogWarning(ex, "実行フラグ設定時の永続cron式取得に失敗しました。 JobKey={JobKey}", def.JobKey);
					}
					try {
						schedule = CrontabSchedule.Parse(cronExpression);
					}
					catch (Exception ex) {
						_logger.LogWarning(ex, "実行フラグ設定時のcron式解析に失敗しました。 JobKey={JobKey}, Cron={Cron}", def.JobKey, cronExpression);
						schedule = null;
					}
				}

				if (schedule != null && ViolatesMinInterval(def, schedule, out var actualMinutes)) {
					return Task.FromResult(new SchedulerResult {
						Result = IntervalTooShort,
						Detail = $"起動間隔が短すぎます: 最小間隔={actualMinutes}分, 下限={MinIntervalMinutes}分。1時間以上あける設定にしてください。",
						TaskId = request.TaskId,
					});
				}
			}

			using var configScope = _scopeFactory.CreateScope();
			var configDb = configScope.ServiceProvider.GetRequiredService<ExDatabase>();
			new SchedulerJobConfigDb(configDb).SetEnabled(def.TaskId, request.IsEnabled);
			_logger.LogInformation("実行フラグ設定: JobKey={JobKey}, TaskId={TaskId}, IsEnabled={IsEnabled}", def.JobKey, guid, request.IsEnabled);

			return Task.FromResult(new SchedulerResult { Result = Success, Detail = "正常終了", TaskId = guid.ToString() });
		}
		catch (Exception ex) {
			_logger.LogError(ex, "実行フラグ設定に失敗しました。 TaskId={TaskId}", guid);
			return Task.FromResult(new SchedulerResult { Result = InternalError, Detail = "実行フラグ設定に失敗しました。", TaskId = request.TaskId });
		}
	}

	private static bool IsSystemTask(string? taskName) {
		if (string.IsNullOrWhiteSpace(taskName))
			return false;
		foreach (var def in SystemJobDefinitions) {
			if (taskName.Equals(def.TaskName, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	/// <summary>JobKey からシステムジョブ定義を取得する。見つからない場合は実装不備なので例外にする。</summary>
	private static SchedulerJobDefinition FindDefinition(string jobKey) {
		foreach (var def in SystemJobDefinitions) {
			if (def.JobKey == jobKey)
				return def;
		}
		throw new InvalidOperationException($"未定義のJobKeyです: {jobKey}");
	}

	/// <summary>TaskId からシステムジョブ定義を取得する。アドホックタスクの場合は null。</summary>
	private static SchedulerJobDefinition? FindDefinitionByTaskId(Guid taskId) {
		foreach (var def in SystemJobDefinitions) {
			if (def.TaskId == taskId)
				return def;
		}
		return null;
	}

	/// <summary>
	/// cron式から起動間隔の最小値(分)を算出する。基準時刻から<see cref="IntervalLookaheadDays"/>日先までの発生を列挙し、
	/// 連続する発生の最小間隔を返す。最小間隔が<see cref="MinIntervalMinutes"/>未満になった時点で打ち切る。
	/// 発生が1件以下の場合は間隔不明として<c>null</c>を返す(チェック対象外として許可)。
	/// </summary>
	public static int? CalculateMinIntervalMinutes(CrontabSchedule schedule) {
		ArgumentNullException.ThrowIfNull(schedule);

		var now = DateTime.Now;
		var baseTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
		var endTime = baseTime.AddDays(IntervalLookaheadDays);

		DateTime? previous = null;
		int? minMinutes = null;
		var enumerated = 0;
		foreach (var occurrence in schedule.GetNextOccurrences(baseTime, endTime)) {
			enumerated++;
			if (previous.HasValue) {
				var minutes = (int)(occurrence - previous.Value).TotalMinutes;
				if (minMinutes == null || minutes < minMinutes.Value) {
					minMinutes = minutes;
				}
				if (minMinutes.Value < MinIntervalMinutes) {
					break;
				}
			}
			previous = occurrence;
			if (enumerated >= MaxIntervalOccurrenceCount) {
				break;
			}
		}
		return minMinutes;
	}

	/// <summary>
	/// 起動間隔の下限違反を判定する。チェック無効/対象外ジョブ/アドホックタスクは常に違反なしとする。
	/// </summary>
	private static bool ViolatesMinInterval(SchedulerJobDefinition? definition, CrontabSchedule schedule, out int actualMinutes) {
		actualMinutes = 0;
		if (!MinIntervalCheckEnabled || definition == null || !definition.CheckMinInterval) {
			return false;
		}

		var minutes = CalculateMinIntervalMinutes(schedule);
		if (minutes == null) {
			return false;
		}

		actualMinutes = minutes.Value;
		return minutes.Value < MinIntervalMinutes;
	}

	/// <summary>
	/// システムジョブ定義を登録する。cron式は永続値(<see cref="SchedulerJobConfigDb.GetCron"/>)があればそれを使い、
	/// 無い/parse失敗の場合は定義の既定cronにフォールバックする。
	/// </summary>
	private SchedulerResult RegisterSystemJob(SchedulerJobDefinition definition, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executor) {
		var cronExpression = definition.DefaultCronExpression;
		try {
			using var scope = _scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
			var persistedCron = new SchedulerJobConfigDb(db).GetCron(definition.TaskId);
			if (!string.IsNullOrWhiteSpace(persistedCron)) {
				try {
					CrontabSchedule.Parse(persistedCron);
					cronExpression = persistedCron;
				}
				catch (Exception ex) {
					_logger.LogWarning(ex, "永続化されたcron式が不正なため既定値を使用します。 JobKey={JobKey}, Cron={Cron}", definition.JobKey, persistedCron);
				}
			}
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "永続化cron式の取得に失敗したため既定値を使用します。 JobKey={JobKey}", definition.JobKey);
		}

		return RegisterTask(definition.TaskName, cronExpression, executor, definition.TaskId, definition);
	}

	private SchedulerResult RegisterTask(string taskName, string cronExpression, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executor, Guid? taskId = null, SchedulerJobDefinition? definition = null) {
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
				ct => ExecuteScheduledTaskWithScopeAsync(taskName, definition, ct, executor));
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

	private async Task ExecuteScheduledTaskWithScopeAsync(string taskName, SchedulerJobDefinition? definition, CancellationToken cancellationToken, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executeAsync) {
		using var scope = _scopeFactory.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();

		if (definition != null) {
			var configDb = new SchedulerJobConfigDb(db);
			var isEnabled = configDb.GetEnabled(definition.TaskId) ?? definition.DefaultEnabled;
			if (!isEnabled) {
				_logger.LogInformation("実行フラグがfalseのため自動実行をスキップしました。 TaskName={TaskName}, JobKey={JobKey}", taskName, definition.JobKey);
				return;
			}
		}

		await ExecuteWithAutoexecHistoryAsync(db, taskName, cancellationToken, executeAsync);
	}

	private async Task<AutoexecTaskResult> ExecuteTaskCoreAsync(ExDatabase db, AddSchedulerTaskRequest request, CancellationToken cancellationToken) {
		return request.TaskType switch {
			SchedulerTaskType.LogOnly => await ExecuteLogOnlyAsync(request, cancellationToken),
			SchedulerTaskType.RunSummary => await ExecuteRunSummaryAsync(db, request, cancellationToken),
			SchedulerTaskType.MasterShohinMeishoRebuild => await ExecuteMasterShohinMeishoRebuildCoreAsync(db, request.TaskName, cancellationToken),
			SchedulerTaskType.MasterVColumnResync => await ExecuteMasterVColumnResyncCoreAsync(db, request.TaskName, cancellationToken),
			SchedulerTaskType.TranTaxRebuild => await ExecuteTranTaxRebuildCoreAsync(db, request.TaskName, cancellationToken),
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
			var param = new CalcDateTermParameter(yyyymm, yyyymm);
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

	/// <summary>
	/// 在庫・売掛・買掛を、前月分・当月分の順で再集計する。
	/// 区分（在庫/売掛/買掛）は互いに独立で、ある区分がエラーになってもその区分だけ中断し、残りの区分は実行する。
	/// </summary>
	private async Task<AutoexecTaskResult> ExecuteMonthlyResummaryCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		var now = DateTime.Now;
		var summaryDb = new SummaryDb(db);
		var shime = summaryDb.GetOwnClosingDay();
		var currentKakeMonth = ClosingMonthCalculator.CalculateKakeMonth(now, shime);
		string[] months = [ClosingMonthCalculator.AddMonths(currentKakeMonth, -1), currentKakeMonth];
		ResummaryGroup[] groups = [
			new("在庫", summaryDb.SummaryAllAsyncStream),
			new("売掛", summaryDb.SummaryUriKakeAsyncStream),
			new("買掛", summaryDb.SummaryKaiKakeAsyncStream),
		];

		_logger.LogInformation(
			"再集計開始: TaskName={TaskName}, Months={Months}",
			taskName,
			string.Join(",", months));

		var totalCount = 0;
		var returnCode = Success;
		var memos = new List<string>();

		foreach (var group in groups) {
			cancellationToken.ThrowIfCancellationRequested();

			var groupCount = 0;
			string? groupError = null;
			foreach (var yyyymm in months) {
				var param = new CalcDateTermParameter(yyyymm, yyyymm);
				var result = await RunSummaryStreamAsync(group.CreateStream(param), taskName, group.Label, yyyymm, cancellationToken);
				groupCount += result.Count;
				if (result.ErrorMessage != null) {
					// 同一区分は以降の月を実行せず中断し、次の区分へ進む
					groupError = $"{yyyymm}: {result.ErrorMessage}";
					break;
				}
			}

			totalCount += groupCount;
			if (groupError == null) {
				memos.Add($"{group.Label}=OK({groupCount})");
			}
			else {
				returnCode = InternalError;
				memos.Add($"{group.Label}=NG({groupError})");
			}
		}

		var memo = $"再集計({string.Join(",", months)}): {string.Join(", ", memos)}";
		_logger.LogInformation("再集計完了: TaskName={TaskName}, ReturnCode={ReturnCode}, Memo={Memo}", taskName, returnCode, memo);
		return new AutoexecTaskResult(returnCode, totalCount, memo);
	}

	/// <summary>
	/// 集計ストリームを実行し、エラーを検出した時点で以降のステップを打ち切る
	/// </summary>
	private async Task<SummaryStreamResult> RunSummaryStreamAsync(IAsyncEnumerable<StreamStepProgress> stream, string taskName, string label, string yyyymm, CancellationToken cancellationToken) {
		var count = 0;
		try {
			await foreach (var step in stream.WithCancellation(cancellationToken)) {
				if (step.IsError) {
					_logger.LogError(
						"再集計エラー: TaskName={TaskName}, 区分={Label}, yyyymm={Yyyymm}, Step={Step}, Error={Error}",
						taskName, label, yyyymm, step.StepName, step.ErrorMessage);
					return new SummaryStreamResult(count, $"Step={step.StepName}, Error={step.ErrorMessage}");
				}
				if (step.IsCompleted) {
					_logger.LogInformation(
						"再集計完了: TaskName={TaskName}, 区分={Label}, yyyymm={Yyyymm}, Count={Count}, Duration={Duration}",
						taskName, label, yyyymm, count, step.ErrorMessage);
					continue;
				}
				count += step.Count;
				_logger.LogInformation(
					"再集計進捗: 区分={Label}, yyyymm={Yyyymm}, Step={Step}, Progress={Progress}, Count={Count}",
					label, yyyymm, step.StepName, step.Progress, step.Count);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "再集計中に例外が発生しました: TaskName={TaskName}, 区分={Label}, yyyymm={Yyyymm}", taskName, label, yyyymm);
			return new SummaryStreamResult(count, $"例外: {ex.Message}");
		}
		return new SummaryStreamResult(count, null);
	}

	/// <summary>
	/// 期限切れの適用上代を削除する。伝票は消さないので、必要になれば再展開で復元できる。
	/// </summary>
	private Task<AutoexecTaskResult> ExecuteJodaiPurgeCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		try {
			var jodaiDb = new JodaiDb(db);
			var keepDays = jodaiDb.GetKeepDays();
			var deleted = jodaiDb.PurgeExpiredByConfig(DateTime.Today);
			var memo = $"適用上代の期限切れ削除: 保持日数={keepDays}, 削除={deleted}";
			_logger.LogInformation(
				"適用上代の期限切れ削除: TaskName={TaskName}, KeepDays={KeepDays}, Deleted={Deleted}",
				taskName, keepDays, deleted);
			return Task.FromResult(new AutoexecTaskResult(Success, deleted, memo));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "適用上代の期限切れ削除に失敗しました: TaskName={TaskName}", taskName);
			return Task.FromResult(new AutoexecTaskResult(InternalError, 0, $"例外: {ex.Message}"));
		}
	}

	/// <summary>
	/// 商品名称マスタを再構築する(<see cref="MasterShohin"/>のId_Col/Id_Sizが0のデータから名称マスタを再構築)。
	/// 全件走査の重い処理のため、既定では無効(IsEnabled=false)・起動間隔の下限チェック対象。
	/// </summary>
	private Task<AutoexecTaskResult> ExecuteMasterShohinMeishoRebuildCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		try {
			var updated = new RebuildDb(db).RebuildMasterShohin2Meisho();
			var memo = $"商品名称マスタ再構築: 更新件数={updated}";
			_logger.LogInformation("商品名称マスタ再構築完了: TaskName={TaskName}, Updated={Updated}", taskName, updated);
			return Task.FromResult(new AutoexecTaskResult(Success, updated, memo));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "商品名称マスタ再構築に失敗しました: TaskName={TaskName}", taskName);
			return Task.FromResult(new AutoexecTaskResult(InternalError, 0, $"例外: {ex.Message}"));
		}
	}

	/// <summary>
	/// マスタ名称の複製列(V*列)とJSON内の名称スナップショットを、参照先マスタの現在値で再同期する。
	/// 全マスタを対象にSerializableトランザクションで実行する重い処理のため、既定では無効(IsEnabled=false)・起動間隔の下限チェック対象。
	/// </summary>
	private Task<AutoexecTaskResult> ExecuteMasterVColumnResyncCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		var errors = new List<string>();
		try {
			db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var updated = new MasterCascadeDb(db).ResyncAll(errors);
			db.CompleteTransaction();

			var returnCode = errors.Count > 0 ? InternalError : Success;
			var memo = errors.Count > 0
				? $"V*列再同期: 更新行数={updated}, 失敗ルール数={errors.Count}"
				: $"V*列再同期: 更新行数={updated}";
			if (errors.Count > 0) {
				_logger.LogError("V*列再同期で一部ルールが失敗しました: TaskName={TaskName}, Updated={Updated}, ErrorCount={ErrorCount}", taskName, updated, errors.Count);
			}
			else {
				_logger.LogInformation("V*列再同期完了: TaskName={TaskName}, Updated={Updated}", taskName, updated);
			}
			return Task.FromResult(new AutoexecTaskResult(returnCode, updated, memo));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			db.AbortTransaction();
			throw;
		}
		catch (Exception ex) {
			db.AbortTransaction();
			_logger.LogError(ex, "V*列再同期に失敗しました: TaskName={TaskName}", taskName);
			return Task.FromResult(new AutoexecTaskResult(InternalError, 0, $"例外: {ex.Message}"));
		}
	}

	/// <summary>
	/// 対象6伝票の期首日以降を、取引先マスタの現在の消費税設定(TaxCalcUnit/TaxRounding・明細別Id_Tax)で再計算する。
	/// 全件走査・Serializableトランザクションの重い処理のため、既定では無効(IsEnabled=false)・起動間隔の下限チェック対象。
	/// 部分更新を残さないため、例外時は必ずロールバックする。
	/// </summary>
	private Task<AutoexecTaskResult> ExecuteTranTaxRebuildCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		var startTime = DateTime.Now;
		try {
			db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var results = new TranTaxRebuildDb(db).RebuildAll();
			db.CompleteTransaction();

			var totalUpdated = 0;
			foreach (var r in results) {
				totalUpdated += r.Updated;
			}
			var memo = TranTaxRebuildDb.BuildSummary(startTime, results);
			_logger.LogInformation("伝票税額再更新完了: TaskName={TaskName}, Updated={Updated}", taskName, totalUpdated);
			return Task.FromResult(new AutoexecTaskResult(Success, totalUpdated, memo));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			db.AbortTransaction();
			throw;
		}
		catch (Exception ex) {
			db.AbortTransaction();
			_logger.LogError(ex, "伝票税額再更新に失敗しました: TaskName={TaskName}", taskName);
			return Task.FromResult(new AutoexecTaskResult(InternalError, 0, $"例外: {ex.Message}"));
		}
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
					if (entryInfo is FileInfo fileInfo && (latestFileTime > threshold.AddDays(-1))) {
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
		return PrintServerPathResolver.Resolve(_configuration, _env).OutputDir;
	}

	public static Dictionary<string, object> ExecuteSqliteWalCheckpoint(ExDatabase db) {
		var result = db.RawExecCmd(SqliteOptimizeSql);
		Helpers.EnsureRawExecSucceeded(result, SqliteOptimizeSql);
		result = db.RawExecCmd(SqliteWalCheckpointSql);
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

		public static double ToHistoryElapsedSeconds(TimeSpan elapsedTime) {
			if (elapsedTime.TotalSeconds <= 0) {
				return 0;
			}
			if (elapsedTime.TotalSeconds >= double.MaxValue) {
				return double.MaxValue;
			}
			return elapsedTime.TotalSeconds;
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
