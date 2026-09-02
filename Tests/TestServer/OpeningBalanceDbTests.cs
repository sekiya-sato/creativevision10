using System;
using System.Collections.Generic;
using System.Linq;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 期首残高の洗い替え登録（<see cref="OpeningBalanceDb"/>）のテスト。
/// <para>
/// `Doc/spec/archive/2026-08-21_残高登録処理_詳細設計.md` 5章の規則を固定する。
/// 冪等性（同一CSVの再取込で uk1 違反にならず件数が増えない）、期首ガード、
/// 許可テーブル、洗い替え範囲、ロールバックを対象にする。
/// </para>
/// </summary>
[TestClass]
public class OpeningBalanceDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"OpeningBalanceDbTests-{Guid.NewGuid():N}";
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

	[TestMethod]
	public void Import_RegistersOpeningBalanceForEachKind() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		var uriKake = target.Import(Param(nameof(SummaryUriKake), "202606", [1, 2],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 },
			 new SummaryUriKake { Id_Tokui = 2, DenMonth = "202606", Balance = 3000, TotalSales = 3000 }]));
		Assert.AreEqual(0, uriKake.Deleted);
		Assert.AreEqual(2, uriKake.Inserted);
		Assert.AreEqual(5000L, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606").Balance);

		var uriSei = target.Import(Param(nameof(SummaryUriSei), "20260630", [1],
			[new SummaryUriSei {
				Id_Tokui = 1, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630",
				Balance = 5000, TotalSales = 5000,
			}]));
		Assert.AreEqual(1, uriSei.Inserted);
		Assert.AreEqual(5000L, db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260630").Balance);

		var kaiKake = target.Import(Param(nameof(SummaryKaiKake), "202606", [11],
			[new SummaryKaiKake { Id_Shiire = 11, DenMonth = "202606", Balance = 7000, TotalShiire = 7000 }]));
		Assert.AreEqual(1, kaiKake.Inserted);
		Assert.AreEqual(7000L, db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 11, "202606").Balance);

		var kaiShi = target.Import(Param(nameof(SummaryKaiShi), "20260630", [11],
			[new SummaryKaiShi {
				Id_Shiire = 11, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630",
				Balance = 7000, TotalShiire = 7000,
			}]));
		Assert.AreEqual(1, kaiShi.Inserted);
		Assert.AreEqual(7000L, db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 11, "20260630").Balance);
	}

	[TestMethod]
	public void Import_SetsAuditValuesOnServer() {
		var db = Prepare();

		new OpeningBalanceDb(db).Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000, Vdc = 1, Vdu = 1 }]));

		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.IsTrue(row.Vdc > 1, "Vdcはサーバー側で採番する");
		Assert.AreEqual(row.Vdc, row.Vdu);
	}

	[TestMethod]
	public void Import_IsIdempotentForSameCsv() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);
		var param = Param(nameof(SummaryUriKake), "202606", [1, 2],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 },
			 new SummaryUriKake { Id_Tokui = 2, DenMonth = "202606", Balance = 3000, TotalSales = 3000 }]);

		target.Import(param);
		var second = target.Import(param);

		Assert.AreEqual(2, second.Deleted, "2回目は既存行を削除してから登録し直す");
		Assert.AreEqual(2, second.Inserted);
		Assert.AreEqual(2, db.Fetch<SummaryUriKake>("where DenMonth=@0", "202606").Count, "uk1違反にならず件数も増えない");
	}

	[TestMethod]
	public void Import_ReplacesOnlyListedOwners() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);
		target.Import(Param(nameof(SummaryUriKake), "202606", [1, 2],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 },
			 new SummaryUriKake { Id_Tokui = 2, DenMonth = "202606", Balance = 3000, TotalSales = 3000 }]));

		// 得意先1だけを載せた2回目。得意先2の期首残は触らない
		var result = target.Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 9000, TotalSales = 9000 }]));

		Assert.AreEqual(1, result.Deleted);
		Assert.AreEqual(1, result.Inserted);
		Assert.AreEqual(9000L, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606").Balance);
		Assert.AreEqual(3000L, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 2, "202606").Balance,
			"CSVに載っていない取引先の既存行は残す");
	}

	[TestMethod]
	public void Import_DeletesWhenNoRecordIsGiven() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);
		target.Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 }]));

		// 残高0（削除だけ）の取引先は OwnerIds に載るが行は無い
		var result = target.Import(Param<SummaryUriKake>(nameof(SummaryUriKake), "202606", [1], []));

		Assert.AreEqual(1, result.Deleted);
		Assert.AreEqual(0, result.Inserted);
		Assert.AreEqual(0, db.Fetch<SummaryUriKake>("where DenMonth=@0", "202606").Count);
	}

	[TestMethod]
	public void Import_KeepsOtherKeyDatesUntouched() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202605", Balance = 100, TotalSales = 100 });

		target.Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 }]));

		Assert.AreEqual(100L, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202605").Balance,
			"別のキー日付の行は対象外");
	}

	// ---- 拒否条件 ----------------------------------------------------------------

	[TestMethod]
	public void Import_RejectsKeyDateOnOrAfterFiscalStart() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		var ex = Assert.ThrowsExactly<ArgumentException>(() => target.Import(Param(nameof(SummaryUriKake), "202607", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202607", Balance = 5000, TotalSales = 5000 }])));

		StringAssert.Contains(ex.Message, "期首");
		Assert.AreEqual(0, db.Fetch<SummaryUriKake>("where DenMonth=@0", "202607").Count);
	}

	[TestMethod]
	public void Import_RejectsTableOutsideAllowList() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		var ex = Assert.ThrowsExactly<ArgumentException>(() => target.Import(
			new OpeningBalanceImportParam("MasterTokui", "202606", [1], "[]")));

		StringAssert.Contains(ex.Message, "対象テーブルではありません");
	}

	[TestMethod]
	public void Import_RejectsMalformedKeyDate() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		Assert.ThrowsExactly<ArgumentException>(() => target.Import(
			new OpeningBalanceImportParam(nameof(SummaryUriKake), "2026-06", [1], "[]")),
			"年月は6桁の数字だけを受け付ける");
		Assert.ThrowsExactly<ArgumentException>(() => target.Import(
			new OpeningBalanceImportParam(nameof(SummaryUriSei), "202606", [1], "[]")),
			"請求は8桁の年月日が必要");
	}

	[TestMethod]
	public void Import_RejectsEmptyOwnerIds() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		Assert.ThrowsExactly<ArgumentException>(() => target.Import(
			new OpeningBalanceImportParam(nameof(SummaryUriKake), "202606", [], "[]")));
	}

	[TestMethod]
	public void Import_RejectsRowOutsideKeyDateOrOwnerScope() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		var keyMismatch = Assert.ThrowsExactly<ArgumentException>(() => target.Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202605", Balance = 5000, TotalSales = 5000 }])));
		StringAssert.Contains(keyMismatch.Message, "一致しない");

		var ownerMismatch = Assert.ThrowsExactly<ArgumentException>(() => target.Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 9, DenMonth = "202606", Balance = 5000, TotalSales = 5000 }])));
		StringAssert.Contains(ownerMismatch.Message, "洗い替え対象外");
	}

	[TestMethod]
	public void Import_RejectsUnsetFiscalStartDate() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriKake), true, false);
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { FiscalStartDate = OpeningBalanceCsv.UnsetFiscalStartDate });

		var ex = Assert.ThrowsExactly<ArgumentException>(() => new OpeningBalanceDb(db).Import(
			Param(nameof(SummaryUriKake), "202606", [1],
				[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 }])));

		StringAssert.Contains(ex.Message, "期首日が未設定");
	}

	[TestMethod]
	public void Import_RollsBackEverythingWhenOneRowFails() {
		var db = Prepare();
		var target = new OpeningBalanceDb(db);

		// 同一キーの重複行は uk1 違反になる。1件目も残さない
		Assert.ThrowsExactly<SqliteException>(() => target.Import(Param(nameof(SummaryUriKake), "202606", [1, 2],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 },
			 new SummaryUriKake { Id_Tokui = 2, DenMonth = "202606", Balance = 3000, TotalSales = 3000 },
			 new SummaryUriKake { Id_Tokui = 2, DenMonth = "202606", Balance = 1000, TotalSales = 1000 }])));

		Assert.AreEqual(0, db.Fetch<SummaryUriKake>("where DenMonth=@0", "202606").Count, "途中失敗で1件も残さない");
	}

	// ---- 繰越との結合 ------------------------------------------------------------

	[TestMethod]
	public void Import_UriSei_OpeningRowIsIncludedInPreviousBalanceOfNextClosingPeriod() {
		// 繰越はテーブルに持たない(2.3)。期首残高CSVで投入した行は「期首直前の1期間の実績行」として
		// 残り続け、読み出し側の標準SQL(SUM(TotalSales - TotalIn) WHERE DayTo < 対象期間開始日)へ
		// 自然に取り込まれることを検証する。
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriSei), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		db.CreateTable(typeof(MasterTokui), true, false);
		db.CreateTable(typeof(MasterMeisho), true, false);
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { FiscalStartDate = "20260701", ShimeBi = 99 });
		db.Insert(new MasterTokui { Code = "00123", Shime1 = 99, TenType = 1 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "00123").Id;

		// 期首残 150,000(正=未回収) を 20260630 の請求行として投入する
		new OpeningBalanceDb(db).Import(Param(nameof(SummaryUriSei), "20260630", [idTokui],
			[new SummaryUriSei {
				Id_Tokui = idTokui, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630",
				Balance = 150000, TotalSales = 150000, TotalIn = 0,
			}]));

		// 期首以降の請求月を計算する。再計算は対象期間だけの純増減を作るのみで、期首行には触れない
		new SummaryDb(db).CalcSummaryUriSei("202607", 99);

		var opening = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idTokui, "20260630");
		Assert.AreEqual(150000L, opening.Balance, "期首前の行は再計算で上書きしない");

		var july = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idTokui, "20260731");
		Assert.AreEqual(0L, july.Balance, "7月に伝票が無いので当月分の純増減は0(繰越は積まない)");

		// PreviousBalance の標準SQL(7.3)。期首行だけが対象範囲(DayTo < 7月の開始日)に入る
		var previousBalance = db.FirstOrDefault<long>(
			"SELECT SUM(TotalSales - TotalIn) FROM SummaryUriSei WHERE Id_Tokui=@0 AND DayTo < @1", idTokui, july.DayFrom);
		Assert.AreEqual(150000L, previousBalance, "期首行がPreviousBalanceの累計に含まれる");
	}

	[TestMethod]
	public void Import_UriKake_OpeningRowIsIncludedInPreviousBalanceOfNextMonth() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriKake), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		db.CreateTable(typeof(MasterMeisho), true, false);
		db.CreateTable(typeof(MasterSysman), true, false);
		// CalcSummaryUriKake は税区分別のTaxRounding解決のためMasterTokuiをLEFT JOINする(3.5)。
		// 対象取引先が無くてもIFNULLで既定値になるため、テーブルさえあれば行の投入は不要。
		db.CreateTable(typeof(MasterTokui), true, false);
		db.Insert(new MasterSysman { FiscalStartDate = "20260701", ShimeBi = 99 });

		new OpeningBalanceDb(db).Import(Param(nameof(SummaryUriKake), "202606", [1],
			[new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 150000, TotalSales = 150000 }]));

		new SummaryDb(db).CalcSummaryUriKake("202607", "202607");

		var opening = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.AreEqual(150000L, opening.Balance, "期首前の行は再計算で上書きしない");
		Assert.IsNull(db.FirstOrDefault<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607"),
			"7月に伝票が無ければ行は作られない(繰越は積まない)");

		// PreviousBalance の標準SQL(7.3)。202606の期首行だけが対象範囲(DenMonth < 202607)に入る
		var previousBalance = db.FirstOrDefault<long>(
			"SELECT SUM(TotalSales - TotalIn) FROM SummaryUriKake WHERE Id_Tokui=@0 AND DenMonth < @1", 1, "202607");
		Assert.AreEqual(150000L, previousBalance, "期首行がPreviousBalanceの累計に含まれる");
	}

	// ---- 取引先照会SQL（実スキーマで通ること） -----------------------------------

	[TestMethod]
	public void BuildOwnerQuerySql_RunsAgainstRealSchemaForEveryKind() {
		var db = Prepare();
		db.CreateTable(typeof(MasterTokui), true, true);
		db.CreateTable(typeof(MasterShiire), true, true);
		db.Insert(new MasterTokui { Code = "00123", Name = "株式会社アルファ", Shime1 = 99, TenType = 1 });
		db.Insert(new MasterTokui { Code = "00900", Name = "直営店E", Shime1 = 99, TenType = 6 });
		db.Insert(new MasterShiire { Code = "S001", Name = "仕入先A", Shime1 = 99 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "00123").Id;
		db.Insert(new SummaryUriKake { Id_Tokui = idTokui, DenMonth = "202606", Balance = 5000, TotalSales = 5000 });

		foreach (var kind in new[] {
			EnumOpeningBalanceKind.UriKake, EnumOpeningBalanceKind.UriSei,
			EnumOpeningBalanceKind.KaiKake, EnumOpeningBalanceKind.KaiShi }) {
			var spec = OpeningBalanceCsv.GetSpec(kind);
			var keyDate = spec.IsClosingBased ? "20260630" : "202606";

			// 絞り込み無し（取込時のコード解決）と全絞り込み（テンプレート出力）の両方を実行する
			foreach (var scope in new[] {
				EnumOpeningBalanceOwnerScope.All,
				EnumOpeningBalanceOwnerScope.OwnerTypeFilter | EnumOpeningBalanceOwnerScope.ClosingFilter
					| EnumOpeningBalanceOwnerScope.CodeRange | EnumOpeningBalanceOwnerScope.ExistingOnly }) {
				var sql = OpeningBalanceCsv.BuildOwnerQuerySql(kind, scope);
				var rows = db.Fetch<OpeningBalanceOwnerRow>(sql, keyDate, string.Empty, string.Empty, 99);
				Assert.IsNotNull(rows, $"{kind} / {scope} のSQLが実行できない");
			}
		}
	}

	[TestMethod]
	public void BuildOwnerQuerySql_ReturnsExistingAmountAsPositive() {
		var db = Prepare();
		db.CreateTable(typeof(MasterTokui), true, true);
		db.Insert(new MasterTokui { Code = "00123", Name = "株式会社アルファ", Shime1 = 99, TenType = 1 });
		db.Insert(new MasterTokui { Code = "00124", Name = "ベータ商事", Shime1 = 99, TenType = 3 });
		db.Insert(new MasterTokui { Code = "00900", Name = "直営店E", Shime1 = 99, TenType = 6 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "00123").Id;
		db.Insert(new SummaryUriKake {
			Id_Tokui = idTokui, DenMonth = "202606", Balance = 5000,
			TotalSales = 6000, TotalIn = 1000, Uriage = 6000, Cash = 1000,
		});

		var sql = OpeningBalanceCsv.BuildOwnerQuerySql(
			EnumOpeningBalanceKind.UriKake, EnumOpeningBalanceOwnerScope.OwnerTypeFilter);
		var rows = db.Fetch<OpeningBalanceOwnerRow>(sql, "202606", string.Empty, string.Empty, 99);

		Assert.AreEqual(2, rows.Count, "TenType IN (1,3) だけを返す（直営店は除く）");
		var alpha = rows.Single(x => x.Code == "00123");
		Assert.AreEqual(1, alpha.HasExisting);
		Assert.AreEqual(5000L, alpha.Amount, "既存の期首残高は正数（未回収）で返す");
		Assert.AreEqual(6000L, alpha.Main);
		Assert.AreEqual(1000L, alpha.Cash);

		var beta = rows.Single(x => x.Code == "00124");
		Assert.AreEqual(0, beta.HasExisting);
		Assert.AreEqual(0L, beta.Amount);
	}

	[TestMethod]
	public void BuildOwnerQuerySql_ClosingFilter_MatchesOwnerFinalClosingDayOnly() {
		// 4.6: 締日フィルタは「最終締日(有効締日集合の最大値)が選択締日と一致するか」で絞る。
		// 中間の締日(20)では引っかからず、最終締日(99)でだけ拾えること。Shime1=0(未設定)は自社締日(20)へ
		// フォールバックすることも合わせて確認する。
		var db = Prepare();
		db.CreateTable(typeof(MasterTokui), true, true);
		// Prepare()が作るMasterSysman行(ShimeBi未設定)を更新する。挿入すると自社締日サブクエリ
		// (ORDER BY Id LIMIT 1)が拾うのは先に挿入済みの行になってしまう。
		db.Execute($"UPDATE {nameof(MasterSysman)} SET ShimeBi=@0", 20);
		db.Insert(new MasterTokui { Code = "00123", Name = "複数締日", Shime1 = 10, Shime2 = 20, Shime3 = 99, TenType = 1 });
		db.Insert(new MasterTokui { Code = "00124", Name = "単一締日20", Shime1 = 20, TenType = 1 });
		db.Insert(new MasterTokui { Code = "00125", Name = "未設定", Shime1 = 0, TenType = 1 });

		// 締日フィルタが効くのは請求・支払(IsClosingBased)だけ。売掛・買掛は締日を持たない(2.5)。
		var sql = OpeningBalanceCsv.BuildOwnerQuerySql(
			EnumOpeningBalanceKind.UriSei, EnumOpeningBalanceOwnerScope.ClosingFilter);

		var forFinalShime = db.Fetch<OpeningBalanceOwnerRow>(sql, "20260630", string.Empty, string.Empty, 99);
		CollectionAssert.AreEqual(new[] { "00123" }, forFinalShime.Select(x => x.Code).ToArray(),
			"最終締日(99)を選んだときだけ複数締日の得意先が拾える");

		var forMiddleShime = db.Fetch<OpeningBalanceOwnerRow>(sql, "20260630", string.Empty, string.Empty, 20);
		CollectionAssert.AreEqual(new[] { "00124", "00125" }, forMiddleShime.Select(x => x.Code).ToArray(),
			"中間の締日(20)では複数締日の得意先は拾えない。単一締日20と、未設定(自社締日20へフォールバック)の得意先だけ拾える");
	}

	// ---- ヘルパ ------------------------------------------------------------------

	/// <summary>
	/// 一意キー(uk1)を含むインデックスまで作る。期首残高の洗い替えは「再取込でuk1違反にならない」ことが
	/// 要点なので、この試験だけは実スキーマと同じ制約下で動かす。
	/// </summary>
	private ExDatabaseSqlite Prepare() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriKake), true, true);
		db.CreateTable(typeof(SummaryUriSei), true, true);
		db.CreateTable(typeof(SummaryKaiKake), true, true);
		db.CreateTable(typeof(SummaryKaiShi), true, true);
		db.CreateTable(typeof(MasterSysman), true, true);
		db.Insert(new MasterSysman { FiscalStartDate = "20260701" });
		return db;
	}

	private static OpeningBalanceImportParam Param<T>(string tableName, string keyDate, long[] ownerIds, List<T> items)
		where T : BaseDbClass =>
		new(tableName, keyDate, ownerIds, Common.SerializeObject(items));
}
