using System;
using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 上代一括変更 Step4後半の回帰テスト。<see cref="JodaiConflictChecker"/>（C4・C6・C7・C8。DB参照が要るもの）を検証する。
/// <para>
/// 伝票内だけで完結するC1・C2・C5は<see cref="JodaiScopeResolver"/>（<c>CvBase</c>、別テスト）が担当し、
/// C3は<see cref="TranJodai.Normalize"/>（<see cref="JodaiExpandTests"/>）が担当するため、ここでは扱わない。
/// 仕様は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 2.8・6.1。
/// </para>
/// </summary>
[TestClass]
public class JodaiConflictCheckerTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private JodaiConflictChecker? _checker;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");
	private JodaiConflictChecker Checker => _checker ?? throw new AssertFailedException("Checker not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"JodaiConflictCheckerTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		Db.CreateTable(typeof(TranJodai), true, false);
		Db.CreateTable(typeof(DerivedJodai), true, true);
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(MasterConfig), true, false);
		_checker = new JodaiConflictChecker(Db);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	// ============================================================
	// C4: 他伝票との競合
	// ============================================================

	[TestMethod]
	public void C4_他伝票の確定済みDerivedJodaiと期間が重なる_検出する() {
		var idShohin = InsertShohin("P1", "商品1");
		InsertDerived(idTran: 999, idShohin: idShohin, idTenpo: 101, dayFrom: "20260910", dayTo: "20260920", jodai: 6900);
		var tran = MakeTran(id: 1, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");

		var conflicts = Checker.CheckOtherSlipConflict(tran);

		Assert.AreEqual(1, conflicts.Count);
		Assert.AreEqual(EnumJodaiConflictKind.OtherSlipConflict, conflicts[0].Kind);
		Assert.AreEqual(EnumJodaiConflictSeverity.Warning, conflicts[0].Severity);
		StringAssert.Contains(conflicts[0].Message, "P1");
	}

	[TestMethod]
	public void C4_他伝票と期間が重ならない_検出しない() {
		var idShohin = InsertShohin("P1", "商品1");
		InsertDerived(idTran: 999, idShohin: idShohin, idTenpo: 101, dayFrom: "20261001", dayTo: "20261010", jodai: 6900);
		var tran = MakeTran(id: 1, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");

		var conflicts = Checker.CheckOtherSlipConflict(tran);

		Assert.AreEqual(0, conflicts.Count);
	}

	[TestMethod]
	public void C4_自伝票の行は検出しない() {
		var idShohin = InsertShohin("P1", "商品1");
		var tran = MakeTran(id: 42, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");
		InsertDerived(idTran: 42, idShohin: idShohin, idTenpo: 101, dayFrom: "20260910", dayTo: "20260920", jodai: 6900);

		var conflicts = Checker.CheckOtherSlipConflict(tran);

		Assert.AreEqual(0, conflicts.Count);
	}

	[TestMethod]
	public void C4_Id_Tenpoが0_全件ワイルドカードの他伝票行も検出される() {
		var idShohin = InsertShohin("P1", "商品1");
		InsertDerived(idTran: 999, idShohin: idShohin, idTenpo: 0, dayFrom: "20260910", dayTo: "20260920", jodai: 5900);
		var tran = MakeTran(id: 1, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");

		var conflicts = Checker.CheckOtherSlipConflict(tran);

		Assert.AreEqual(1, conflicts.Count);
		StringAssert.Contains(conflicts[0].Message, "5900");
	}

	[TestMethod]
	public void 空の伝票_JshopとJmeisaiが空_例外を投げない() {
		var tran = new TranJodai { Id = 1 };

		var c4 = Checker.CheckOtherSlipConflict(tran);
		var c6 = Checker.CheckProperBaselineMismatch(tran);
		var c7 = Checker.CheckBelowCost(tran);
		var c8 = Checker.CheckBelowMinPrice(tran);

		Assert.AreEqual(0, c4.Count);
		Assert.AreEqual(0, c6.Count);
		Assert.AreEqual(0, c7.Count);
		Assert.AreEqual(0, c8.Count);
	}

	// ============================================================
	// C6: 恒久上代変更との基準不整合
	// ============================================================

	[TestMethod]
	public void C6_期間内にKubunProperの有効な伝票がある_検出する() {
		var idShohin = InsertShohin("P1", "商品1");
		InsertDerived(idTran: 999, idShohin: idShohin, idTenpo: 101, dayFrom: "20260101", dayTo: "99991231", jodai: 9800, kubun: (int)EnumJodaiKubun.Proper);
		var tran = MakeTran(id: 1, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");

		var conflicts = Checker.CheckProperBaselineMismatch(tran);

		Assert.AreEqual(1, conflicts.Count);
		Assert.AreEqual(EnumJodaiConflictKind.ProperBaselineMismatch, conflicts[0].Kind);
		Assert.AreEqual(EnumJodaiConflictSeverity.Warning, conflicts[0].Severity);
	}

	[TestMethod]
	public void C6_KubunProperの伝票が無い_検出しない() {
		var idShohin = InsertShohin("P1", "商品1");
		InsertDerived(idTran: 999, idShohin: idShohin, idTenpo: 101, dayFrom: "20260101", dayTo: "99991231", jodai: 9800, kubun: (int)EnumJodaiKubun.Sale);
		var tran = MakeTran(id: 1, idShohin: idShohin, idTenpo: 101, dayFrom: "20260901", dayTo: "20260930");

		var conflicts = Checker.CheckProperBaselineMismatch(tran);

		Assert.AreEqual(0, conflicts.Count);
	}

	// ============================================================
	// C7: 原価割れ
	// ============================================================

	[TestMethod]
	public void C7_明細のTankaGenka時点値がある_それを使う() {
		var idShohin = InsertShohin("P1", "商品1", tankaGenka: 500);
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = idShohin, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 900, TankaGenka = 1000 });

		var conflicts = Checker.CheckBelowCost(tran);

		// 明細の時点値(1000)を使うので、900<1000で原価割れ（マスタの500を使えば割れない）
		Assert.AreEqual(1, conflicts.Count);
		Assert.AreEqual(EnumJodaiConflictKind.BelowCost, conflicts[0].Kind);
		StringAssert.Contains(conflicts[0].Message, "1000");
	}

	[TestMethod]
	public void C7_明細のTankaGenkaが0_マスタのTankaGenkaを引く() {
		var idShohin = InsertShohin("P1", "商品1", tankaGenka: 1000);
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = idShohin, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 900, TankaGenka = 0 });

		var conflicts = Checker.CheckBelowCost(tran);

		Assert.AreEqual(1, conflicts.Count);
		StringAssert.Contains(conflicts[0].Message, "1000");
	}

	[TestMethod]
	public void C7_原価割れが無ければ検出しない() {
		var idShohin = InsertShohin("P1", "商品1", tankaGenka: 500);
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = idShohin, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 900, TankaGenka = 0 });

		var conflicts = Checker.CheckBelowCost(tran);

		Assert.AreEqual(0, conflicts.Count);
	}

	// ============================================================
	// C8: 最低販売価格違反
	// ============================================================

	[TestMethod]
	public void C8_JodaiMinPriceが0なら判定しない() {
		InsertConfig(MasterConfig.NameJodaiMinPrice, "0");
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = 201, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 100 });

		var conflicts = Checker.CheckBelowMinPrice(tran);

		Assert.AreEqual(0, conflicts.Count);
	}

	[TestMethod]
	public void C8_設定値未満の明細があれば検出する() {
		InsertConfig(MasterConfig.NameJodaiMinPrice, "500");
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = 201, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 100 });

		var conflicts = Checker.CheckBelowMinPrice(tran);

		Assert.AreEqual(1, conflicts.Count);
		Assert.AreEqual(EnumJodaiConflictKind.BelowMinPrice, conflicts[0].Kind);
		StringAssert.Contains(conflicts[0].Message, "500");
	}

	[TestMethod]
	public void C8_MasterConfigの行自体が無くても例外にならず判定しない() {
		var tran = new TranJodai { Id = 1 };
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = 201, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 100 });

		var conflicts = Checker.CheckBelowMinPrice(tran);

		Assert.AreEqual(0, conflicts.Count);
		Assert.AreEqual(0, Checker.GetJodaiMinPrice());
	}

	// ============================================================
	// ヘルパー
	// ============================================================

	private TranJodai MakeTran(long id, long idShohin, long idTenpo, string dayFrom, string dayTo) {
		var tran = new TranJodai {
			Id = id,
			TaishoType = (int)EnumJodaiTaisho.Tenpo,
			DayFrom = dayFrom,
			DayTo = dayTo,
		};
		tran.Jshop.Add(new TranJodaiShop { Id_Tenpo = idTenpo, DayFrom = dayFrom, DayTo = dayTo });
		tran.Jmeisai.Add(new TranJodaiMeisai { Id_Shohin = idShohin, Code_Shohin = "P1", Mei_Shohin = "商品1", JodaiNew = 900 });
		return tran;
	}

	/// <summary>
	/// MasterShohinはAutoIncrement PKのため、明示的にIdを指定してもDBが採番した値へ上書きされる
	/// （NPocoの規約）。生成された<see cref="MasterShohin.Id"/>を返すので、呼び出し側はそれを使うこと。
	/// </summary>
	private long InsertShohin(string code, string name, int tankaGenka = 0) {
		var row = new MasterShohin { Code = code, Name = name, TankaGenka = tankaGenka };
		Db.Insert(row);
		return row.Id;
	}

	private void InsertDerived(long idTran, long idShohin, long idTenpo, string dayFrom, string dayTo, int jodai, int kubun = (int)EnumJodaiKubun.Sale) =>
		Db.Insert(new DerivedJodai {
			TaishoType = (int)EnumJodaiTaisho.Tenpo,
			Id_Tenpo = idTenpo,
			Id_Shohin = idShohin,
			DayFrom = dayFrom,
			DayTo = dayTo,
			Kubun = kubun,
			Jodai = jodai,
			Id_Tran = idTran,
		});

	private void InsertConfig(string name, string val) =>
		Db.Insert(new MasterConfig { Category = MasterConfig.CategorySystem, Name = name, Val = val });
}
