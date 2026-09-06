using System;
using System.Linq;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// マニュアル排他制御の状態照会・強制クリア（<see cref="ManualLockDb.FetchManualLockStatus"/>、
/// <see cref="ManualLockDb.ForceClearManualLocks"/>、詳細設計 §2.5、Step 9-5）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="ManualLockDbTests"/>に合わせる。
/// 仕様書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §5 のL-14〜L-20を実装する。
/// L-15/L-16はサーバー側で検証できる範囲として、確認本文の中身ではなくDTOの
/// <see cref="ManualLockRow.IsLikelyAlive"/>と<see cref="ManualLockRow.ElapsedSecondsSinceVdu"/>で代替する。
/// </summary>
[TestClass]
public class ManualLockClearTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockClearTests-{Guid.NewGuid():N}";
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

	private System.Collections.Generic.List<SysSequence> FetchAllSequences() =>
		Db.Fetch<SysSequence>($"SELECT * FROM {nameof(SysSequence)} ORDER BY Id");

	// ------------------------------------------------------------------
	// L-14: 排他行が0件のとき、照会が空を返す
	// ------------------------------------------------------------------

	[TestMethod]
	public void FetchManualLockStatus_排他行が0件なら空を返す() {
		var target = new ManualLockDb(Db);

		var status = target.FetchManualLockStatus();

		Assert.AreEqual(0, status.Rows.Count);
		Assert.IsFalse(status.HasLikelyAlive);
	}

	// ------------------------------------------------------------------
	// L-15/L-16: ElapsedSecondsSinceVduとIsLikelyAliveが正しい。
	//   閾値の境界(ComputeThresholdTicksと一致すること)を、ExpectedDurationが小さい場合(15分の下限)と
	//   大きい場合(×2)の両方で固定する
	// ------------------------------------------------------------------

	/// <summary>
	/// ExpectedDurationが小さく、15分の下限が効く場合の閾値境界。
	/// 実時刻(Common.GetVdate)基準のため、境界からわずかにずらして安定させる
	/// </summary>
	[TestMethod]
	public void FetchManualLockStatus_下限15分が効く場合_閾値未満はIsLikelyAliveがtrue() {
		const long expectedDurationSeconds = 60; // ×2=120秒 < 15分 なので下限(15分)が効く
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);
		Assert.AreEqual(TimeSpan.FromMinutes(15).Ticks, threshold, "下限15分が効いていることの前提確認");

		var now = Common.GetVdate();
		// 閾値よりわずかに手前(経過時間が短い = まだ動いている可能性がある)
		var vdu = now - (threshold - TimeSpan.FromSeconds(5).Ticks);
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "在庫・掛再集計",
			ColumnName = "買掛集計",
			SeqNo = 1,
			Vdc = vdu,
			Vdu = vdu,
			ExpectedDuration = expectedDurationSeconds,
		});

		var target = new ManualLockDb(Db);
		var status = target.FetchManualLockStatus();

		Assert.AreEqual(1, status.Rows.Count);
		var row = status.Rows[0];
		Assert.IsTrue(row.IsLikelyAlive);
		Assert.IsTrue(status.HasLikelyAlive);
		// ElapsedSecondsSinceVduはおよそ閾値-5秒(=900-5=895秒)相当のはず
		Assert.IsTrue(row.ElapsedSecondsSinceVdu is >= 890 and <= 900,
			$"ElapsedSecondsSinceVdu={row.ElapsedSecondsSinceVdu}");
	}

	[TestMethod]
	public void FetchManualLockStatus_下限15分が効く場合_閾値超過はIsLikelyAliveがfalse() {
		const long expectedDurationSeconds = 60;
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);

		var now = Common.GetVdate();
		// 閾値よりわずかに超過(経過時間が長い = 監視タスクが解放する見込み)
		var vdu = now - (threshold + TimeSpan.FromSeconds(5).Ticks);
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "在庫・掛再集計",
			ColumnName = "買掛集計",
			SeqNo = 1,
			Vdc = vdu,
			Vdu = vdu,
			ExpectedDuration = expectedDurationSeconds,
		});

		var target = new ManualLockDb(Db);
		var status = target.FetchManualLockStatus();

		Assert.AreEqual(1, status.Rows.Count);
		var row = status.Rows[0];
		Assert.IsFalse(row.IsLikelyAlive);
		Assert.IsFalse(status.HasLikelyAlive);
		Assert.IsTrue(row.ElapsedSecondsSinceVdu is >= 900 and <= 910,
			$"ElapsedSecondsSinceVdu={row.ElapsedSecondsSinceVdu}");
	}

	/// <summary>ExpectedDurationが大きく、×2が15分の下限を上回って効く場合の閾値境界</summary>
	[TestMethod]
	public void FetchManualLockStatus_倍数2倍が効く場合_閾値未満はIsLikelyAliveがtrue() {
		const long expectedDurationSeconds = 3600; // ×2=7200秒(2時間) > 15分 なので倍数側が効く
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);
		Assert.AreEqual(TimeSpan.FromSeconds(expectedDurationSeconds * 2).Ticks, threshold, "倍数2倍が効いていることの前提確認");

		var now = Common.GetVdate();
		var vdu = now - (threshold - TimeSpan.FromSeconds(5).Ticks);
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "評価替え",
			ColumnName = "評価替え計算",
			SeqNo = 1,
			Vdc = vdu,
			Vdu = vdu,
			ExpectedDuration = expectedDurationSeconds,
		});

		var target = new ManualLockDb(Db);
		var status = target.FetchManualLockStatus();

		Assert.AreEqual(1, status.Rows.Count);
		Assert.IsTrue(status.Rows[0].IsLikelyAlive);
		Assert.IsTrue(status.HasLikelyAlive);
	}

	[TestMethod]
	public void FetchManualLockStatus_倍数2倍が効く場合_閾値超過はIsLikelyAliveがfalse() {
		const long expectedDurationSeconds = 3600;
		var threshold = ManualLockMonitor.ComputeThresholdTicks(expectedDurationSeconds);

		var now = Common.GetVdate();
		var vdu = now - (threshold + TimeSpan.FromSeconds(5).Ticks);
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "評価替え",
			ColumnName = "評価替え計算",
			SeqNo = 1,
			Vdc = vdu,
			Vdu = vdu,
			ExpectedDuration = expectedDurationSeconds,
		});

		var target = new ManualLockDb(Db);
		var status = target.FetchManualLockStatus();

		Assert.AreEqual(1, status.Rows.Count);
		Assert.IsFalse(status.Rows[0].IsLikelyAlive);
		Assert.IsFalse(status.HasLikelyAlive);
	}

	// ------------------------------------------------------------------
	// L-17: 強制クリアで SysSeqType=1 の行が全件消え、SysSeqType=0 の行は消えない
	// ------------------------------------------------------------------

	[TestMethod]
	public void ForceClearManualLocks_SysSeqType1の行が全件消えSysSeqType0の行は消えない() {
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterShohin", ColumnName = "Code", SeqNo = 1 });
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中");
		Assert.IsTrue(begun.IsAcquired);

		var deletedCount = target.ForceClearManualLocks(idShain: 100);

		Assert.AreEqual(1, deletedCount);
		var remaining = FetchAllSequences();
		Assert.AreEqual(1, remaining.Count);
		Assert.AreEqual((int)EmSysSeqType.TableSeq, remaining[0].SysSeqType);
		Assert.AreEqual("MasterShohin", remaining[0].TableName);
	}

	// ------------------------------------------------------------------
	// L-18: 強制クリアで SysHistAutoexec へ SysHistType=1・TaskName='マニュアル排他制御クリア' の行が
	//        1件残り、Memoに削除した行の内容と実行社員が入る
	// ------------------------------------------------------------------

	[TestMethod]
	public void ForceClearManualLocks_履歴へSysHistType1とTaskNameと実行社員を記録する() {
		var target = new ManualLockDb(Db);
		target.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中メモ");

		var deletedCount = target.ForceClearManualLocks(idShain: 12345);

		Assert.AreEqual(1, deletedCount);
		var histories = Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}");
		Assert.AreEqual(1, histories.Count);
		var history = histories[0];
		Assert.AreEqual((int)EmSysHistType.ManualExec, history.SysHistType);
		Assert.AreEqual(ManualLockDb.ManualLockClearTaskName, history.TaskName);
		Assert.AreEqual(0, history.ReturnCode);
		Assert.AreEqual(1, history.Count);
		Assert.IsTrue(history.Memo.Contains("在庫・掛再集計"));
		Assert.IsTrue(history.Memo.Contains("買掛集計"));
		Assert.IsTrue(history.Memo.Contains("実行中メモ"));
		Assert.IsTrue(history.Memo.Contains("実行社員Id=12345"));
	}

	/// <summary>排他行が0件で強制クリアを呼んでも履歴が増えない(消していないため)</summary>
	[TestMethod]
	public void ForceClearManualLocks_0件のときは履歴が増えない() {
		var target = new ManualLockDb(Db);

		var deletedCount = target.ForceClearManualLocks(idShain: 999);

		Assert.AreEqual(0, deletedCount);
		var histories = Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}");
		Assert.AreEqual(0, histories.Count);
	}

	/// <summary>2行ある状態(§2.1の競合直後を想定)で強制クリアすると2行とも消え、履歴のCountが2になる</summary>
	[TestMethod]
	public void ForceClearManualLocks_2行ある状態で両方消え履歴Countが2になる() {
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "在庫・掛再集計",
			ColumnName = "買掛集計",
			SeqNo = 1,
			Memo = "1行目",
		});
		Db.Insert(new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = "現在庫再集計",
			ColumnName = "現在庫集計",
			SeqNo = 1,
			Memo = "2行目",
		});
		var target = new ManualLockDb(Db);

		var deletedCount = target.ForceClearManualLocks(idShain: 1);

		Assert.AreEqual(2, deletedCount);
		Assert.AreEqual(0, FetchAllSequences().Count);
		var histories = Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}");
		Assert.AreEqual(1, histories.Count);
		Assert.AreEqual(2, histories[0].Count);
	}

	// ------------------------------------------------------------------
	// L-19: 強制クリア後に監視タスクのEvaluateを動かすと、§3.6(2f)の正常終了経路になる
	// ------------------------------------------------------------------

	[TestMethod]
	public void ForceClearManualLocks後にEvaluateすると正常終了とみなされる() {
		var lockDb = new ManualLockDb(Db);
		var begun = lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中");
		Assert.IsTrue(begun.IsAcquired);

		// 監視タスクが直前にこの行を検知していた状態を模す
		var beforeClear = lockDb.FetchActiveLocks().Single();
		var previousState = ManualLockMonitorState.FromRow(beforeClear);

		lockDb.ForceClearManualLocks(idShain: 1);

		// 強制クリア後、監視タスクが次回チェックすると行が消えている
		var tick = ManualLockMonitor.Evaluate(previousState, lockDb.FetchActiveLocks(), Common.GetVdate());

		Assert.AreEqual(ManualLockMonitorAction.RecordNormalEnd, tick.Action);
		Assert.IsNull(tick.NextState);
		Assert.AreSame(previousState, tick.Subject);
	}

	// ------------------------------------------------------------------
	// L-20: 強制クリア直後に別の一連処理がTryBeginで正常に開始できる
	// ------------------------------------------------------------------

	[TestMethod]
	public void ForceClearManualLocks直後に別の一連処理がTryBeginできる() {
		var lockDb = new ManualLockDb(Db);
		lockDb.TryBegin("在庫・掛再集計", "買掛集計", 600, "実行中");

		lockDb.ForceClearManualLocks(idShain: 1);

		var result = lockDb.TryBegin("現在庫再集計", "現在庫集計", 300, "新規開始");

		Assert.IsTrue(result.IsAcquired);
		Assert.IsNotNull(result.Handle);
		var rows = FetchAllSequences();
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual("現在庫再集計", rows[0].TableName);
	}
}
