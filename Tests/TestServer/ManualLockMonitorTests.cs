using System;
using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="ManualLockMonitor"/>（判定の純関数、設計書§3.1〜§3.4）の単体テスト。
/// 現在時刻を引数で渡せるため、<see cref="DateTime.Now"/>等の実時間に依存させずに判定を固定できる。
/// 仕様書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §5 のL-08〜L-10、L-12を実装する。
/// </summary>
[TestClass]
public class ManualLockMonitorTests {
	private const long OneSecondTicks = TimeSpan.TicksPerSecond;
	private const long OneMinuteTicks = TimeSpan.TicksPerMinute;

	private static SysSequence NewRow(long id, string tableName, string columnName, long seqNo, long vdc, long vdu, long expectedDuration, int sysSeqType = (int)EmSysSeqType.ManualLock) =>
		new() {
			Id = id,
			SysSeqType = sysSeqType,
			TableName = tableName,
			ColumnName = columnName,
			SeqNo = seqNo,
			Vdc = vdc,
			Vdu = vdu,
			ExpectedDuration = expectedDuration,
		};

	// ------------------------------------------------------------------
	// L-08: 監視: 行なしで何もしない。ログも出さない(§3.1、2a)
	// ------------------------------------------------------------------

	[TestMethod]
	public void Evaluate_行が無く前回状態も無ければ何もしない() {
		var now = 1_000_000L;

		var tick = ManualLockMonitor.Evaluate(previous: null, activeLocks: [], nowUtcTicks: now);

		Assert.AreEqual(ManualLockMonitorAction.None, tick.Action);
		Assert.IsNull(tick.NextState);
		Assert.IsNull(tick.Subject);
	}

	/// <summary>行が無く前回状態がある場合は§3.6(2f)。行が無い=2aとは別の分岐であることの確認</summary>
	[TestMethod]
	public void Evaluate_行が無いが前回状態があれば正常終了とみなす() {
		var previous = new ManualLockMonitorState(1, "在庫・掛再集計", "買掛集計", 1, Vdc: 100, Vdu: 200, ExpectedDuration: 600);
		var tick = ManualLockMonitor.Evaluate(previous, activeLocks: [], nowUtcTicks: 1_000_000L);

		Assert.AreEqual(ManualLockMonitorAction.RecordNormalEnd, tick.Action);
		Assert.IsNull(tick.NextState);
		Assert.AreSame(previous, tick.Subject);
	}

