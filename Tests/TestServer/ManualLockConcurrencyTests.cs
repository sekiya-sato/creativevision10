using System;
using System.Linq;
using System.Threading;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="ManualLockDb.TryBegin"/>のTOCTOU対策(設計書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §2.1)を、
/// 本当に並行させて実証する単体テスト（テスト計画 `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` E-04）。
/// <para>
/// 既存の<see cref="ManualLockDbTests.TryBegin_二つのインスタンスからの連続呼び出しでIdが大きい方だけ降りる"/>は、
/// 同一プロセス内で2インスタンスを<b>逐次</b>呼ぶ模擬にとどまる。本クラスはスレッドごとに別々の
/// <see cref="SqliteConnection"/>／<see cref="ExDatabaseSqlite"/>／<see cref="ManualLockDb"/>を用意し、
/// <see cref="Barrier"/>でNスレッドを同時発火させることで、本当の並行呼び出しを再現する。
/// </para>
/// <para>
/// <b>限界</b>: 本テストは1プロセス内の複数<see cref="SqliteConnection"/>（<c>Cache=Shared</c>の
/// インメモリDBを共有）による並行であり、複数プロセス・複数マシンでの真の分散並行は再現しない。
/// また、SQLiteはシングルライタであるため、<c>PRAGMA busy_timeout</c>（既定30秒、
/// <c>ExDatabaseSqlite.GetDbConn</c>と同じ既定値。ここでは接続文字列で明示していないため
/// Microsoft.Data.Sqliteの既定値がそのまま効く）の範囲内であれば<c>SQLITE_BUSY</c>は
/// 自動的にリトライされ表面化しない想定である。万一<c>SQLITE_BUSY</c>等の例外が観測された場合は、
/// リトライで隠さずテストを失敗させ、例外の型・メッセージ・発生したスレッド数を報告する。
/// </para>
/// </summary>
[TestClass]
public class ManualLockConcurrencyTests {
	private string? _connectionString;
	private SqliteConnection? _anchorConnection;
	private SqliteConnection? _setupConnection;
	private ExDatabaseSqlite? _setupDb;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockConcurrencyTests-{Guid.NewGuid():N}";
		_connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		// 共有インメモリDBは「開いている接続が1つも無くなると消える」ため、
		// 捨て接続(_anchorConnection)を先に開いて維持する(既存テストと同じ作法)
		_anchorConnection = new SqliteConnection(_connectionString);
		_anchorConnection.Open();

