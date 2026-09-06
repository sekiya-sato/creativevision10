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
/// <see cref="ManualLockDb"/>（マニュアル排他制御 詳細設計 Step 9-2）の単体テスト。
/// SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbTests"/>に合わせる。
/// 仕様書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §5 のL-01〜L-06、L-12を実装する。
/// </summary>
[TestClass]
public class ManualLockDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockDbTests-{Guid.NewGuid():N}";
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
	// L-01: 排他行が無い状態でTryBeginすると行が1行できて取得できる
	// ------------------------------------------------------------------

	[TestMethod]
	public void TryBegin_行が無い状態なら取得できて行が1行できる() {
		var target = new ManualLockDb(Db);

		var result = target.TryBegin("在庫・掛再集計", "買掛集計", 600, "月次バッチ");

		Assert.IsTrue(result.IsAcquired);
		Assert.IsNotNull(result.Handle);
		Assert.IsNull(result.Blocker);

		var rows = FetchAllSequences();
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual((int)EmSysSeqType.ManualLock, rows[0].SysSeqType);
		Assert.AreEqual("在庫・掛再集計", rows[0].TableName);
		Assert.AreEqual("買掛集計", rows[0].ColumnName);
		Assert.AreEqual(1, rows[0].SeqNo);
		Assert.AreEqual(600, rows[0].ExpectedDuration);
		Assert.AreEqual("月次バッチ", rows[0].Memo);
	}

	// ------------------------------------------------------------------
	// L-02: 既にSysSeqType=1の行があるとき、後発が取得できず自分の行を残さない。
	//        かつ先行処理の情報が呼び出し側へ返る
	// ------------------------------------------------------------------

	[TestMethod]
	public void TryBegin_先行がいると取得できず自分の行を残さない() {
		var first = new ManualLockDb(Db);
		var firstResult = first.TryBegin("在庫・掛再集計", "買掛集計", 600, "先行");
		Assert.IsTrue(firstResult.IsAcquired);

		var second = new ManualLockDb(Db);
		var secondResult = second.TryBegin("現在庫再集計", "現在庫集計", 300, "後発");

		Assert.IsFalse(secondResult.IsAcquired);
		Assert.IsNull(secondResult.Handle);
		Assert.IsNotNull(secondResult.Blocker);
		Assert.AreEqual("在庫・掛再集計", secondResult.Blocker!.TableName);
		Assert.AreEqual("買掛集計", secondResult.Blocker!.ColumnName);
		Assert.AreEqual(1, secondResult.Blocker!.SeqNo);
		Assert.AreEqual(600, secondResult.Blocker!.ExpectedDuration);
		Assert.AreEqual("先行", secondResult.Blocker!.Memo);

		// 後発の行を残さない: SysSeqType=1の行は先行の1行のまま
		var rows = FetchAllSequences();
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual("在庫・掛再集計", rows[0].TableName);
	}

	// ------------------------------------------------------------------
	// L-03: 2つのManualLockDbがほぼ同時にTryBeginしたとき、Idの大きい方だけが降り、
	//        行が1行に収束する（§2.1）
	// ------------------------------------------------------------------

	[TestMethod]
	public void TryBegin_二つのインスタンスからの連続呼び出しでIdが大きい方だけ降りる() {
		var dbA = new ManualLockDb(Db);
		var dbB = new ManualLockDb(Db);

		// どちらも「確認済み」の状態を模すため、DB上には直接INSERTのみが起きるタイミングで
		// 連続して呼ぶ（TryBeginは呼び出しごとにINSERT→再確認まで完結するため、
		// 同一DBに対して2インスタンスから連続で呼べばIdの大小関係で再現できる）
		var resultA = dbA.TryBegin("在庫・掛再集計", "買掛集計", 600);
		var resultB = dbB.TryBegin("在庫・掛再集計", "現在庫集計", 600);

		// Idが小さい方(A)が勝者、大きい方(B)が降りる
		Assert.IsTrue(resultA.IsAcquired);
		Assert.IsFalse(resultB.IsAcquired);
		Assert.IsNotNull(resultB.Blocker);
		Assert.AreEqual(resultA.Handle!.TableName, resultB.Blocker!.TableName);

		var rows = FetchAllSequences();
		Assert.AreEqual(1, rows.Count);
		Assert.AreEqual("買掛集計", rows[0].ColumnName);
	}

	// ------------------------------------------------------------------
	// L-04: Progressで ColumnName / SeqNo / Memo / Vdu が変わる
	// ------------------------------------------------------------------

	[TestMethod]
	public void Progress_ColumnNameとSeqNoとMemoとVduが変わる() {
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600);
		var before = FetchAllSequences().Single();

		var updated = target.Progress(begun.Handle!, "売掛集計", 2, "売掛の集計を実施");

		Assert.AreEqual(1, updated);
		var after = FetchAllSequences().Single();
		Assert.AreEqual("売掛集計", after.ColumnName);
		Assert.AreEqual(2, after.SeqNo);
		Assert.IsTrue(after.Memo.Contains("売掛集計"));
		Assert.IsTrue(after.Memo.Contains("売掛の集計を実施"));
		Assert.AreNotEqual(before.Vdu, after.Vdu);
		Assert.IsTrue(after.Vdu >= before.Vdu);
	}

	/// <summary>
	/// Progressの対象行が消えている場合(強制クリア等)に例外にならず0件を返すこと
	/// </summary>
	[TestMethod]
	public void Progress_対象行が消えている場合は例外にせず0件を返す() {
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600);
		Db.Execute($"DELETE FROM {nameof(SysSequence)}");

		var updated = target.Progress(begun.Handle!, "売掛集計", 2, "強制クリア後");

		Assert.AreEqual(0, updated);
	}

	// ------------------------------------------------------------------
	// L-05: Memoが300文字を超えるとき、古い内容から切り捨てて末尾が残る
	// ------------------------------------------------------------------

	[TestMethod]
	public void Progress_Memoが300文字を超えると古い内容から切り捨てて末尾が残る() {
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600, new string('a', 280));

		target.Progress(begun.Handle!, "売掛集計", 2, "最新の内容ABC123");

		var after = FetchAllSequences().Single();
		Assert.IsTrue(after.Memo.Length <= 300);
		Assert.IsTrue(after.Memo.EndsWith("最新の内容ABC123"));
		// 先頭の古い内容('a'の連続)は切り捨てられて残っていない
		Assert.IsFalse(after.Memo.StartsWith(new string('a', 280)));
	}

	/// <summary>Memo切り捨ての純関数の単体テスト(境界値: ちょうど300文字、301文字)</summary>
	[TestMethod]
	public void AppendTruncatedMemo_境界値_ちょうど上限は切り捨てない() {
		// " / "(3文字)の区切りを含めてちょうど300文字になるよう調整
		var current = new string('x', 100);
		var appendText = new string('y', 197); // 100 + 3(" / ") + 197 = 300
		var result = ManualLockDb.AppendTruncatedMemo(current, appendText, 300);

		Assert.AreEqual(300, result.Length);
		Assert.IsTrue(result.EndsWith(appendText));
		Assert.IsTrue(result.StartsWith(current));
	}

	[TestMethod]
	public void AppendTruncatedMemo_境界値_301文字は先頭1文字だけ切り捨てて末尾を残す() {
		var current = new string('x', 100);
		var appendText = new string('y', 198); // 100 + 3(" / ") + 198 = 301
		var combinedBeforeTruncate = current + " / " + appendText;

		var result = ManualLockDb.AppendTruncatedMemo(current, appendText, 300);

		Assert.AreEqual(300, result.Length);
		Assert.AreEqual(combinedBeforeTruncate[1..], result);
		Assert.IsTrue(result.EndsWith(appendText));
	}

	// ------------------------------------------------------------------
	// L-06: Completeで SeqNo=99 が書かれ、SysHistAutoexecが1行増え、SysSequenceの行が消える
	// ------------------------------------------------------------------

	[TestMethod]
	public void Complete_SeqNo99を書き履歴を追加しSysSequenceの行を消す() {
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600, "開始メモ");
		target.Progress(begun.Handle!, "売掛集計", 2, "進捗メモ");

		target.Complete(begun.Handle!, 0, 42, "正常終了しました");

		// SysSequenceの行は消える(削除される)
		Assert.AreEqual(0, FetchAllSequences().Count);

		// SysHistAutoexecが1行増える
		var histories = Db.Fetch<SysHistAutoexec>($"SELECT * FROM {nameof(SysHistAutoexec)}");
		Assert.AreEqual(1, histories.Count);
		var history = histories[0];
		Assert.AreEqual((int)EmSysHistType.ManualExec, history.SysHistType);
		Assert.AreEqual("在庫・掛再集計", history.TaskName);
		Assert.AreEqual(0, history.ReturnCode);
		Assert.AreEqual(42, history.Count);
		Assert.IsTrue(history.Memo.Contains("進捗メモ"));
		Assert.IsTrue(history.Memo.Contains("正常終了しました"));
		Assert.AreEqual(14, history.StartTime.Length);
		Assert.AreEqual(14, history.EndTime.Length);

		Assert.IsTrue(begun.Handle!.Completed);
	}

	/// <summary>Dispose後にComplete()を呼ばなかった場合は行を残す(異常終了とみなす方針)ことの確認</summary>
	[TestMethod]
	public void Dispose_Completeを呼ばずに破棄すると行は残る() {
		var target = new ManualLockDb(Db);
		var begun = target.TryBegin("在庫・掛再集計", "買掛集計", 600);

		using (begun.Handle) {
			// Completeを呼ばずにスコープを抜ける = 異常終了を模す
		}

		Assert.AreEqual(1, FetchAllSequences().Count);
	}

	// ------------------------------------------------------------------
	// L-12: SysSeqType=0(テーブル連番)の行が排他判定にもFetchActiveLocksにも影響しない
	// ------------------------------------------------------------------

	[TestMethod]
	public void TryBegin及びFetchActiveLocksはSysSeqType0の行を無視する() {
		// テーブル連番(SysSeqType=0)の行を複数用意しておく
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterShohin", ColumnName = "Code", SeqNo = 1 });
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterShohin", ColumnName = "Code", SeqNo = 2 });
		Db.Insert(new SysSequence { SysSeqType = (int)EmSysSeqType.TableSeq, TableName = "MasterTorihiki", ColumnName = "Code", SeqNo = 1 });

		var target = new ManualLockDb(Db);

		// FetchActiveLocksはSysSeqType=0の行を含めない
		Assert.AreEqual(0, target.FetchActiveLocks().Count);

		// TryBeginはSysSeqType=0の行があっても取得できる(排他判定に影響しない)
		var result = target.TryBegin("在庫・掛再集計", "買掛集計", 600);
		Assert.IsTrue(result.IsAcquired);
		Assert.AreEqual(1, target.FetchActiveLocks().Count);

		// SysSeqType=0の行は変わらず3行残っている
		var tableSeqRows = FetchAllSequences().Where(x => x.SysSeqType == (int)EmSysSeqType.TableSeq).ToList();
		Assert.AreEqual(3, tableSeqRows.Count);
	}
}
