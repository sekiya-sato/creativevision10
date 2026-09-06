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
	/// <summary>自動実行履歴のMemoへメール送信結果を追記するときの区切り。</summary>
	private const string AutoexecMemoSeparator = " / ";
	/// <summary>自動実行履歴のMemoへ追記するメール送信結果の見出し。</summary>
	private const string AutoexecMailMemoPrefix = "メール:";
	private const int WorkFileCleanupTargetAgeHours = 2;
	private const int AutoExecMailTimeoutSeconds = 30;
	/// <summary>起動間隔の下限チェックを行うかどうかの内部フラグ（重い処理の連続実行を防ぐ）</summary>
	public const bool MinIntervalCheckEnabled = true;
	/// <summary>起動間隔の下限（分）。重い処理はこれより短い間隔では設定させない</summary>
	public const int MinIntervalMinutes = 60;
	/// <summary>起動間隔算出のための発生列挙回数の上限（暴走防止。毎分実行cronは31日で44640件になる）</summary>
	private const int MaxIntervalOccurrenceCount = 20000;
	/// <summary>起動間隔算出の対象期間（日数）</summary>
	private const int IntervalLookaheadDays = 31;
	/// <summary>予定時刻に対して遅れて起動した場合に許容する猶予(秒)</summary>
	private const int ScheduleWindowLateSeconds = 30;
	/// <summary>タイマ誤差で予定時刻より早く起動した場合に許容する猶予(秒)</summary>
	private const int ScheduleWindowEarlySeconds = 1;

	/// <summary>
	/// 以下の cron式・タスク名・TaskId・既定実行フラグの値の出典は <see cref="MasterConfig"/>（CvBase）側の定数であり、
	/// ここにある同名の公開定数は外部参照（テスト等）向けの互換のための別名である。
	/// </summary>
	public const string DailyWalCheckpointCronExpression = MasterConfig.AutoExecCronWalCheckpoint;
	public const string DailyWalCheckpointTaskName = MasterConfig.AutoExecTaskNameWalCheckpoint;
	public const string WorkFileCleanupCronExpression = MasterConfig.AutoExecCronWorkFileCleanup;
	public const string WorkFileCleanupTaskName = MasterConfig.AutoExecTaskNameWorkFileCleanup;
	public const string MonthlyResummaryCronExpression = MasterConfig.AutoExecCronMonthlyResummary;
	public const string MonthlyResummaryTaskName = MasterConfig.AutoExecTaskNameMonthlyResummary;
	public const string JodaiPurgeCronExpression = MasterConfig.AutoExecCronJodaiPurge;
	public const string JodaiPurgeTaskName = MasterConfig.AutoExecTaskNameJodaiPurge;
	public const string MasterShohinMeishoRebuildCronExpression = MasterConfig.AutoExecCronMasterShohinMeishoRebuild;
	public const string MasterShohinMeishoRebuildTaskName = MasterConfig.AutoExecTaskNameMasterShohinMeishoRebuild;
	public const string MasterVColumnResyncCronExpression = MasterConfig.AutoExecCronMasterVColumnResync;
	public const string MasterVColumnResyncTaskName = MasterConfig.AutoExecTaskNameMasterVColumnResync;
	public const string TranTaxRebuildCronExpression = MasterConfig.AutoExecCronTranTaxRebuild;
	public const string TranTaxRebuildTaskName = MasterConfig.AutoExecTaskNameTranTaxRebuild;
	public const string ManualLockMonitorCronExpression = MasterConfig.AutoExecCronManualLockMonitor;
	public const string ManualLockMonitorTaskName = MasterConfig.AutoExecTaskNameManualLockMonitor;

	public static readonly Guid DailyWalCheckpointTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdWalCheckpoint);
	public static readonly Guid WorkFileCleanupTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdWorkFileCleanup);
	public static readonly Guid MonthlyResummaryTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdMonthlyResummary);
	public static readonly Guid JodaiPurgeTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdJodaiPurge);
	public static readonly Guid MasterShohinMeishoRebuildTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdMasterShohinMeishoRebuild);
	public static readonly Guid MasterVColumnResyncTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdMasterVColumnResync);
	public static readonly Guid TranTaxRebuildTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdTranTaxRebuild);
	public static readonly Guid ManualLockMonitorTaskId = Guid.Parse(MasterConfig.AutoExecTaskIdManualLockMonitor);

	/// <summary>ジョブを識別するキー（<see cref="MasterConfig"/> の Name に使う固定文字列）</summary>
	public const string JobKeyWalCheckpoint = "WalCheckpoint";
	public const string JobKeyWorkFileCleanup = "WorkFileCleanup";
	public const string JobKeyMonthlyResummary = "MonthlyResummary";
	public const string JobKeyJodaiPurge = "JodaiPurge";
	public const string JobKeyMasterShohinMeishoRebuild = "MasterShohinMeishoRebuild";
	public const string JobKeyMasterVColumnResync = "MasterVColumnResync";
	public const string JobKeyTranTaxRebuild = "TranTaxRebuild";
	public const string JobKeyManualLockMonitor = "ManualLockMonitor";

	/// <summary>システムジョブ1件の定義（TaskId・設定キー・名称・既定cron・既定の実行フラグ・起動間隔チェックの有無）</summary>
	public sealed record SchedulerJobDefinition(
		Guid TaskId,
		string JobKey,
		string TaskName,
		string DefaultCronExpression,
		bool DefaultEnabled,
		bool DefaultIsSendMail,
		bool CheckMinInterval);

	/// <summary>
	/// サーバが自動登録するシステムジョブの一覧。
	/// 実行フラグ・cron式の永続値は <see cref="SchedulerJobConfigDb"/> 経由で参照し、未設定ならここの既定値を使う。
	/// </summary>
	public static readonly IReadOnlyList<SchedulerJobDefinition> SystemJobDefinitions = [
		new(DailyWalCheckpointTaskId, JobKeyWalCheckpoint, DailyWalCheckpointTaskName, DailyWalCheckpointCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledWalCheckpoint), IsSendMailDefault(DailyWalCheckpointTaskId), false),
		new(WorkFileCleanupTaskId, JobKeyWorkFileCleanup, WorkFileCleanupTaskName, WorkFileCleanupCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledWorkFileCleanup), IsSendMailDefault(WorkFileCleanupTaskId), false),
		new(MonthlyResummaryTaskId, JobKeyMonthlyResummary, MonthlyResummaryTaskName, MonthlyResummaryCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledMonthlyResummary), IsSendMailDefault(MonthlyResummaryTaskId), false),
		new(JodaiPurgeTaskId, JobKeyJodaiPurge, JodaiPurgeTaskName, JodaiPurgeCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledJodaiPurge), IsSendMailDefault(JodaiPurgeTaskId), false),
		new(MasterShohinMeishoRebuildTaskId, JobKeyMasterShohinMeishoRebuild, MasterShohinMeishoRebuildTaskName, MasterShohinMeishoRebuildCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledMasterShohinMeishoRebuild), IsSendMailDefault(MasterShohinMeishoRebuildTaskId), true),
		new(MasterVColumnResyncTaskId, JobKeyMasterVColumnResync, MasterVColumnResyncTaskName, MasterVColumnResyncCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledMasterVColumnResync), IsSendMailDefault(MasterVColumnResyncTaskId), true),
		new(TranTaxRebuildTaskId, JobKeyTranTaxRebuild, TranTaxRebuildTaskName, TranTaxRebuildCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledTranTaxRebuild), IsSendMailDefault(TranTaxRebuildTaskId), true),
		// CheckMinInterval は必ずfalse: 監視タスクは5分毎cronであり、MinIntervalMinutes(60分)の下限チェック対象にすると弾かれてしまう。
		new(ManualLockMonitorTaskId, JobKeyManualLockMonitor, ManualLockMonitorTaskName, ManualLockMonitorCronExpression, IsEnabledDefault(MasterConfig.AutoExecEnabledManualLockMonitor), IsSendMailDefault(ManualLockMonitorTaskId), false),
	];

	/// <summary>MasterConfigの実行フラグ値(1/0)を bool に変換する</summary>
	private static bool IsEnabledDefault(string value) => value == MasterConfig.ValAutoExecEnabled;
	private static bool IsSendMailDefault(Guid taskId) => IsEnabledDefault(
		MasterConfig.AutoExecJobDefaults.Single(definition => Guid.Parse(definition.TaskId) == taskId).IsSendMail);

	private readonly ILogger<SchedulerService> _logger;
	private readonly NCrontab.Scheduler.IScheduler _scheduler;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private static readonly TimeSpan WorkFileCleanupTargetAge = TimeSpan.FromHours(WorkFileCleanupTargetAgeHours);

	/// <summary>
	/// マニュアル排他制御監視タスク（設計書§3）が前回チェック時点で保持する状態。
	/// 設計書§3は「タスクは静的変数に保持する」と定めるが、static fieldにすると単体テストで
	/// 状態が漏れる（<c>CvDomainLogic.ManualLockMonitor</c>のクラスコメント参照）。
	/// <see cref="SchedulerService"/>はDIでシングルトン登録されている（<c>CvServer/Program.cs</c>）ため、
	/// このインスタンスフィールドはアプリ実行中は事実上の静的変数と同じ役割を果たす。
	/// </summary>
	private CvDomainLogic.ManualLockMonitorState? _manualLockMonitorState;
	/// <summary>前回状態の読み書きを直列化するための排他オブジェクト。</summary>
	private readonly object _manualLockMonitorGate = new();

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
		if (result.Result == Success && request.IsSendMail && Guid.TryParse(result.TaskId, out var addedTaskId)) {
			// 登録に成功してからフラグを永続化する。フラグの保存に失敗してもタスク登録自体は成功として返す。
			try {
				using var scope = _scopeFactory.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
				new SchedulerJobConfigDb(db).SetIsSendMail(addedTaskId, true);
			}
			catch (Exception ex) {
				_logger.LogError(ex, "追加タスクのメール送信フラグ保存に失敗しました。 TaskId={TaskId}", addedTaskId);
			}
		}
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

		// 動的追加タスクは再起動で消えるため、残ったメール送信フラグ行も片付ける。
		// システムジョブは再登録されるので設定を残す。
		if (FindDefinitionByTaskId(guid) == null) {
			try {
				using var scope = _scopeFactory.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
				new SchedulerJobConfigDb(db).RemoveIsSendMail(guid);
			}
			catch (Exception ex) {
				_logger.LogWarning(ex, "削除タスクのメール送信フラグ削除に失敗しました。 TaskId={TaskId}", guid);
			}
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

	/// <summary>
	/// マニュアル排他制御の監視タスク（設計書§3、Step 9-4）を登録する。5分毎に実行する。
	/// <para>
	/// <b>汎用の自動実行履歴ラッパーを使わない理由（報告事項）</b>: 他の6タスクと同様に
	/// <see cref="ExecuteWithAutoexecHistoryAsync"/>を使うと、実行の都度（5分毎に）
	/// 「処理開始」/「処理完了」の<see cref="SysHistAutoexec"/>行が1件増えてしまう。
	/// 設計書§3.1（2a: 行が無ければ何もしない・ログも出さない）と§3.3（2c: Vduが前進していればログを出さない）は
	/// 「該当ティックでは1行も増えない」ことを要求しており、また§3.7は
	/// 「ログは必ず2b→2f/2eで対になる」という不変条件を課している。汎用ラッパーの行が
	/// 挟まるとどちらも満たせなくなるため、このタスクだけ<c>suppressAutoexecHistory: true</c>で登録し、
	/// <see cref="SysHistAutoexec"/>への書き込みはドメインロジック（<see cref="ManualLockDb.RecordMonitorDetected"/>等）が
	/// 判定（§3.2/§3.5/§3.6）に応じて行う。
	/// </para>
	/// </summary>
	public SchedulerResult RegisterManualLockMonitorTask() {
		var def = FindDefinition(JobKeyManualLockMonitor);
		return RegisterSystemJob(def, (db, ct) => ExecuteManualLockMonitorCoreAsync(db, def.TaskName, ct), suppressAutoexecHistory: true);
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
				var isSendMail = false;
				if (def == null) {
					// 動的追加タスクは実行フラグを持たない。メール送信フラグだけ永続値を見る。
					isEnabled = true;
					try {
						isSendMail = configDb?.GetIsSendMail(task.Id) ?? false;
					}
					catch (Exception ex) {
						_logger.LogWarning(ex, "メール送信フラグの取得に失敗しました。 TaskId={TaskId}", task.Id);
					}
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
					try {
						isSendMail = configDb?.GetIsSendMail(def.TaskId) ?? def.DefaultIsSendMail;
					}
					catch (Exception ex) {
						_logger.LogWarning(ex, "メール送信フラグの取得に失敗しました。 JobKey={JobKey}", def.JobKey);
					}
				}

				result.Tasks.Add(new SchedulerTaskInfo {
					TaskId = task.Id.ToString(),
					TaskName = task.Name ?? string.Empty,
					CronExpression = task.CrontabSchedule.ToString(),
					NextOccurrence = task.CrontabSchedule.GetNextOccurrence(DateTime.Now),
					IsSystemTask = IsSystemTask(task.Name),
					IsEnabled = isEnabled,
					IsSendMail = isSendMail,
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

	/// <summary>
	/// スケジュールタスクの実行結果メールを送信する/しないフラグを設定する。
	/// システムジョブと、スケジューラに登録済みの動的追加タスクの両方を対象にする。
	/// </summary>
	public Task<SchedulerResult> SetTaskSendMailAsync(SetSchedulerTaskSendMailRequest request, CallContext context = default) {
		if (!Guid.TryParse(request.TaskId, out var guid)) {
			return Task.FromResult(new SchedulerResult { Result = InvalidTaskId, Detail = $"TaskId が不正です: {request.TaskId}", TaskId = request.TaskId });
		}

		var def = FindDefinitionByTaskId(guid);
		if (def == null && !IsRegisteredTask(guid)) {
			return Task.FromResult(new SchedulerResult { Result = TaskNotFound, Detail = $"対象タスクが存在しません: {request.TaskId}", TaskId = request.TaskId });
		}

		try {
			using var scope = _scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
			new SchedulerJobConfigDb(db).SetIsSendMail(guid, request.IsSendMail);
			_logger.LogInformation("メール送信フラグ設定: JobKey={JobKey}, TaskId={TaskId}, IsSendMail={IsSendMail}", def?.JobKey ?? "(動的追加)", guid, request.IsSendMail);

			return Task.FromResult(new SchedulerResult { Result = Success, Detail = "正常終了", TaskId = guid.ToString() });
		}
		catch (Exception ex) {
			_logger.LogError(ex, "メール送信フラグ設定に失敗しました。 TaskId={TaskId}", guid);
			return Task.FromResult(new SchedulerResult { Result = InternalError, Detail = "メール送信フラグ設定に失敗しました。", TaskId = request.TaskId });
		}
	}

	/// <summary>指定 TaskId がスケジューラに登録されているかを返す。</summary>
	private bool IsRegisteredTask(Guid taskId) {
		try {
			return _scheduler.GetTasks().Any(task => task.Id == taskId);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "スケジュールタスクの存在確認に失敗しました。 TaskId={TaskId}", taskId);
			return false;
		}
	}

	/// <summary>
	/// 自動実行結果メールの設定を取得する。パスワードは返さず、登録済みかどうかだけを返す。
	/// </summary>
	public Task<GetAutoExecMailConfigResponse> GetAutoExecMailConfigAsync(CallContext context = default) {
		try {
			using var scope = _scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
			var values = new AutoExecMailConfigStore(db).Read();
			var loadResult = new AutoExecMailSettingsLoader(db).Load();

			return Task.FromResult(new GetAutoExecMailConfigResponse {
				Result = Success,
				Detail = "正常終了",
				Config = ToContract(values),
				HasCredential = values.HasCredential,
				IsValid = loadResult.IsValid,
				ValidationDetail = loadResult.IsValid
					? "この設定でメールを送信できます。"
					: AutoExecMailResultText.Describe(loadResult.Failure, loadResult.FailureSettingName),
				SecurityValues = [.. AutoExecMailSettingsLoader.SecurityValues],
				AuthModeValues = [.. AutoExecMailSettingsLoader.AuthModeValues],
			});
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行結果メール設定の取得に失敗しました。");
			return Task.FromResult(new GetAutoExecMailConfigResponse {
				Result = InternalError,
				Detail = "自動実行結果メール設定の取得に失敗しました。",
			});
		}
	}

	/// <summary>
	/// 自動実行結果メールの設定を保存する。空欄は未入力として保存し、形式が誤っている値だけを拒否する。
	/// </summary>
	public Task<SchedulerResult> SetAutoExecMailConfigAsync(SetAutoExecMailConfigRequest request, CallContext context = default) {
		if (request?.Config == null) {
			return Task.FromResult(new SchedulerResult { Result = InvalidRequest, Detail = "設定内容が指定されていません。" });
		}

		var values = ToValues(request.Config);
		var validation = AutoExecMailConfigStore.Validate(values);
		if (!validation.IsValid) {
			return Task.FromResult(new SchedulerResult {
				Result = InvalidRequest,
				Detail = $"{validation.ErrorMessage}({validation.ErrorSettingName})",
			});
		}

		try {
			using var scope = _scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
			// 空文字は「変更しない」、消去指示があったときだけ空文字で上書きする。
			var credential = request.ClearCredential
				? string.Empty
				: string.IsNullOrEmpty(request.Credential) ? null : request.Credential;
			new AutoExecMailConfigStore(db).Write(values, credential);

			var loadResult = new AutoExecMailSettingsLoader(db).Load();
			_logger.LogInformation(
				"自動実行結果メール設定を保存しました。 Server={Server}, Port={Port}, Security={Security}, AuthMode={AuthMode}, 送信可否={IsValid}",
				values.Server, values.Port, values.Security, values.AuthMode, loadResult.IsValid);

			return Task.FromResult(new SchedulerResult {
				Result = Success,
				Detail = loadResult.IsValid
					? "保存しました。この設定でメールを送信できます。"
					: $"保存しました。ただしこのままでは送信できません。{AutoExecMailResultText.Describe(loadResult.Failure, loadResult.FailureSettingName)}",
			});
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行結果メール設定の保存に失敗しました。");
			return Task.FromResult(new SchedulerResult { Result = InternalError, Detail = "自動実行結果メール設定の保存に失敗しました。" });
		}
	}

	/// <summary>
	/// 保存済みの設定でテストメールを実際に送信する。設定不備・SMTPエラーはどちらも Detail で返す。
	/// </summary>
	public async Task<SchedulerResult> TestSendAutoExecMailAsync(CallContext context = default) {
		using var scope = _scopeFactory.CreateScope();
		var mailService = scope.ServiceProvider.GetService<IAutoExecMailService>();
		if (mailService == null) {
			_logger.LogError("自動実行結果メールサービスが登録されていないためテスト送信できません。");
			return new SchedulerResult { Result = InternalError, Detail = "メール送信サービスが登録されていません。" };
		}

		var message = new AutoExecMailMessage(
			"[CV10 自動実行] テスト送信",
			string.Join(Environment.NewLine, [
				"自動実行結果メールのテスト送信です。",
				$"送信日時: {DateTime.Now:yyyy/MM/dd HH:mm:ss}",
				"このメールが届いていれば、自動実行の結果メールも同じ設定で送信されます。",
			]));

		try {
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(AutoExecMailTimeoutSeconds));
			var sendResult = await mailService.SendAsync(message, timeoutCts.Token);
			if (!sendResult.Sent) {
				_logger.LogWarning(
					"テストメールを送信しませんでした。 設定不備={Failure}, 設定名={SettingName}",
					sendResult.SettingsFailure, sendResult.FailureSettingName);
				return new SchedulerResult {
					Result = InvalidRequest,
					Detail = $"テストメールを送信できません。{AutoExecMailResultText.Describe(sendResult.SettingsFailure, sendResult.FailureSettingName)}",
				};
			}

			_logger.LogInformation("テストメールを送信しました。");
			return new SchedulerResult { Result = Success, Detail = "テストメールを送信しました。受信を確認してください。" };
		}
		catch (OperationCanceledException ex) {
			_logger.LogWarning(ex, "テストメールの送信がタイムアウトしました。");
			return new SchedulerResult {
				Result = Canceled,
				Detail = $"テストメールの送信が{AutoExecMailTimeoutSeconds}秒でタイムアウトしました。サーバー名とポート番号を確認してください。",
			};
		}
		catch (Exception ex) {
			_logger.LogError(ex, "テストメールの送信に失敗しました。");
			return new SchedulerResult { Result = InternalError, Detail = $"テストメールの送信に失敗しました。{ex.Message}" };
		}
	}

	private static AutoExecMailConfig ToContract(AutoExecMailConfigValues values) => new() {
		Server = values.Server,
		Port = values.Port,
		Security = values.Security,
		AuthMode = values.AuthMode,
		UserId = values.UserId,
		FromAddress = values.FromAddress,
		FromName = values.FromName,
		ToAddress = values.ToAddress,
	};

	private static AutoExecMailConfigValues ToValues(AutoExecMailConfig config) => new(
		config.Server ?? string.Empty,
		config.Port ?? string.Empty,
		config.Security ?? string.Empty,
		config.AuthMode ?? string.Empty,
		config.UserId ?? string.Empty,
		config.FromAddress ?? string.Empty,
		config.FromName ?? string.Empty,
		config.ToAddress ?? string.Empty,
		false);

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
	/// 起動要求が cron の予定時刻に基づくものかを判定する。
	/// (now - late, now + early] の範囲に cron の発生時刻があれば予定内とみなす。
	/// cron は分単位のため正規の起動は必ず秒が00になる。予定外の割り込み起動を弾くために使う。
	/// </summary>
	public static bool IsWithinScheduleWindow(CrontabSchedule schedule, DateTime now, TimeSpan late, TimeSpan early) {
		ArgumentNullException.ThrowIfNull(schedule);
		return schedule.GetNextOccurrence(now - late) <= now + early;
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
	private SchedulerResult RegisterSystemJob(SchedulerJobDefinition definition, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executor, bool suppressAutoexecHistory = false) {
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

		return RegisterTask(definition.TaskName, cronExpression, executor, definition.TaskId, definition, suppressAutoexecHistory);
	}

	/// <summary>
	/// <paramref name="suppressAutoexecHistory"/>は<see cref="RegisterManualLockMonitorTask"/>専用。
	/// trueの場合、<see cref="ExecuteWithAutoexecHistoryAsync"/>（実行の都度<see cref="SysHistAutoexec"/>に
	/// 開始/終了行を書く汎用処理）を経由せず、実行フラグ判定後に<paramref name="executor"/>を直接呼ぶ。
	/// メール送信もこの経路では行わない（監視タスクは既定でメール送信フラグを持たない）。
	/// </summary>
	private SchedulerResult RegisterTask(string taskName, string cronExpression, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executor, Guid? taskId = null, SchedulerJobDefinition? definition = null, bool suppressAutoexecHistory = false) {
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
			var guid = taskId ?? Guid.NewGuid();
			var scheduledTask = new AsyncScheduledTask(
				guid,
				taskName,
				schedule,
				ct => ExecuteScheduledTaskWithScopeAsync(guid, taskName, definition, ct, executor, suppressAutoexecHistory));
			_scheduler.AddTask(scheduledTask);

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

	/// <summary>
	/// スケジューラからの起動要求が予定時刻によるものかを判定する。
	/// NCrontab.Scheduler 2.1.23 は AddTask/UpdateTask/RemoveTask で待機をキャンセルした際に
	/// 待機中のタスクをそのまま実行してしまうため、予定時刻外の割り込み起動をここで捨てる。
	/// 判定できない場合は実行する(fail-open)。ガードが原因で自動実行が止まる事故を避けるため。
	/// </summary>
	private bool IsScheduledInvocation(Guid taskId, string taskName) {
		CrontabSchedule? schedule;
		try {
			// 実行中スケジューラから現在の cron を引く。UpdateTask 直後でも最新の値が取れる
			schedule = _scheduler.GetTasks().FirstOrDefault(t => t.Id == taskId)?.CrontabSchedule;
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "予定時刻判定のためのスケジュール取得に失敗しました。 TaskName={TaskName}, TaskId={TaskId}", taskName, taskId);
			return true;
		}

		if (schedule == null) {
			return true;
		}

		var now = DateTime.Now;
		if (IsWithinScheduleWindow(schedule, now, TimeSpan.FromSeconds(ScheduleWindowLateSeconds), TimeSpan.FromSeconds(ScheduleWindowEarlySeconds))) {
			return true;
		}

		_logger.LogInformation(
			"予定時刻外の起動要求のためスキップしました。 TaskName={TaskName}, TaskId={TaskId}, Now={Now}, NextOccurrence={NextOccurrence}",
			taskName, taskId, now, schedule.GetNextOccurrence(now));
		return false;
	}

	private async Task ExecuteScheduledTaskWithScopeAsync(Guid taskId, string taskName, SchedulerJobDefinition? definition, CancellationToken cancellationToken, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executeAsync, bool suppressAutoexecHistory = false) {
		if (!IsScheduledInvocation(taskId, taskName)) {
			return;
		}
		using var scope = _scopeFactory.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ExDatabase>();
		var isSendMail = false;
		var configDb = new SchedulerJobConfigDb(db);

		if (definition != null) {
			var isEnabled = configDb.GetEnabled(definition.TaskId) ?? definition.DefaultEnabled;
			if (!isEnabled) {
				_logger.LogInformation("実行フラグがfalseのため自動実行をスキップしました。 TaskName={TaskName}, JobKey={JobKey}", taskName, definition.JobKey);
				return;
			}
		}

		if (suppressAutoexecHistory) {
			// SysHistAutoexecへの記録はexecuteAsync側（ドメインロジック）が判定に応じて行うため、
			// ここでは汎用の開始/終了ログを書かずにそのまま実行する（理由は RegisterManualLockMonitorTask 参照）。
			try {
				await executeAsync(db, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				throw;
			}
			catch (Exception ex) {
				_logger.LogError(ex, "自動実行に失敗しました（履歴記録は抑止対象）。 TaskName={TaskName}", taskName);
			}
			return;
		}

		try {
			// システムジョブは定義の既定値、動的追加タスクは既定で送信しない。
			isSendMail = configDb.GetIsSendMail(taskId) ?? definition?.DefaultIsSendMail ?? false;
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "自動実行時のメール送信フラグ取得に失敗しました。 TaskName={TaskName}, TaskId={TaskId}", taskName, taskId);
		}

		IAutoExecMailService? mailService = null;
		if (isSendMail) {
			try {
				mailService = scope.ServiceProvider.GetService<IAutoExecMailService>();
			}
			catch (Exception ex) {
				_logger.LogError(ex, "自動実行結果メールサービスの解決に失敗しました。 TaskName={TaskName}", taskName);
			}
			if (mailService == null) {
				_logger.LogWarning("自動実行結果メールサービスが登録されていないため送信しません。 TaskName={TaskName}", taskName);
			}
		}
		await ExecuteWithAutoexecHistoryAsync(db, taskName, cancellationToken, executeAsync, mailService);
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
				else if (step.Phase != StreamStepProgressPhase.Started) {
					// 開始通知は件数が未確定（常に0）なので、進捗ログと件数の採用対象から外す
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
				if (step.Phase == StreamStepProgressPhase.Started) {
					// 開始通知は件数が未確定（常に0）なので、ログ・件数集計とも対象外
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

	/// <summary>
	/// マニュアル排他制御の監視タスク本体（設計書§3、Step 9-4）。判定の純関数（<see cref="CvDomainLogic.ManualLockMonitor.Evaluate"/>）は
	/// CvDomainLogicに置き、ここでは「前回状態の読み書き（本来の静的変数の代わり）」と
	/// 「判定結果に応じたDB書き込み（<see cref="ManualLockDb.RecordMonitorDetected"/>等）」だけを行う薄い呼び出しにする。
	/// </summary>
	private Task<AutoexecTaskResult> ExecuteManualLockMonitorCoreAsync(ExDatabase db, string taskName, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();

		var lockDb = new ManualLockDb(db);
		var nowUtcTicks = DateTime.UtcNow.Ticks;

		CvDomainLogic.ManualLockMonitorTick tick;
		lock (_manualLockMonitorGate) {
			var activeLocks = lockDb.FetchActiveLocks();
			tick = CvDomainLogic.ManualLockMonitor.Evaluate(_manualLockMonitorState, activeLocks, nowUtcTicks);
			_manualLockMonitorState = tick.NextState;
		}

		switch (tick.Action) {
			case CvDomainLogic.ManualLockMonitorAction.RecordDetected:
				lockDb.RecordMonitorDetected(tick.Subject!, taskName);
				_logger.LogWarning(
					"マニュアル排他制御監視: 実行中の一連処理を検知しました。 TableName={TableName}, ColumnName={ColumnName}, SeqNo={SeqNo}",
					tick.Subject!.TableName, tick.Subject.ColumnName, tick.Subject.SeqNo);
				break;
			case CvDomainLogic.ManualLockMonitorAction.RecordTimeout:
				lockDb.RecordMonitorTimeout(tick.Subject!, taskName);
				_logger.LogError(
					"マニュアル排他制御監視: 長時間更新が無いため異常とみなし強制解放しました。 TableName={TableName}, ColumnName={ColumnName}, ExpectedDuration={ExpectedDuration}",
					tick.Subject!.TableName, tick.Subject.ColumnName, tick.Subject.ExpectedDuration);
				break;
			case CvDomainLogic.ManualLockMonitorAction.RecordNormalEnd:
				lockDb.RecordMonitorNormalEnd(tick.Subject!, taskName);
				_logger.LogInformation(
					"マニュアル排他制御監視: 一連処理の正常終了を検知しました。 TableName={TableName}",
					tick.Subject!.TableName);
				break;
			case CvDomainLogic.ManualLockMonitorAction.None:
			default:
				break;
		}

		// suppressAutoexecHistory:true で登録しているため、この戻り値自体はSysHistAutoexecへは書かれない。
		return Task.FromResult(new AutoexecTaskResult(Success, 0, "監視完了"));
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

	private async Task ExecuteWithAutoexecHistoryAsync(ExDatabase db, string taskName, CancellationToken cancellationToken, Func<ExDatabase, CancellationToken, Task<AutoexecTaskResult>> executeAsync, IAutoExecMailService? mailService) {
		var startTime = DateTime.Now;
		var stopwatch = Stopwatch.StartNew();
		var history = InsertAutoexecHistory(db, taskName, startTime);
		var result = new AutoexecTaskResult(Success, 0, "正常終了");
		Exception? caughtException = null;
		var historyUpdated = false;

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
			historyUpdated = UpdateAutoexecHistory(db, history, DateTime.Now, stopwatch.Elapsed, result);
		}

		if (historyUpdated && history != null && mailService != null) {
			await TrySendAutoexecMailAsync(db, mailService, history);
		}

		if (caughtException != null) {
			ExceptionDispatchInfo.Capture(caughtException).Throw();
		}
	}

	/// <summary>
	/// 自動実行結果メールを送信し、その成否を自動実行履歴のMemoへ追記する。
	/// <para>
	/// メール本文はこの追記より前の履歴内容から作るため、本文に「メール:...」は含まれない。
	/// メールの成否は履歴画面で確認する。
	/// </para>
	/// </summary>
	private async Task TrySendAutoexecMailAsync(ExDatabase db, IAutoExecMailService mailService, SysHistAutoexec history) {
		string mailMemo;
		try {
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(AutoExecMailTimeoutSeconds));
			var mailResult = await mailService.SendAsync(BuildAutoExecMailMessage(history), timeoutCts.Token);
			if (mailResult.Sent) {
				_logger.LogInformation("自動実行結果メールを送信しました。 HistoryId={HistoryId}, TaskName={TaskName}", history.Id, history.TaskName);
			}
			else {
				_logger.LogWarning(
					"自動実行結果メールを送信しませんでした。 HistoryId={HistoryId}, TaskName={TaskName}, 理由={Reason}, 設定不備={Failure}, 設定名={SettingName}",
					history.Id, history.TaskName, mailResult.NotSentReason, mailResult.SettingsFailure, mailResult.FailureSettingName);
			}
			mailMemo = $"{AutoexecMailMemoPrefix}{AutoExecMailResultText.Describe(mailResult)}";
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行結果メールの送信に失敗しました。 HistoryId={HistoryId}, TaskName={TaskName}", history.Id, history.TaskName);
			mailMemo = $"{AutoexecMailMemoPrefix}送信に失敗しました({ex.GetType().Name})";
		}

		AppendAutoexecHistoryMemo(db, history, mailMemo);
	}

	/// <summary>
	/// 自動実行履歴のMemoへ追記して更新する。Memoは <see cref="MaxAutoexecMemoLength"/> 文字までなので、
	/// 追記分の場所を確保するために元のMemoを切り詰める。
	/// </summary>
	public static string BuildAppendedAutoexecMemo(string? currentMemo, string appendText) {
		var suffix = AutoexecMemoSeparator + appendText;
		if (suffix.Length >= MaxAutoexecMemoLength) {
			return suffix[..MaxAutoexecMemoLength];
		}
		var baseMemo = currentMemo ?? string.Empty;
		var keepLength = MaxAutoexecMemoLength - suffix.Length;
		return (baseMemo.Length > keepLength ? baseMemo[..keepLength] : baseMemo) + suffix;
	}

	private void AppendAutoexecHistoryMemo(ExDatabase db, SysHistAutoexec history, string appendText) {
		try {
			history.Memo = BuildAppendedAutoexecMemo(history.Memo, appendText);
			history.Vdu = DateTime.Now.ToUniversalTime().Ticks;
			db.Update(history, ["Memo", "Vdu"]);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴へのメール送信結果の追記に失敗しました。 Id={Id}, TaskName={TaskName}", history.Id, history.TaskName);
		}
	}

	/// <summary>自動実行履歴の確定値からメール件名と本文を作成する。</summary>
	public static AutoExecMailMessage BuildAutoExecMailMessage(SysHistAutoexec history) {
		ArgumentNullException.ThrowIfNull(history);
		var resultText = history.ReturnCode == Success ? "正常終了" : "異常終了";
		var body = string.Join(Environment.NewLine, [
			"自動実行処理結果",
			$"タスク名: {history.TaskName}",
			$"開始日時: {history.StartTime}",
			$"終了日時: {history.EndTime}",
			$"経過時間(秒): {history.ElapsedTime:0.########}",
			$"戻り値: {history.ReturnCode}",
			$"処理件数: {history.Count}",
			$"内容: {history.Memo}",
		]);
		return new AutoExecMailMessage($"[CV10 自動実行] {history.TaskName} - {resultText}", body);
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

	private bool UpdateAutoexecHistory(ExDatabase db, SysHistAutoexec? history, DateTime endTime, TimeSpan elapsedTime, AutoexecTaskResult result) {
		if (history == null) {
			return false;
		}

		try {
			history.EndTime = Helpers.ToAutoexecDateTimeString(endTime);
			history.ElapsedTime = Helpers.ToHistoryElapsedSeconds(elapsedTime);
			history.ReturnCode = result.ReturnCode;
			history.Count = result.Count;
			history.Memo = Helpers.NormalizeAutoexecText(result.Memo, MaxAutoexecMemoLength, "処理完了");
			history.Vdu = DateTime.Now.ToUniversalTime().Ticks;

			db.Update(history, ["EndTime", "ElapsedTime", "ReturnCode", "Count", "Memo", "Vdu"]);
			return true;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "自動実行履歴の終了更新に失敗しました。 Id={Id}, TaskName={TaskName}", history.Id, history.TaskName);
			return false;
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