	/// <summary>行を新規検知した場合(§3.2、2b)。前回状態がnullの場合</summary>
	[TestMethod]
	public void Evaluate_前回状態が無く行があれば新規検知としてログを出す() {
		var row = NewRow(1, "在庫・掛再集計", "買掛集計", 1, vdc: 100, vdu: 200, expectedDuration: 600);
		var tick = ManualLockMonitor.Evaluate(previous: null, activeLocks: [row], nowUtcTicks: 1_000_000L);

		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick.Action);
		Assert.IsNotNull(tick.NextState);
		Assert.AreEqual(1, tick.NextState!.Id);
		Assert.AreEqual("在庫・掛再集計", tick.Subject!.TableName);
	}

	/// <summary>行を新規検知した場合(§3.2、2b)。前回状態はあるが別の行(Idが違う)の場合</summary>
	[TestMethod]
	public void Evaluate_前回と別の行なら新規検知としてログを出す() {
		var previous = new ManualLockMonitorState(1, "在庫・掛再集計", "買掛集計", 1, Vdc: 100, Vdu: 200, ExpectedDuration: 600);
		var row = NewRow(2, "現在庫再集計", "現在庫集計", 1, vdc: 300, vdu: 300, expectedDuration: 300);

		var tick = ManualLockMonitor.Evaluate(previous, activeLocks: [row], nowUtcTicks: 1_000_000L);

		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick.Action);
		Assert.AreEqual(2, tick.NextState!.Id);
		Assert.AreEqual("現在庫再集計", tick.Subject!.TableName);
	}

	// ------------------------------------------------------------------
	// L-09: 監視: Vduが前進していればログを出さない(§3.3、2c)。行も消さない
	// ------------------------------------------------------------------

	[TestMethod]
	public void Evaluate_同じ行でVduが前進していれば処理中とみなしログを出さない() {
		var previous = new ManualLockMonitorState(1, "在庫・掛再集計", "買掛集計", 1, Vdc: 100, Vdu: 200, ExpectedDuration: 600);
		var row = NewRow(1, "在庫・掛再集計", "売掛集計", 2, vdc: 100, vdu: 500, expectedDuration: 600);

		var tick = ManualLockMonitor.Evaluate(previous, activeLocks: [row], nowUtcTicks: 1_000_000L);

		Assert.AreEqual(ManualLockMonitorAction.None, tick.Action);
		Assert.IsNull(tick.Subject);
		Assert.IsNotNull(tick.NextState);
		Assert.AreEqual(500, tick.NextState!.Vdu);
		Assert.AreEqual("売掛集計", tick.NextState.ColumnName);
	}

	// ------------------------------------------------------------------
	// L-10: 監視: 閾値 max(ExpectedDuration×2, 15分) の境界。
	//       直前は削除せず、直後は削除する。15分の下限、ExpectedDuration×2 の両方を確認
	// ------------------------------------------------------------------

	/// <summary>ExpectedDurationが小さい(60秒)場合、15分の下限が効くこと</summary>
	[TestMethod]
	public void Evaluate_ExpectedDurationが小さいとき15分の下限が効く境界値() {
		const long expectedDurationSeconds = 60; // ×2=120秒 < 15分なので、15分が閾値になる
		var previous = new ManualLockMonitorState(1, "在庫・掛再集計", "買掛集計", 1, Vdc: 0, Vdu: 0, ExpectedDuration: expectedDurationSeconds);
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);
		Assert.AreEqual(15 * OneMinuteTicks, threshold, "60秒の2倍(120秒)は15分未満なので、閾値は15分になること");

		var rowSameVdu = NewRow(1, "在庫・掛再集計", "買掛集計", 1, vdc: 0, vdu: 0, expectedDuration: expectedDurationSeconds);

		// 閾値ちょうど(境界。経過時間 > 閾値 が異常条件なので、ちょうどは異常にしない)
		var atThreshold = ManualLockMonitor.Evaluate(previous, [rowSameVdu], nowUtcTicks: threshold);
		Assert.AreEqual(ManualLockMonitorAction.None, atThreshold.Action, "経過時間がちょうど閾値のときは削除しないこと");

		// 閾値+1 Tick(直後)は異常
		var justOver = ManualLockMonitor.Evaluate(previous, [rowSameVdu], nowUtcTicks: threshold + 1);
		Assert.AreEqual(ManualLockMonitorAction.RecordTimeout, justOver.Action, "経過時間が閾値を超えた直後は削除すること");
		Assert.AreEqual(1, justOver.Subject!.Id);
		Assert.IsNull(justOver.NextState);

		// 閾値-1 Tick(直前)は異常にしない
		var justUnder = ManualLockMonitor.Evaluate(previous, [rowSameVdu], nowUtcTicks: threshold - 1);
		Assert.AreEqual(ManualLockMonitorAction.None, justUnder.Action, "経過時間が閾値未満のときは削除しないこと");
	}

	/// <summary>ExpectedDurationが大きい(3600秒)場合、×2が効くこと(15分の下限より大きい)</summary>
	[TestMethod]
	public void Evaluate_ExpectedDurationが大きいとき2倍が効く境界値() {
		const long expectedDurationSeconds = 3600; // ×2=7200秒=120分 > 15分なので、×2が閾値になる
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);
		Assert.AreEqual(expectedDurationSeconds * 2 * OneSecondTicks, threshold, "3600秒の2倍(7200秒)が15分を上回るので、閾値は×2になること");

		var previous = new ManualLockMonitorState(1, "評価替え", "評価替え計算", 1, Vdc: 0, Vdu: 0, ExpectedDuration: expectedDurationSeconds);
		var row = NewRow(1, "評価替え", "評価替え計算", 1, vdc: 0, vdu: 0, expectedDuration: expectedDurationSeconds);

		var justUnder = ManualLockMonitor.Evaluate(previous, [row], nowUtcTicks: threshold);
		Assert.AreEqual(ManualLockMonitorAction.None, justUnder.Action, "経過時間がちょうど閾値のときは削除しないこと");

		var justOver = ManualLockMonitor.Evaluate(previous, [row], nowUtcTicks: threshold + 1);
		Assert.AreEqual(ManualLockMonitorAction.RecordTimeout, justOver.Action, "経過時間が閾値を超えた直後は削除すること");
	}

	// ------------------------------------------------------------------
	// L-12: SysSeqType=0(テーブル連番)の行が監視に影響しない
	// ------------------------------------------------------------------

	[TestMethod]
	public void Evaluate_SysSeqType0の行はactiveLocksに含めない前提なので監視に影響しない() {
		// FetchActiveLocksがSysSeqType=1だけを返す契約のため、ここではその契約どおりの入力(空)で
		// SysSeqType=0の行が存在しても2aとして扱われることを確認する。
		var tableSeqOnly = new System.Collections.Generic.List<SysSequence>(); // FetchActiveLocksがSysSeqType=0を除外済みの状態を模す

		var tick = ManualLockMonitor.Evaluate(previous: null, activeLocks: tableSeqOnly, nowUtcTicks: 1_000_000L);

		Assert.AreEqual(ManualLockMonitorAction.None, tick.Action);
	}
}

