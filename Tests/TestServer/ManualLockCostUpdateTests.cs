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
/// マニュアル排他制御（`Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`、以下「設計書」）の
/// 原価4処理・評価替えへの適用(Step 9-3)を、総平均原価更新(<see cref="CostUpdateDb.ApplyTotalAverageCost"/>)で
/// 代表させて確認する。SQLiteインメモリDBの作成作法は<see cref="CostUpdateDbCostTests"/>に合わせる。
/// </summary>
[TestClass]
public class ManualLockCostUpdateTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ManualLockCostUpdateTests-{Guid.NewGuid():N}";
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
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	// ------------------------------------------------------------------
	// テーブル・データ準備(CostUpdateDbCostTests.CreateCostTables等と同じ作法)
	// ------------------------------------------------------------------

	private void CreateCostTables(int costMethod) {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterShain), true, false);
		Db.CreateTable(typeof(MasterShiire), true, false);
		Db.CreateTable(typeof(MasterMaterial), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran02Material), true, false);
		Db.CreateTable(typeof(TranGenka), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SysSequence), true, false);
		Db.CreateTable(typeof(SysHistAutoexec), true, false);
		Db.Execute($"CREATE UNIQUE INDEX TranGenka_uk1 ON {nameof(TranGenka)} (SumMonth, Id_Shohin, CostMethod, ChangeKind)");
		Db.Insert(new MasterSysman { ShimeBi = 99, CostMethod = costMethod, Vdc = 1, Vdu = 1 });
	}

	private long InsertShohin(string code, int tankaGenka = 0, int isZaiko = 1) {
		var shohin = new MasterShohin { Code = code, Name = $"商品{code}", TankaGenka = tankaGenka, IsZaiko = isZaiko, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		return shohin.Id;
	}

	private long InsertShain(string code = "E1") {
		var shain = new MasterShain { Code = code, Name = $"社員{code}", Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		return shain.Id;
	}

	private long InsertPurchase(string denDay, int kubun, long idShohin, int su, long kingaku, int isStock = 1, int isPay = 1) {
		var meisai = new System.Collections.Generic.List<Tran99Meisai> {
			new() { No = 1, Id_Shohin = idShohin, Su = su, Tanka = su == 0 ? 0 : (int)(kingaku / su), Kingaku = kingaku },
		};
		var header = new Tran03Shiire { DenDay = denDay, KakeDay = denDay, IsStock = isStock, IsPay = isPay, Jmeisai = meisai, Vdc = 1, Vdu = 1 };
		header.Kubun = kubun;
		Db.Insert(header);
		return header.Id;
	}

	private void InsertOpeningStock(string sumMonth, long idShohin, int su) {
		Db.Insert(new SummaryStock { SumMonth = sumMonth, Id_Soko = 1, Id_Shohin = idShohin, Su = su, Vdc = 1, Vdu = 1 });
	}

	private static CostUpdateParameter NewParam(string targetMonth, long idShain = 0, string batchId = "B1") => new() {
		TargetMonth = targetMonth, Id_Shain = idShain, BatchId = batchId,
	};

	// ------------------------------------------------------------------
	// 1. 排他行がある状態でApplyTotalAverageCostを呼ぶと、IsSuccess=falseでDBが一切変更されない。
	//    Messageに先行処理名が含まれる
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyTotalAverageCost_LockHeld_FailsWithoutChangingDb() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 5000);
		InsertOpeningStock("202608", idShohin, su: 10);
		InsertPurchase("20260910", 10, idShohin, su: 14, kingaku: 68000);

		// 先行処理が排他を握っている状態を模す
		new ManualLockDb(Db).TryBegin("先行一連処理", "処理中", 600, "先行メモ");

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		StringAssert.Contains(result.Message, "先行一連処理");
		StringAssert.Contains(result.Message, "処理中");
		// DBは一切変更されない
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
		Assert.AreEqual(5000, Db.FirstOrDefault<MasterShohin>("WHERE Id=@0", idShohin)!.TankaGenka);
		// 排他行は先行の1行のまま(今回のApplyは行を作っていない)
		var locks = Db.Fetch<SysSequence>($"WHERE SysSeqType={(int)EmSysSeqType.ManualLock}");
		Assert.AreEqual(1, locks.Count);
		Assert.AreEqual("先行一連処理", locks[0].TableName);
	}

	// ------------------------------------------------------------------
	// 2. 排他行がある状態でもPreviewTotalAverageCostは通る(Previewは対象外)
	// ------------------------------------------------------------------

	[TestMethod]
	public void PreviewTotalAverageCost_LockHeld_StillWorks() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 5000);
		InsertOpeningStock("202608", idShohin, su: 10);
		InsertPurchase("20260910", 10, idShohin, su: 14, kingaku: 68000);

		new ManualLockDb(Db).TryBegin("先行一連処理", "処理中", 600);

		var preview = new CostUpdateDb(Db).PreviewTotalAverageCost(NewParam("202609", idShain));

		Assert.AreEqual(1, preview.Count);
		Assert.AreEqual(4916, preview[0].AfterCost);
	}

	// ------------------------------------------------------------------
	// 3. ApplyTotalAverageCostが正常終了すると、SysSequenceの行が消え、
	//    SysHistAutoexecにSysHistType=1の行が1件増える
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyTotalAverageCost_Success_ClearsLockAndAddsManualHistory() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 5000);
		InsertOpeningStock("202608", idShohin, su: 10);
		InsertPurchase("20260910", 10, idShohin, su: 14, kingaku: 68000);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<SysSequence>($"WHERE SysSeqType={(int)EmSysSeqType.ManualLock}").Count);
		var histories = Db.Fetch<SysHistAutoexec>($"WHERE SysHistType={(int)EmSysHistType.ManualExec}");
		Assert.AreEqual(1, histories.Count);
		Assert.AreEqual("総平均原価更新", histories[0].TaskName);
		Assert.AreEqual(0, histories[0].ReturnCode);
		Assert.AreEqual(1, histories[0].Count);
	}

	// ------------------------------------------------------------------
	// 4. ApplyTotalAverageCostが業務エラー(負在庫)で失敗したとき、
	//    SysSequenceの行が残る(監視タスクが解放する対象になる)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyTotalAverageCost_BusinessError_LeavesLockRowForMonitor() {
		// 設計書§6.5「OpeningQty<0はエラー」。業務エラーはCompleteを呼ばず、行を残す方針(ManualLockHandle参照)
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idBad = InsertShohin("BAD", tankaGenka: 100);
		InsertPurchase("20260910", 10, idBad, su: 10, kingaku: 1000);
		InsertOpeningStock("202608", idBad, su: -5);

		var result = new CostUpdateDb(Db).ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(0, Db.Fetch<TranGenka>().Count);
		// 排他行が残っている(異常終了として監視タスク/強制クリアで解放される対象)
		var locks = Db.Fetch<SysSequence>($"WHERE SysSeqType={(int)EmSysSeqType.ManualLock}");
		Assert.AreEqual(1, locks.Count);
		Assert.AreEqual("総平均原価更新", locks[0].TableName);
		// Completeを呼んでいないため、手動実行履歴は残らない
		Assert.AreEqual(0, Db.Fetch<SysHistAutoexec>($"WHERE SysHistType={(int)EmSysHistType.ManualExec}").Count);
	}

	// ------------------------------------------------------------------
	// 8. 後続月再計算カスケード(設計書§6.6)で、月ごとにProgressが呼ばれ
	//    SysSequence.ColumnNameとVduが前進すること。行は完了時に消えるため、
	//    完了履歴(SysHistAutoexec.Memo)に各月の進捗記録が残っていることで間接的に確認する
	// ------------------------------------------------------------------

	[TestMethod]
	public void ApplyTotalAverageCost_SuccessorMonthCascade_ProgressesEachMonth() {
		CreateCostTables((int)EnumCostMethod.TotalAverage);
		var idShain = InsertShain();
		var idShohin = InsertShohin("A1", tankaGenka: 100);
		var costUpdateDb = new CostUpdateDb(Db);

		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 1000);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain)).IsSuccess);

		InsertOpeningStock("202609", idShohin, su: 10);
		InsertPurchase("20261010", 10, idShohin, su: 10, kingaku: 2000);
		Assert.IsTrue(costUpdateDb.ApplyTotalAverageCost(NewParam("202610", idShain)).IsSuccess);

		// 202609の仕入を修正して再実行 → 202609・202610の両方が再計算されるカスケードになる(設計書§6.6)
		Db.Execute($"DELETE FROM {nameof(Tran03Shiire)} WHERE DenDay=@0", "20260910");
		InsertPurchase("20260910", 10, idShohin, su: 10, kingaku: 2000);

		var rerun = costUpdateDb.ApplyTotalAverageCost(NewParam("202609", idShain));

		Assert.IsTrue(rerun.IsSuccess);
		// 完了時にSysSequenceの行は消える
		Assert.AreEqual(0, Db.Fetch<SysSequence>($"WHERE SysSeqType={(int)EmSysSeqType.ManualLock}").Count);
		// 直近の手動実行履歴のMemoに、カスケード対象の2か月ぶんの進捗記録(ColumnNameに積んだ内容)が残っている
		var history = Db.Fetch<SysHistAutoexec>($"WHERE SysHistType={(int)EmSysHistType.ManualExec} ORDER BY Id DESC LIMIT 1").Single();
		StringAssert.Contains(history.Memo, "202609");
		StringAssert.Contains(history.Memo, "202610");
		StringAssert.Contains(history.Memo, "総平均原価再計算");
	}
}