		_setupConnection = new SqliteConnection(_connectionString);
		_setupConnection.Open();
		_setupDb = new ExDatabaseSqlite(_setupConnection) { KeepConnectionAlive = true };
		SetupDb.CreateTable(typeof(SysSequence), true, false);
		SetupDb.CreateTable(typeof(SysHistAutoexec), true, false);
	}

	[TestCleanup]
	public void Cleanup() {
		_setupDb?.Close();
		_setupConnection?.Close();
		_anchorConnection?.Close();
	}

	private ExDatabaseSqlite SetupDb => _setupDb ?? throw new AssertFailedException("Database not initialized");
	private string ConnectionString => _connectionString ?? throw new AssertFailedException("Database not initialized");

	// ------------------------------------------------------------------
	// E-04: 真の同時TryBegin。N=2とN=8の両方で、IsAcquired==trueがちょうど1つになり、
	// 行が1行に収束し、勝者のIdが全スレッドの中で最小になり、敗者は自分の行を残さないことを確認する
	// ------------------------------------------------------------------

	[TestMethod]
	public void TryBegin_2スレッドが同時に呼んでもIsAcquiredはちょうど1つに収束する() {
		RunConcurrentTryBeginScenario(threadCount: 2);
	}

	[TestMethod]
	public void TryBegin_8スレッドが同時に呼んでもIsAcquiredはちょうど1つに収束する() {
		RunConcurrentTryBeginScenario(threadCount: 8);
	}

	/// <summary>
	/// threadCount本のスレッドから、同一processNameで同時にTryBeginを呼び、収束後の状態を検証する。
	/// </summary>
	private void RunConcurrentTryBeginScenario(int threadCount) {
		const string processName = "在庫・掛再集計";

		// 開始前の最大Idを記録しておく(空のはずだが、念のため一般化しておく)。
		// 勝者のIdが「全スレッドが観測した中での最小Id」と一致することの根拠は、
		// テーブルが本テスト開始時点で空であり、AutoIncrementのIdはスレッドの
		// INSERT完了順(SQLiteはシングルライタのため全体で1つの順序に決まる)で
		// baselineMaxId+1, +2, ... と重複なく割り当てられること。
		// TryBeginは「現在アクティブな行の中でIdが最小でない側が自分の行を消して降りる」設計
		// (ManualLockDb.TryBegin、設計書§2.1)のため、割り当てられたId群の中で最小のIdを
		// 持つ行だけが最後まで削除されずに残る。したがって収束後に残る行のIdは、
		// 全スレッドに割り当てられたId群の最小値(=baselineMaxId+1)と一致するはずである。
		var baselineMaxId = SetupDb.ExecuteScalar<long>($"SELECT COALESCE(MAX(Id), 0) FROM {nameof(SysSequence)}");

		var connections = new SqliteConnection[threadCount];
		var lockDbs = new ManualLockDb[threadCount];
		var results = new ManualLockResult?[threadCount];
		var exceptions = new Exception?[threadCount];
		var threads = new Thread[threadCount];
		using var barrier = new Barrier(threadCount);

		try {
			// スレッドごとに別々のSqliteConnection/ExDatabaseSqlite/ManualLockDbを用意する
			// (同一接続を複数スレッドで共有すると本当の並行にならないため)
			for (var i = 0; i < threadCount; i++) {
				var conn = new SqliteConnection(ConnectionString);
				conn.Open();
				connections[i] = conn;
				var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
				lockDbs[i] = new ManualLockDb(db);
			}

			for (var i = 0; i < threadCount; i++) {
				var threadIndex = i;
				threads[threadIndex] = new Thread(() => {
					try {
						// 全スレッドがここで待ち合わせ、Barrierが解放された瞬間に一斉にTryBeginを呼ぶ
						barrier.SignalAndWait();
						results[threadIndex] = lockDbs[threadIndex].TryBegin(
							processName, $"スレッド{threadIndex}", 600, $"thread{threadIndex}");
					}
					catch (Exception ex) {
						exceptions[threadIndex] = ex;
					}
				});
			}

			foreach (var thread in threads) {
				thread.Start();
			}
			foreach (var thread in threads) {
				thread.Join();
			}

			// SQLiteの書き込み競合(SQLITE_BUSY/database is locked)等が出た場合はリトライで隠さず、
			// その事実(型・メッセージ・発生スレッド数)を報告する
			var failedThreads = exceptions
				.Select((ex, idx) => (ex, idx))
				.Where(x => x.ex != null)
				.ToList();
			if (failedThreads.Count > 0) {
				var detail = string.Join(" / ", failedThreads.Select(x => $"thread{x.idx}: {x.ex!.GetType().FullName}: {x.ex.Message}"));
				Assert.Fail($"threadCount={threadCount}で{failedThreads.Count}件の例外が発生した(リトライはしていない): {detail}");
			}

			// 1. IsAcquired==trueはちょうど1つ
			var acquiredResults = results.Where(r => r is { IsAcquired: true }).ToList();
			Assert.AreEqual(1, acquiredResults.Count, $"threadCount={threadCount}でIsAcquired==trueがちょうど1つであること");

			// 2. 収束後、SysSeqType=1の行はちょうど1行
			var activeLocks = new ManualLockDb(SetupDb).FetchActiveLocks();
			Assert.AreEqual(1, activeLocks.Count, "収束後、SysSeqType=1の行はちょうど1行であること");
			var survivor = activeLocks[0];
			Assert.AreEqual(processName, survivor.TableName);

			// 3. 勝者のIdは、全スレッドに割り当てられたId群の中での最小Idと一致する(上のコメント参照)
			Assert.AreEqual(baselineMaxId + 1, survivor.Id,
				"勝者の行のIdは、全スレッドが取得したIdの中で最小(=開始前の最大Id+1)であること");

			// 4. 敗者はいずれもBlocker!=nullで、Blocker.TableNameがprocessNameと一致する
			var loserResults = results.Where(r => r is { IsAcquired: false }).ToList();
			Assert.AreEqual(threadCount - 1, loserResults.Count, "IsAcquired==false(敗者)はthreadCount-1件であること");
			foreach (var loser in loserResults) {
				Assert.IsNotNull(loser!.Blocker, "敗者はBlockerを持つこと");
				Assert.AreEqual(processName, loser.Blocker!.TableName, "BlockerのTableNameがprocessNameと一致すること");
			}

			// 後片付け: 勝者のCompleteを呼び、行が消えることを確認する
			var winnerIndex = Array.FindIndex(results, r => r is { IsAcquired: true });
			Assert.IsTrue(winnerIndex >= 0);
			lockDbs[winnerIndex].Complete(results[winnerIndex]!.Handle!, 0, threadCount, "並行テスト終了");

			var afterComplete = new ManualLockDb(SetupDb).FetchActiveLocks();
			Assert.AreEqual(0, afterComplete.Count, "Complete後はSysSequenceの行が消えること");
		}
		finally {
			foreach (var conn in connections) {
				conn?.Close();
				conn?.Dispose();
			}
		}
	}
}