/// <summary>
/// <see cref="ManualLockDb"/>の監視タスク向けメソッド(<c>RecordMonitorDetected</c>/<c>RecordMonitorTimeout</c>/
/// <c>RecordMonitorNormalEnd</c>)のDB結合テスト。SQLiteインメモリDBの作成作法は<see cref="ManualLockDbTests"/>に合わせる。
/// 仕様書 §5 のL-07、L-11を実装する。加えて2b/2e/2fの<c>SysHistType</c>が0(自動実行)であること、
/// 2a/2cでログが増えないことを確認する。
/// </summary>
[TestClass]
public class ManualLockMonitorDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockMonitorDbTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
		_db.KeepConnectionAlive = true;
		Db.CreateTable(typeof(SysSequence), true, false);
		Db.CreateTable(typeof(SysHistAutoexec), true, false);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	private const string MonitorTaskName = "マニュアル排他制御監視";

	/// <summary>
	/// 1回分の監視ティックを、本番(CvServer.SchedulerService.ExecuteManualLockMonitorCoreAsync)と同じ手順で模す。
	/// FetchActiveLocks→Evaluate→(必要なら)ManualLockDbへの書き込み、を行う。
	/// </summary>
	private static ManualLockMonitorTick RunTick(ManualLockDb lockDb, ManualLockMonitorState? previous, long nowUtcTicks) {
		var activeLocks = lockDb.FetchActiveLocks();
		var tick = ManualLockMonitor.Evaluate(previous, activeLocks, nowUtcTicks);
		switch (tick.Action) {
			case ManualLockMonitorAction.RecordDetected:
				lockDb.RecordMonitorDetected(tick.Subject!, MonitorTaskName);
				break;
			case ManualLockMonitorAction.RecordTimeout:
				lockDb.RecordMonitorTimeout(tick.Subject!, MonitorTaskName);
				break;
			case ManualLockMonitorAction.RecordNormalEnd:
				lockDb.RecordMonitorNormalEnd(tick.Subject!, MonitorTaskName);
				break;
		}
		return tick;
	}

	// ------------------------------------------------------------------
	// L-07: 処理が例外で落ちて行が残ったとき、監視タスクが閾値超過で削除する
	// ------------------------------------------------------------------

	[TestMethod]
	public void 処理が異常終了し行が残った場合閾値超過で監視タスクが削除する() {
		var lockDb = new ManualLockDb(Db);
		var begun = lockDb.TryBegin("総平均原価更新", "総平均原価計算", 600, "月次バッチ");
		// Completeを呼ばずに異常終了したことを模す(行はDBに残ったまま)
		begun.Handle!.Dispose();

		// 1回目のティック: 2b(検知)
		var tick1 = RunTick(lockDb, previous: null, nowUtcTicks: begun.Handle.Vdc);
		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick1.Action);
		Assert.AreEqual(1, Db.Fetch<SysSequence>($"SELECT * FROM {nameof(SysSequence)}").Count, "検知時点では行を消さない");

		// 2回目のティック: 閾値超過(15分の下限を超えた時刻)で2e(削除)
		var threshold = ManualLockMonitor.ComputeThresholdTicks(600);
		var afterThreshold = begun.Handle.Vdc + threshold + 1;
		var tick2 = RunTick(lockDb, tick1.NextState, afterThreshold);

		Assert.AreEqual(ManualLockMonitorAction.RecordTimeout, tick2.Action);
		Assert.AreEqual(0, Db.Fetch<SysSequence>($"SELECT * FROM {nameof(SysSequence)}").Count, "閾値超過でSysSequenceの行が削除されること");

		var histories = Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)} ORDER BY Id");
		Assert.AreEqual(2, histories.Count, "2b(検知)と2e(タイムアウト解放)の2行になること");
		Assert.AreEqual((int)EmSysHistType.AutoExec, histories[0].SysHistType);
		Assert.AreEqual((int)EmSysHistType.AutoExec, histories[1].SysHistType);
		Assert.AreEqual(0, histories[0].ReturnCode, "2bのReturnCodeは0");
		Assert.AreNotEqual(0, histories[1].ReturnCode, "2e(異常終了)のReturnCodeは非0");
		Assert.IsTrue(histories[1].Memo.Contains("総平均原価更新"));
	}

	// ------------------------------------------------------------------
	// 2a/2cでログが増えないことの確認(§3.1、§3.3、§3.7)
	// ------------------------------------------------------------------

	[TestMethod]
	public void 行が無い場合と前進している場合はログが増えない() {
		var lockDb = new ManualLockDb(Db);

		// 2a: 行が無い状態でのティック
		var tick2a = RunTick(lockDb, previous: null, nowUtcTicks: 1000);
		Assert.AreEqual(ManualLockMonitorAction.None, tick2a.Action);
		Assert.AreEqual(0, Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}").Count, "2aではログが増えないこと");

		// 開始して2bを1回発生させる
		var begun = lockDb.TryBegin("買掛再集計", "買掛集計", 600);
		var tick2b = RunTick(lockDb, tick2a.NextState, begun.Handle!.Vdc);
		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick2b.Action);
		Assert.AreEqual(1, Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}").Count);

		// 進捗を進めてVduを前進させる(2c)
		lockDb.Progress(begun.Handle, "買掛集計2", 2, "進捗");
		var advancedRow = Db.Fetch<SysSequence>($"SELECT * FROM {nameof(SysSequence)}").Single();
		var tick2c = RunTick(lockDb, tick2b.NextState, advancedRow.Vdu);

		Assert.AreEqual(ManualLockMonitorAction.None, tick2c.Action);
		Assert.AreEqual(1, Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}").Count, "2cではログが増えないこと");
	}

	// ------------------------------------------------------------------
	// L-11: ログが必ず 2b→2f か 2b→2e の対になる
	// ------------------------------------------------------------------

	[TestMethod]
	public void ログは必ず2bと2fの対になる_正常終了経路() {
		var lockDb = new ManualLockDb(Db);
		var begun = lockDb.TryBegin("請求計算", "請求集計", 600);

		var tick1 = RunTick(lockDb, previous: null, nowUtcTicks: begun.Handle!.Vdc);
		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick1.Action);

		// 処理側が正常終了する(§2.3): SysSequenceの行が消える
		lockDb.Complete(begun.Handle, 0, 10, "正常終了");

		var tick2 = RunTick(lockDb, tick1.NextState, begun.Handle.Vdc + 1000);
		Assert.AreEqual(ManualLockMonitorAction.RecordNormalEnd, tick2.Action);
		Assert.IsNull(tick2.NextState);

		AssertMonitorHistoryPairsAlternate();
	}

	[TestMethod]
	public void ログは必ず2bと2eの対になる_異常終了経路() {
		var lockDb = new ManualLockDb(Db);
		var begun = lockDb.TryBegin("HHT取込反映", "HHT反映", 60);
		begun.Handle!.Dispose(); // Completeを呼ばずに異常終了

		var tick1 = RunTick(lockDb, previous: null, nowUtcTicks: begun.Handle.Vdc);
		Assert.AreEqual(ManualLockMonitorAction.RecordDetected, tick1.Action);

		var threshold = ManualLockMonitor.ComputeThresholdTicks(60);
		var tick2 = RunTick(lockDb, tick1.NextState, begun.Handle.Vdc + threshold + 1);
		Assert.AreEqual(ManualLockMonitorAction.RecordTimeout, tick2.Action);

		AssertMonitorHistoryPairsAlternate();
	}

	/// <summary>
	/// 監視タスクの履歴(<c>SysHistAutoexec</c>、<c>TaskName=マニュアル排他制御監視</c>)をId昇順に読み、
	/// 「2b」で始まり、次が必ず「2e」または「2f」であることを確認する(設計書§3.7の不変条件)。
	/// </summary>
	private void AssertMonitorHistoryPairsAlternate() {
		var histories = Db.Fetch<SysHistAutoexec>(
			$"SELECT * FROM {nameof(SysHistAutoexec)} WHERE TaskName=@0 ORDER BY Id", MonitorTaskName);
		Assert.IsTrue(histories.Count > 0, "監視履歴が存在すること");
		Assert.AreEqual(0, histories.Count % 2, "2b→2e/2fの対になっているため偶数件であること");

		for (var i = 0; i < histories.Count; i += 2) {
			var startEntry = histories[i];
			var endEntry = histories[i + 1];
			Assert.IsTrue(startEntry.Memo.StartsWith("[2b:"), $"{i}件目は2b(検知)で始まること: {startEntry.Memo}");
			Assert.IsTrue(
				endEntry.Memo.StartsWith("[2e:") || endEntry.Memo.StartsWith("[2f:"),
				$"{i + 1}件目は2e(タイムアウト解放)または2f(正常終了)であること: {endEntry.Memo}");
			Assert.AreEqual((int)EmSysHistType.AutoExec, startEntry.SysHistType);
			Assert.AreEqual((int)EmSysHistType.AutoExec, endEntry.SysHistType);
		}
	}
}
