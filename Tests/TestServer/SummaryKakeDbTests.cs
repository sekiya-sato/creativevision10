using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeShare;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 売掛(<see cref="SummaryUriKake"/>) / 買掛(<see cref="SummaryKaiKake"/>)集計のテスト。
/// <para>
/// `Doc/spec/2026-08-18_請求計算・支払計算_詳細設計.md` 2.1 の確定ルールを固定する。
/// 区分別の正値内訳、`Total` / 明細金額の正値源、`IsPay` による除外、後続月再計算、`KakeDay` 基準を対象にする。
/// </para>
/// </summary>
[TestClass]
public class SummaryKakeDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"SummaryKakeDbTests-{System.Guid.NewGuid():N}";
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

	// ---- 売掛 --------------------------------------------------------------------

	[TestMethod]
	public void CalcSummaryUriKake_UsesTotalForPositiveBreakdownAndNegativeBalance() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateUriage("20260711", 1, EnumUri00.UriSale, 500, 50));
		db.Insert(CreateUriage("20260712", 1, EnumUri00.Henpin, 200, 20));
		db.Insert(CreateUriage("20260713", 1, EnumUri00.HenSale, 100, 10));
		db.Insert(CreateUriage("20260714", 1, EnumUri00.Nebiki, 300, 30));
		db.Insert(CreateUriage("20260715", 1, EnumUri00.Other, 700, 70));
		var range40 = CreateUriage("20260716", 1, EnumUri00.Uriage, 400, 40);
		range40.Kubun = 40;
		db.Insert(range40);
		db.Insert(CreateUriageWithKubun("20260717", 1, 19, 19, 1));
		db.Insert(CreateUriageWithKubun("20260718", 1, 29, 29, 2));
		db.Insert(CreateUriageWithKubun("20260719", 1, 39, 39, 3));
		db.Insert(CreateUriageWithKubun("20260720", 1, 89, 89, 4));
		db.Insert(CreateUriageWithKubun("20260721", 1, 90, 900, 5));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreNotEqual(range40.Total, range40.KingakuTotal);
		Assert.AreEqual(1000 + 500 + 400 + 19 + 89 + 700, row.Uriage, "区分99(その他売上)は売上へ畳み込む");
		Assert.AreEqual(200 + 100 + 29, row.Henpin);
		Assert.AreEqual(300 + 39, row.Nebiki);
		Assert.AreEqual(100 + 50 - 20 - 10 + 30 + 70 + 40 + 1 - 2 + 3 + 4 + 5, row.Tax1);
		Assert.AreEqual(row.Uriage - row.Henpin - row.Nebiki + row.Tax1, row.TotalSales);
		Assert.AreEqual(-row.TotalSales, row.Balance);
	}

	[TestMethod]
	public void CalcSummaryUriKake_DoesNotDoubleCountTaxWithTotal() {
		// 回帰テスト(仕様3.8): 伝票のTotalは税込(|KingakuTotal|+Tax1)なので、UriageをTotalで積んでからTax1を
		// 加算すると消費税が二重計上になる。税抜1000・消費税100の売上1件だけなら Uriage=1000 / Tax1=100 /
		// TotalSales=1100 になるべきで、Totalで集計してしまうと Uriage=1100 / TotalSales=1200 になってしまう。
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(1000, row.Uriage);
		Assert.AreEqual(100, row.Tax1);
		Assert.AreEqual(1100, row.TotalSales);
	}

	[TestMethod]
	public void CalcSummaryUriKake_ExcludesNotBilledSlips() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		var notBilled = CreateUriage("20260711", 1, EnumUri00.Uriage, 9999, 999);
		notBilled.IsPay = 0;
		db.Insert(notBilled);

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(1000, row.Uriage);
		Assert.AreEqual(1100, row.TotalSales);
	}

	[TestMethod]
	public void CalcSummaryUriKake_UsesJmeisaiAndFallsBackToOther() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateNyukin("20260710", 1, [
			(KinCash, 5000),
			(KinFee, 440),
			(KinDensai, 3000),
			(KinOffset, 200),
			(KinOther, 60),
			(KinUnknown, 8),
			// KIN マスタに存在しない Id_Kin。移行途中の 0 と同じ扱いで「その他」へ寄せる
			(0, 7),
		]));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(5000, row.Cash);
		Assert.AreEqual(440, row.Fee);
		Assert.AreEqual(3000, row.Densai);
		Assert.AreEqual(200, row.Offset);
		Assert.AreEqual(60 + 8 + 7, row.Other);
		Assert.AreNotEqual(5000 + 440 + 3000 + 200 + 60 + 8 + 7 + 10000, row.TotalIn);
		Assert.AreEqual(row.TotalIn, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
		Assert.AreEqual(5000 + 440 + 3000 + 200 + 60 + 8 + 7, row.TotalIn);
	}

	[TestMethod]
	public void CalcSummaryUriKake_HandlesInvalidJsonReceiptAsZero() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 0, 0));
		db.Insert(CreateNyukin("20260710", 1, [(KinCash, 999)]));
		db.Execute($"UPDATE {nameof(Tran06Nyukin)} SET Jmeisai=@0", "{");

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(0, row.TotalIn);
		Assert.AreEqual(0, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
	}

	[TestMethod]
	public void CalcSummaryUriKake_UsesKakeDayForReceipts() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		// 入金の日付列は 2026-08-16 に DenDay から KakeDay へ改名した。掛計上日の月へ入る
		db.Insert(CreateNyukin("20260805", 1, [(KinCash, 1200)]));

		summaryDb.CalcSummaryUriKake("202607", "202608");
		var july = db.FirstOrDefault<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");
		var august = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202608");

		Assert.IsNull(july);
		Assert.AreEqual(1200, august.TotalIn);
		Assert.AreEqual(1200, august.Balance);
	}

	[TestMethod]
	public void CalcSummaryUriKake_CarriesBalanceForwardAcrossMonths() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateNyukin("20260805", 1, [(KinCash, 400)]));

		summaryDb.CalcSummaryUriKake("202607", "202608");
		var rows = db.Fetch<SummaryUriKake>("where Id_Tokui=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(-1000, rows[0].Balance);
		Assert.AreEqual(-600, rows[1].Balance);
	}

	[TestMethod]
	public void CalcSummaryUriKake_UsesOwnClosingDayForDenMonth() {
		var db = PrepareUriKakeTables();
		db.Execute($"UPDATE {nameof(MasterSysman)} SET ShimeBi=@0", 20);
		db.Insert(CreateUriage("20260720", 1, EnumUri00.Uriage, 1, 0));
		db.Insert(CreateUriage("20260721", 1, EnumUri00.Uriage, 2, 0));
		db.Insert(CreateUriage("20260820", 1, EnumUri00.Uriage, 4, 0));
		db.Insert(CreateUriage("20260821", 1, EnumUri00.Uriage, 8, 0));

		new SummaryDb(db).CalcSummaryUriKake("202607", "202609");

		Assert.AreEqual(1, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607").Uriage);
		Assert.AreEqual(6, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202608").Uriage);
		Assert.AreEqual(8, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202609").Uriage);
	}

	[TestMethod]
	public void CalcSummaryUriKake_RecalculatesMonthsAfterTargetPeriod() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateUriage("20260810", 1, EnumUri00.Uriage, 500, 0));
		summaryDb.CalcSummaryUriKake("202607", "202608");
		Assert.AreEqual(-1500, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202608").Balance);

		// 7月の伝票を増やして7月だけを指定して再計算する。8月の繰越も追随しなければならない
		db.Insert(CreateUriage("20260720", 1, EnumUri00.Uriage, 300, 0));
		summaryDb.CalcSummaryUriKake("202607", "202607");
		var rows = db.Fetch<SummaryUriKake>("where Id_Tokui=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(-1300, rows[0].Balance);
		Assert.AreEqual(-1800, rows[1].Balance);
	}

	[TestMethod]
	public void CalcSummaryUriKake_RecalculationIsIdempotent() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateUriage("20260712", 1, EnumUri00.Henpin, 200, 20));
		db.Insert(CreateNyukin("20260715", 1, [(KinCash, 300), (KinFee, 40)]));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var first = GetUriKakeSnapshot(db);
		summaryDb.CalcSummaryUriKake("202607", "202607");
		var second = GetUriKakeSnapshot(db);

		CollectionAssert.AreEqual(first, second);
	}

	[TestMethod]
	public void CalcSummaryUriKake_FreezesPreFiscalOpeningBalanceAndSeedsCarryForward() {
		var db = PrepareUriKakeTables();
		AddFiscalStartDate(db, "20260701"); // 期首 = 2026年7月
		var summaryDb = new SummaryDb(db);

		// 期首前(202606)に期首売掛残をCSV取込相当で投入した状態
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = -5000, TotalSales = 5000 });
		// 期首前の伝票は集計対象外
		db.Insert(CreateUriage("20260620", 1, EnumUri00.Uriage, 9999, 0));
		// 当月(202607)の伝票
		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));

		// 期首をまたぐ範囲を指定しても開始は期首月へ切り上がる
		summaryDb.CalcSummaryUriKake("202605", "202607");

		var opening = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.AreEqual(-5000, opening.Balance, "期首前の残は再計算で上書きしてはいけない");
		Assert.IsNull(db.FirstOrDefault<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202605"), "期首前の月に行を作ってはいけない");

		var july = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");
		Assert.AreEqual(1000, july.Uriage, "期首前(202606)の伝票は集計されない");
		Assert.AreEqual(-6000, july.Balance, "期首残 -5000 に当月 -1000 が積み上がる");
	}

	[TestMethod]
	public void CalcSummaryUriKake_SkipsRangeEntirelyBeforeFiscalStart() {
		var db = PrepareUriKakeTables();
		AddFiscalStartDate(db, "20260701");
		var summaryDb = new SummaryDb(db);
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = -5000 });
		db.Insert(CreateUriage("20260620", 1, EnumUri00.Uriage, 1000, 0));

		var count = summaryDb.CalcSummaryUriKake("202605", "202606");

		Assert.AreEqual(0, count, "期首前だけの範囲は再計算しない");
		var opening = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.AreEqual(-5000, opening.Balance, "期首前の残は変更されない");
	}

	// ---- 請求残 ------------------------------------------------------------------

	[TestMethod]
	public void CalcSummaryUriSei_CalculatesPeriodBreakdownBalanceAndDueDay() {
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(new MasterTokui { Code = "B001", Shime1 = 20, PayMonth = 1, PayDay = 15 });
		db.Insert(new SummaryUriSei {
			Id_Tokui = 1, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630", TotalSales = 500,
		});
		db.Insert(CreateBillingUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateBillingUriage("20260711", 1, EnumUri00.Henpin, 200, 20));
		db.Insert(CreateBillingUriage("20260712", 1, EnumUri00.Nebiki, 100, 10));
		db.Insert(CreateNyukin("20260715", 1, [(KinCash, 300), (KinFee, 40)]));

		summaryDb.CalcSummaryUriSei("202607", 99, "A001", "A999");
		var row = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");

		Assert.AreEqual("20260701", row.DayFrom);
		Assert.AreEqual("20260731", row.DayTo);
		Assert.AreEqual(1000, row.Uriage);
		Assert.AreEqual(200, row.Henpin);
		Assert.AreEqual(100, row.Nebiki);
		Assert.AreEqual(90, row.Tax1);
		Assert.AreEqual(790, row.TotalSales);
		Assert.AreEqual(340, row.TotalIn);
		Assert.AreEqual(-950, row.Balance);
		Assert.AreEqual("1-20260731-01", row.SeikyuNo);
		Assert.AreEqual(1, row.Renban);
		Assert.AreEqual("20260731", row.NyukinYoteiDay);
		Assert.IsNull(db.FirstOrDefault<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 2, "20260720"));
	}

	[TestMethod]
	public void CalcSummaryUriSei_PreviousBalanceIsRecoveredByAddingSalesAndSubtractingPayments() {
		// 請求書印刷(SeikyuBalanceDetailViewModel)の「前回残高」は当月残高から当月増減を戻して算出する。
		// Balance = 前回残高 + TotalIn - TotalSales で作られるので、逆算は Balance + TotalSales - TotalIn。
		// 符号を逆にすると当月増減が2回効いてしまうため、その式をここで固定する。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(CreateBillingUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateNyukin("20260715", 1, [(KinCash, 300)]));
		db.Insert(CreateBillingUriage("20260810", 1, EnumUri00.Uriage, 2000, 200));
		db.Insert(CreateNyukin("20260815", 1, [(KinCash, 500)]));

		summaryDb.CalcSummaryUriSei("202607", 99, "A001", "A999");
		summaryDb.CalcSummaryUriSei("202608", 99, "A001", "A999");

		var july = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");
		var august = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260831");

		// 帳票が使う式をSQLとして実行し、前月の当月残高と一致することを確かめる
		var prevBalance = db.FirstOrDefault<long>(
			"SELECT s.Balance + s.TotalSales - s.TotalIn FROM SummaryUriSei s WHERE s.Id_Tokui=@0 AND s.DenDay=@1",
			1, "20260831");
		Assert.AreEqual(july.Balance, prevBalance, "前回残高は前月の当月残高と一致しなければならない");

		// 符号を逆にした式（旧実装）は当月増減を2回効かせるので一致しない
		var wrongBalance = db.FirstOrDefault<long>(
			"SELECT s.Balance - s.TotalSales + s.TotalIn FROM SummaryUriSei s WHERE s.Id_Tokui=@0 AND s.DenDay=@1",
			1, "20260831");
		Assert.AreNotEqual(july.Balance, wrongBalance, "符号を逆にした式が偶然一致するテストデータでは検証にならない");
		Assert.AreEqual(july.Balance + 2 * (august.TotalIn - august.TotalSales), wrongBalance);
	}

	[TestMethod]
	public void CalcSummaryUriSei_SeparatesKubun99AsSonotaWithoutFoldingIntoUriage() {
		// E11: 区分99(その他売上)は請求残(SummaryUriSei)ではSonotaへ分離集計し、TotalSalesにも加算する。
		// 一方、CalcSummaryUriKake_UsesTotalForPositiveBreakdownAndNegativeBalance が示す通り
		// 掛集計(SummaryUriKake)側のUriage畳み込みは変更しない。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(CreateBillingUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateBillingUriage("20260711", 1, EnumUri00.Henpin, 200, 20));
		db.Insert(CreateBillingUriage("20260712", 1, EnumUri00.Nebiki, 100, 10));
		db.Insert(CreateBillingUriage("20260713", 1, EnumUri00.Other, 300, 30));

		summaryDb.CalcSummaryUriSei("202607", 99);
		var row = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");

		Assert.AreEqual(1000, row.Uriage, "売上金額に区分99を含めてはいけない");
		Assert.AreEqual(300, row.Sonota, "区分99は独立してSonotaへ分離集計する");
		Assert.AreEqual(120, row.Tax1);
		Assert.AreEqual(row.Uriage - row.Henpin - row.Nebiki + row.Sonota + row.Tax1, row.TotalSales);
		Assert.AreEqual(1000 - 200 - 100 + 300 + 120, row.TotalSales);
	}

	[TestMethod]
	public void CalcSummaryUriSei_RecalculationKeepsInvoiceNumberAndRenban() {
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 1, PayDay = 15 });
		db.Insert(CreateBillingUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));

		summaryDb.CalcSummaryUriSei("202607", 99);
		var first = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");
		first.Renban = 2;
		first.SeikyuNo = "1-20260731-02";
		db.Update(first);
		summaryDb.CalcSummaryUriSei("202607", 99);
		var second = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");

		Assert.AreEqual(2, second.Renban);
		Assert.AreEqual("1-20260731-02", second.SeikyuNo);
		Assert.AreEqual("20260815", second.NyukinYoteiDay);

		summaryDb.CalcSummaryUriSei("202607", 99, isReissue: true);
		var reissued = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");
		Assert.AreEqual(3, reissued.Renban);
		Assert.AreEqual("1-20260731-03", reissued.SeikyuNo);
	}

	[TestMethod]
	public void CalcSummaryUriSei_ExcludesPreFiscalSlipsAndKeepsOpeningRow() {
		var db = PrepareUriSeiTables();
		AddFiscalStartDate(db, "20260701"); // 期首 = 2026年7月
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 20, PayMonth = 1, PayDay = 15 });
		// 期首前にCSV取込相当で投入した期首請求残
		db.Insert(new SummaryUriSei { Id_Tokui = 1, DenDay = "20260620", DayFrom = "20260521", DayTo = "20260620", TotalSales = 3000 });
		// 202607(締日20)の期間は 20260621-20260720。期首(20260701)前の伝票は除外される
		db.Insert(CreateBillingUriage("20260625", 1, EnumUri00.Uriage, 500, 0));
		db.Insert(CreateBillingUriage("20260705", 1, EnumUri00.Uriage, 1000, 0));

		summaryDb.CalcSummaryUriSei("202607", 20, "A001", "A999");

		var opening = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260620");
		Assert.AreEqual(3000, opening.TotalSales, "期首前の請求残は再計算で削除・変更してはいけない");

		var row = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260720");
		Assert.AreEqual(1000, row.Uriage, "期首前(20260625)の伝票は集計されない");
	}

	[TestMethod]
	public async Task SummaryUriSeiAsyncStream_ReportsCompletion() {
		var db = PrepareUriSeiTables();
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		var completed = false;

		await foreach (var progress in new SummaryDb(db).SummaryUriSeiAsyncStream(new BillingParameter("202607", 99, "", ""))) {
			Assert.IsFalse(progress.IsError, progress.ErrorMessage);
			completed |= progress.IsCompleted;
		}

		Assert.IsTrue(completed);
	}

	// ---- 買掛 --------------------------------------------------------------------

	[TestMethod]
	public void CalcSummaryKaiKake_UsesTotalForPositiveBreakdownAndNegativeBalance() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 100));
		db.Insert(CreateShiire("20260711", 1, EnumShiire.Henpin, 200, 20));
		db.Insert(CreateShiire("20260712", 1, EnumShiire.Nebiki, 100, 10));
		db.Insert(CreateShiire("20260713", 1, EnumShiire.Other, 400, 40));
		db.Insert(CreateShiharai("20260714", 1, [(KinCash, 600), (KinOffset, 50)]));
		var range40 = CreateShiire("20260715", 1, EnumShiire.Shiire, 400, 40);
		range40.Kubun = 40;
		db.Insert(range40);
		db.Insert(CreateShiireWithKubun("20260716", 1, 19, 19, 1));
		db.Insert(CreateShiireWithKubun("20260717", 1, 29, 29, 2));
		db.Insert(CreateShiireWithKubun("20260718", 1, 39, 39, 3));
		db.Insert(CreateShiireWithKubun("20260719", 1, 89, 89, 4));
		db.Insert(CreateShiireWithKubun("20260720", 1, 90, 900, 5));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreNotEqual(range40.Total, range40.KingakuTotal);
		Assert.AreEqual(1000 + 400 + 19 + 89 + 400, row.Shiire, "区分99(その他仕入)は仕入へ畳み込む");
		Assert.AreEqual(200 + 29, row.Henpin);
		Assert.AreEqual(100 + 39, row.Nebiki);
		Assert.AreEqual(100 - 20 + 10 + 40 + 40 + 1 - 2 + 3 + 4 + 5, row.Tax1);
		Assert.AreEqual(row.Shiire - row.Henpin - row.Nebiki + row.Tax1, row.TotalShiire);
		Assert.AreEqual(600, row.Cash);
		Assert.AreEqual(50, row.Offset);
		Assert.AreEqual(row.TotalOut, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
		Assert.AreEqual(row.TotalOut - row.TotalShiire, row.Balance);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_ExcludesNotBilledSlips() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		var notBilled = CreateShiire("20260711", 1, EnumShiire.Shiire, 9999, 0);
		notBilled.IsPay = 0;
		db.Insert(notBilled);

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(1000, row.Shiire);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_UsesJmeisaiAndFallsBackToOther() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiharai("20260710", 1, [
			(KinCash, 5000),
			(KinFee, 440),
			(KinDensai, 3000),
			(KinOffset, 200),
			(KinOther, 60),
			(KinUnknown, 8),
			(0, 7),
		]));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(5000, row.Cash);
		Assert.AreEqual(440, row.Fee);
		Assert.AreEqual(3000, row.Densai);
		Assert.AreEqual(200, row.Offset);
		Assert.AreEqual(60 + 8 + 7, row.Other);
		Assert.AreNotEqual(5000 + 440 + 3000 + 200 + 60 + 8 + 7 + 10000, row.TotalOut);
		Assert.AreEqual(row.TotalOut, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
		Assert.AreEqual(5000 + 440 + 3000 + 200 + 60 + 8 + 7, row.TotalOut);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_HandlesInvalidJsonPaymentAsZero() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 0, 0));
		db.Insert(CreateShiharai("20260710", 1, [(KinCash, 999)]));
		db.Execute($"UPDATE {nameof(Tran07Shiharai)} SET Jmeisai=@0", "{");

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(0, row.TotalOut);
		Assert.AreEqual(0, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_CarriesBalanceForwardAcrossMonths() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiharai("20260805", 1, [(KinCash, 400)]));

		summaryDb.CalcSummaryKaiKake("202607", "202608");
		var rows = db.Fetch<SummaryKaiKake>("where Id_Shiire=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(-1000, rows[0].Balance);
		Assert.AreEqual(-600, rows[1].Balance);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_UsesOwnClosingDayForDenMonth() {
		var db = PrepareKaiKakeTables();
		db.Execute($"UPDATE {nameof(MasterSysman)} SET ShimeBi=@0", 20);
		db.Insert(CreateShiire("20260720", 1, EnumShiire.Shiire, 1, 0));
		db.Insert(CreateShiire("20260721", 1, EnumShiire.Shiire, 2, 0));
		db.Insert(CreateShiire("20260820", 1, EnumShiire.Shiire, 4, 0));
		db.Insert(CreateShiire("20260821", 1, EnumShiire.Shiire, 8, 0));

		new SummaryDb(db).CalcSummaryKaiKake("202607", "202609");

		Assert.AreEqual(1, db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607").Shiire);
		Assert.AreEqual(6, db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202608").Shiire);
		Assert.AreEqual(8, db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202609").Shiire);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_RecalculatesMonthsAfterTargetPeriod() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiire("20260810", 1, EnumShiire.Shiire, 500, 0));
		summaryDb.CalcSummaryKaiKake("202607", "202608");
		Assert.AreEqual(-1500, db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202608").Balance);

		db.Insert(CreateShiire("20260720", 1, EnumShiire.Shiire, 300, 0));
		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var rows = db.Fetch<SummaryKaiKake>("where Id_Shiire=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(-1300, rows[0].Balance);
		Assert.AreEqual(-1800, rows[1].Balance);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_RecalculationIsIdempotent() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 100));
		db.Insert(CreateShiire("20260712", 1, EnumShiire.Henpin, 200, 20));
		db.Insert(CreateShiharai("20260715", 1, [(KinCash, 300), (KinFee, 40)]));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var first = GetKaiKakeSnapshot(db);
		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var second = GetKaiKakeSnapshot(db);

		CollectionAssert.AreEqual(first, second);
	}

	// ---- 支払残 ------------------------------------------------------------------

	[TestMethod]
	public void CalcSummaryKaiShi_CalculatesPeriodBreakdownBalanceAndDueDay() {
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 20, PayMonth = 0, PayDay = 0 });
		db.Insert(new MasterShiire { Code = "B001", Shime1 = 99, PayMonth = 1, PayDay = 15 });
		db.Insert(new SummaryKaiShi {
			Id_Shiire = 1, DenDay = "20260620", DayFrom = "20260521", DayTo = "20260620", TotalShiire = 500,
		});
		db.Insert(CreateBillingShiire("20260710", 1, EnumShiire.Shiire, 1000, 100));
		db.Insert(CreateBillingShiire("20260711", 1, EnumShiire.Henpin, 200, 20));
		db.Insert(CreateBillingShiire("20260712", 1, EnumShiire.Nebiki, 100, 10));
		db.Insert(CreateBillingShiire("20260721", 1, EnumShiire.Shiire, 999, 0));
		db.Insert(CreateShiharai("20260715", 1, [(KinCash, 300), (KinFee, 40)]));

		summaryDb.CalcSummaryKaiShi("202607", 20, "A001", "A999");
		var row = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 1, "20260720");

		Assert.AreEqual("20260621", row.DayFrom);
		Assert.AreEqual("20260720", row.DayTo);
		Assert.AreEqual(1000, row.Shiire);
		Assert.AreEqual(200, row.Henpin);
		Assert.AreEqual(100, row.Nebiki);
		Assert.AreEqual(90, row.Tax1);
		Assert.AreEqual(790, row.TotalShiire);
		Assert.AreEqual(340, row.TotalOut);
		Assert.AreEqual(-950, row.Balance);
		Assert.AreEqual("20260731", row.ShiharaiYoteiDay);
		Assert.IsNull(db.FirstOrDefault<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 2, "20260731"));
	}

	[TestMethod]
	public void CalcSummaryKaiShi_RecalculationIsIdempotent() {
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 99, PayMonth = 1, PayDay = 15 });
		db.Insert(CreateBillingShiire("20260710", 1, EnumShiire.Shiire, 1000, 100));

		summaryDb.CalcSummaryKaiShi("202607", 99);
		var first = GetKaiShiSnapshot(db);
		summaryDb.CalcSummaryKaiShi("202607", 99);
		var second = GetKaiShiSnapshot(db);

		CollectionAssert.AreEqual(first, second);
	}

	[TestMethod]
	public async Task SummaryKaiShiAsyncStream_ReportsCompletion() {
		var db = PrepareKaiShiTables();
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		var completed = false;

		await foreach (var progress in new SummaryDb(db).SummaryKaiShiAsyncStream(new BillingParameter("202607", 99, "", ""))) {
			Assert.IsFalse(progress.IsError, progress.ErrorMessage);
			completed |= progress.IsCompleted;
		}

		Assert.IsTrue(completed);
	}

	// ---- 再作成締日検査 ----------------------------------------------------------

	[TestMethod]
	public void SummaryRebuildClosingCheck_SelectsOnlyRequestedKakeSide() {
		Assert.IsTrue(SummaryRebuildClosingCheck.IncludesUriKake("全て"));
		Assert.IsTrue(SummaryRebuildClosingCheck.IncludesKaiKake("全て"));
		Assert.IsFalse(SummaryRebuildClosingCheck.IncludesUriKake("在庫のみ"));
		Assert.IsFalse(SummaryRebuildClosingCheck.IncludesKaiKake("在庫のみ"));
		Assert.IsTrue(SummaryRebuildClosingCheck.IncludesUriKake("売掛のみ"));
		Assert.IsFalse(SummaryRebuildClosingCheck.IncludesKaiKake("売掛のみ"));
		Assert.IsFalse(SummaryRebuildClosingCheck.IncludesUriKake("買掛のみ"));
		Assert.IsTrue(SummaryRebuildClosingCheck.IncludesKaiKake("買掛のみ"));
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_ClampsDayAndUsesMonthEnd() {
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260228", 1, out var firstDay));
		Assert.AreEqual("20260201", firstDay);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260201", 31, out var february31));
		Assert.AreEqual("20260228", february31);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260401", 31, out var april31));
		Assert.AreEqual("20260430", april31);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260201", 99, out var februaryEnd));
		Assert.AreEqual("20260228", februaryEnd);
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_TreatsNullEmptyAndInvalidDayToAsMismatch() {
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay(null, 31, out _));
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay(string.Empty, 31, out _));
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20261331", 31, out _));
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_AllowsZeroSavedRows() {
		var mismatches = SummaryRebuildClosingCheck.FindMismatches("売掛", []);

		Assert.AreEqual(0, mismatches.Count);
		Assert.IsTrue(SummaryRebuildClosingCheck.CanStartRequestDispatch(mismatches));
		Assert.AreEqual(string.Empty, SummaryRebuildClosingCheck.BuildMismatchWarning(mismatches));
		StringAssert.Contains(SummaryRebuildClosingCheck.NoSavedSummaryRowNotice, "保存済み集計行がない場合");
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_BlocksMismatchAndShowsFiveRowsWithRemainder() {
		var rows = Enumerable.Range(1, 7)
			.Select(index => new SummaryClosingCheckRow {
				TorihikiCode = $"T{index:000}",
				DayTo = "20260227",
				Shime1 = 31,
			})
			.ToList();
		var mismatches = SummaryRebuildClosingCheck.FindMismatches("買掛", rows);
		var warning = SummaryRebuildClosingCheck.BuildMismatchWarning(mismatches);

		Assert.AreEqual(7, mismatches.Count);
		Assert.IsFalse(SummaryRebuildClosingCheck.CanStartRequestDispatch(mismatches));
		StringAssert.Contains(warning, "買掛: T001 / 保存締日 20260227 / 現在締日 31日");
		StringAssert.Contains(warning, "買掛: T005 / 保存締日 20260227 / 現在締日 31日");
		Assert.IsFalse(warning.Contains("T006", StringComparison.Ordinal));
		StringAssert.Contains(warning, "ほか2件");
		StringAssert.Contains(warning, SummaryRebuildClosingCheck.ManualRecalculationGuidance);
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_SelectsByDenDayAndCatchesEmptyOrInvalidDayTo() {
		var db = PrepareClosingCheckTables();
		var uriEmptyId = InsertTokui(db, "U001", 31);
		var uriInvalidId = InsertTokui(db, "U002", 31);
		var uriOutsideId = InsertTokui(db, "U003", 31);
		var kaiEmptyId = InsertShiire(db, "K001", 31);
		var kaiInvalidId = InsertShiire(db, "K002", 31);
		var kaiOutsideId = InsertShiire(db, "K003", 31);
		db.Insert(new SummaryUriSei { Id_Tokui = uriEmptyId, DenDay = "20260731", DayTo = string.Empty });
		db.Insert(new SummaryUriSei { Id_Tokui = uriInvalidId, DenDay = "20260731", DayTo = "20261331" });
		db.Insert(new SummaryUriSei { Id_Tokui = uriOutsideId, DenDay = "20260831", DayTo = string.Empty });
		db.Insert(new SummaryKaiShi { Id_Shiire = kaiEmptyId, DenDay = "20260731", DayTo = string.Empty });
		db.Insert(new SummaryKaiShi { Id_Shiire = kaiInvalidId, DenDay = "20260731", DayTo = "20261331" });
		db.Insert(new SummaryKaiShi { Id_Shiire = kaiOutsideId, DenDay = "20260831", DayTo = string.Empty });

		var uriRows = db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.UriClosingCheckSql, "202607", "202607");
		var kaiRows = db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.KaiClosingCheckSql, "202607", "202607");

		CollectionAssert.AreEqual(new[] { "U001", "U002" }, uriRows.Select(row => row.TorihikiCode).ToArray());
		CollectionAssert.AreEqual(new[] { "K001", "K002" }, kaiRows.Select(row => row.TorihikiCode).ToArray());
		Assert.AreEqual(2, SummaryRebuildClosingCheck.FindMismatches("売掛", uriRows).Count);
		Assert.AreEqual(2, SummaryRebuildClosingCheck.FindMismatches("買掛", kaiRows).Count);
		AssertDayToNullIsRejected(db, nameof(SummaryUriSei));
		AssertDayToNullIsRejected(db, nameof(SummaryKaiShi));
	}

	[TestMethod]
	public void SummaryRebuildRequestPlanner_CreatesExpandedDescriptorsAndConfirmation() {
		var all = SummaryRebuildRequestPlanner.CreateDescriptors("全て", ["202607", "202608"], [20, 99], [31], "202607", "202608");
		var stock = SummaryRebuildRequestPlanner.CreateDescriptors("在庫のみ", ["202607", "202608"], [], [], "202607", "202608");
		var uri = SummaryRebuildRequestPlanner.CreateDescriptors("売掛のみ", ["202607", "202608"], [20, 99], [], "202607", "202608");
		var kai = SummaryRebuildRequestPlanner.CreateDescriptors("買掛のみ", ["202607", "202608"], [], [31], "202607", "202608");

		CollectionAssert.AreEqual(new[] {
			new SummaryRebuildRequestDescriptor(CvFlag.Msg050_Summary, "202607", "202608"),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg052_SummaryUriKake, "202607", "202608"),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg053_SummaryKaiKake, "202607", "202608"),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202607", 20),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202607", 99),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202608", 20),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202608", 99),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg057_SummaryKaiShi, "202607", "202608", "202607", 31),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg057_SummaryKaiShi, "202607", "202608", "202608", 31),
		}, all.ToArray());
		CollectionAssert.AreEqual(new[] {
			new SummaryRebuildRequestDescriptor(CvFlag.Msg050_Summary, "202607", "202608"),
		}, stock.ToArray());
		CollectionAssert.AreEqual(new[] {
			new SummaryRebuildRequestDescriptor(CvFlag.Msg052_SummaryUriKake, "202607", "202608"),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202607", 20),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202607", 99),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202608", 20),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg056_SummaryUriSei, "202607", "202608", "202608", 99),
		}, uri.ToArray());
		CollectionAssert.AreEqual(new[] {
			new SummaryRebuildRequestDescriptor(CvFlag.Msg053_SummaryKaiKake, "202607", "202608"),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg057_SummaryKaiShi, "202607", "202608", "202607", 31),
			new SummaryRebuildRequestDescriptor(CvFlag.Msg057_SummaryKaiShi, "202607", "202608", "202608", 31),
		}, kai.ToArray());
		Assert.AreEqual("請求残・支払残も再作成します。", SummaryRebuildRequestPlanner.GetClosingSummaryConfirmation("全て"));
		Assert.AreEqual(string.Empty, SummaryRebuildRequestPlanner.GetClosingSummaryConfirmation("在庫のみ"));
		Assert.AreEqual("請求残も再作成します。", SummaryRebuildRequestPlanner.GetClosingSummaryConfirmation("売掛のみ"));
		Assert.AreEqual("支払残も再作成します。", SummaryRebuildRequestPlanner.GetClosingSummaryConfirmation("買掛のみ"));
	}

	[TestMethod]
	public void SummaryRebuildRequestPlanner_KeepsKakeRequestsWhenClosingDaysAreEmpty() {
		var all = SummaryRebuildRequestPlanner.CreateDescriptors("全て", ["202607", "202608"], [], [], "202607", "202608");
		var stock = SummaryRebuildRequestPlanner.CreateDescriptors("在庫のみ", ["202607", "202608"], [], [], "202607", "202608");
		var uri = SummaryRebuildRequestPlanner.CreateDescriptors("売掛のみ", ["202607", "202608"], [], [], "202607", "202608");
		var kai = SummaryRebuildRequestPlanner.CreateDescriptors("買掛のみ", ["202607", "202608"], [], [], "202607", "202608");

		CollectionAssert.AreEqual(new[] {
			CvFlag.Msg050_Summary,
			CvFlag.Msg052_SummaryUriKake, CvFlag.Msg053_SummaryKaiKake,
		}, all.Select(x => x.Flag).ToArray());
		CollectionAssert.AreEqual(new[] { CvFlag.Msg050_Summary }, stock.Select(x => x.Flag).ToArray());
		CollectionAssert.AreEqual(new[] { CvFlag.Msg052_SummaryUriKake }, uri.Select(x => x.Flag).ToArray());
		CollectionAssert.AreEqual(new[] { CvFlag.Msg053_SummaryKaiKake }, kai.Select(x => x.Flag).ToArray());
		Assert.IsFalse(all.Any(x => x.Flag is CvFlag.Msg056_SummaryUriSei or CvFlag.Msg057_SummaryKaiShi));
	}

	[TestMethod]
	public async Task SummaryRebuildRequestDispatchGate_UsesDescriptorOrderAndStopsBeforeSend() {
		var mismatch = new SummaryClosingMismatch("売掛", "U001", "20260730", 31);
		var descriptors = SummaryRebuildRequestPlanner.CreateDescriptors("全て", ["202607"], [99], [20], "202607", "202607");
		List<CvFlag> createdFlags = [];
		List<CvFlag> sentFlags = [];
		var completed = await SummaryRebuildRequestDispatchGate.ExecuteAsync<SummaryRebuildRequestDescriptor, CvFlag>(
			_ => Task.FromResult<IReadOnlyList<SummaryClosingMismatch>>([]),
			_ => Task.FromResult<IReadOnlyList<SummaryRebuildRequestDescriptor>>(descriptors),
			descriptor => {
				createdFlags.Add(descriptor.Flag);
				return descriptor.Flag;
			},
			async (descriptor, request, requestIndex, requestCount, _) => {
				Assert.AreEqual(descriptor.Flag, request);
				Assert.AreEqual(descriptors.Count, requestCount);
				Assert.AreEqual(requestIndex, sentFlags.Count);
				sentFlags.Add(request);
				await Task.CompletedTask;
			},
			CancellationToken.None);
		CollectionAssert.AreEqual(descriptors.Select(x => x.Flag).ToArray(), createdFlags.ToArray());
		CollectionAssert.AreEqual(descriptors.Select(x => x.Flag).ToArray(), sentFlags.ToArray());
		CollectionAssert.AreEqual(descriptors.ToArray(), completed.Descriptors.ToArray());

		var createDescriptorCount = 0;
		var createRequestCount = 0;
		var sendCount = 0;
		var blocked = await SummaryRebuildRequestDispatchGate.ExecuteAsync<SummaryRebuildRequestDescriptor, CvFlag>(
			_ => Task.FromResult<IReadOnlyList<SummaryClosingMismatch>>([mismatch]),
			_ => {
				createDescriptorCount++;
				return Task.FromResult<IReadOnlyList<SummaryRebuildRequestDescriptor>>(descriptors);
			},
			_ => {
				createRequestCount++;
				return CvFlag.Msg051_SummaryRealStock;
			},
			(_, _, _, _, _) => {
				sendCount++;
				return Task.CompletedTask;
			},
			CancellationToken.None);

		Assert.IsFalse(blocked.CanStartRequestDispatch);
		Assert.AreEqual(0, createDescriptorCount);
		Assert.AreEqual(0, createRequestCount);
		Assert.AreEqual(0, sendCount);
		Assert.AreEqual(0, blocked.Descriptors.Count);

		try {
			await SummaryRebuildRequestDispatchGate.ExecuteAsync<SummaryRebuildRequestDescriptor, CvFlag>(
				_ => Task.FromException<IReadOnlyList<SummaryClosingMismatch>>(new InvalidOperationException("照会失敗")),
				_ => {
					createDescriptorCount++;
					return Task.FromResult<IReadOnlyList<SummaryRebuildRequestDescriptor>>(descriptors);
				},
				_ => {
					createRequestCount++;
					return CvFlag.Msg051_SummaryRealStock;
				},
				(_, _, _, _, _) => {
					sendCount++;
					return Task.CompletedTask;
				},
				CancellationToken.None);
			Assert.Fail("照会例外が発生する必要があります。");
		}
		catch (InvalidOperationException) {
		}
		Assert.AreEqual(0, createDescriptorCount);
		Assert.AreEqual(0, createRequestCount);
		Assert.AreEqual(0, sendCount);

		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();
		try {
			await SummaryRebuildRequestDispatchGate.ExecuteAsync<SummaryRebuildRequestDescriptor, CvFlag>(
				_ => Task.FromCanceled<IReadOnlyList<SummaryClosingMismatch>>(cancellationSource.Token),
				_ => {
					createDescriptorCount++;
					return Task.FromResult<IReadOnlyList<SummaryRebuildRequestDescriptor>>(descriptors);
				},
				_ => {
					createRequestCount++;
					return CvFlag.Msg051_SummaryRealStock;
				},
				(_, _, _, _, _) => {
					sendCount++;
					return Task.CompletedTask;
				},
				CancellationToken.None);
			Assert.Fail("取消例外が発生する必要があります。");
		}
		catch (OperationCanceledException) {
		}
		Assert.AreEqual(0, createDescriptorCount);
		Assert.AreEqual(0, createRequestCount);
		Assert.AreEqual(0, sendCount);
	}

	// ---- 準備 --------------------------------------------------------------------

	/// <summary>KIN 区分マスタの Id。実DBの値ではなくテスト内で採番した値を使う</summary>
	private const long KinCash = 101;
	private const long KinFee = 102;
	private const long KinDensai = 103;
	private const long KinOffset = 104;
	private const long KinOther = 105;
	private const long KinUnknown = 106;

	private ExDatabaseSqlite PrepareUriKakeTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		AddOwnClosingDay(db);
		db.CreateTable(typeof(SummaryUriKake), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		// 請求単位ぶんの端数処理(TaxRounding)を得意先マスタからJOINするため必要(3.5)。行が無ければ既定の四捨五入になる。
		db.CreateTable(typeof(MasterTokui), true, false);
		InsertKinMaster(db);
		return db;
	}

	private ExDatabaseSqlite PrepareClosingCheckTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriSei), true, false);
		db.CreateTable(typeof(MasterTokui), true, false);
		db.CreateTable(typeof(SummaryKaiShi), true, false);
		db.CreateTable(typeof(MasterShiire), true, false);
		return db;
	}

	private static long InsertTokui(ExDatabaseSqlite db, string code, int shime) {
		db.Insert(new MasterTokui { Code = code, Shime1 = shime });
		return db.Single<MasterTokui>("where Code=@0", code).Id;
	}

	private static long InsertShiire(ExDatabaseSqlite db, string code, int shime) {
		db.Insert(new MasterShiire { Code = code, Shime1 = shime });
		return db.Single<MasterShiire>("where Code=@0", code).Id;
	}

	/// <summary>
	/// 期首年月日(yyyyMMdd)を持つ <see cref="MasterSysman"/> を1件作る。
	/// 期首以前の集計行を再計算が凍結することを検証するために使う。
	/// </summary>
	private static void AddFiscalStartDate(ExDatabaseSqlite db, string fiscalStartYmd) {
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { FiscalStartDate = fiscalStartYmd, ShimeBi = 99 });
	}

	private static void AssertDayToNullIsRejected(ExDatabaseSqlite db, string tableName) {
		var rejected = false;
		try {
			db.Execute($"UPDATE {tableName} SET DayTo = NULL");
		}
		catch (SqliteException) {
			rejected = true;
		}
		Assert.IsTrue(rejected, $"{tableName}.DayTo は物理スキーマでNULLを許可してはいけません。");
	}

	private ExDatabaseSqlite PrepareUriSeiTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriSei), true, false);
		db.CreateTable(typeof(MasterTokui), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		InsertKinMaster(db);
		return db;
	}

	private ExDatabaseSqlite PrepareKaiKakeTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		AddOwnClosingDay(db);
		db.CreateTable(typeof(SummaryKaiKake), true, false);
		db.CreateTable(typeof(Tran03Shiire), true, false);
		db.CreateTable(typeof(Tran02Material), true, false);
		db.CreateTable(typeof(Tran07Shiharai), true, false);
		// 請求単位ぶんの端数処理(TaxRounding)を仕入先マスタからJOINするため必要(3.5)。行が無ければ既定の四捨五入になる。
		db.CreateTable(typeof(MasterShiire), true, false);
		InsertKinMaster(db);
		return db;
	}

	private static void AddOwnClosingDay(ExDatabaseSqlite db) {
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { ShimeBi = 99 });
	}

	/// <summary>
	/// 入金・支払の区分別内訳は KIN 区分マスタの Code で振り分ける。
	/// <para>
	/// Id は明細の <c>Id_Kin</c> と突き合わせるので値を固定したいが、<see cref="MasterMeisho.Id"/> は
	/// AutoIncrement の主キーで NPoco の <c>Insert</c> は明示した値を捨てる。ここだけ生SQLで採番する。
	/// </para>
	/// </summary>
	private static void InsertKinMaster(ExDatabaseSqlite db) {
		db.CreateTable(typeof(MasterMeisho), true, false);
		InsertMeisho(db, KinCash, "KIN", "01", "現金入金");
		InsertMeisho(db, KinFee, "KIN", "02", "振込手数料");
		InsertMeisho(db, KinDensai, "KIN", "03", "手形入金");
		InsertMeisho(db, KinOffset, "KIN", "04", "相殺入金");
		InsertMeisho(db, KinOther, "KIN", "05", "その他入金");
		InsertMeisho(db, KinUnknown, "KIN", "06", "未知入金");
		// 同じ Code を持つ別区分に引っ張られないことを担保する
		InsertMeisho(db, 201, "ITM", "01", "別区分の01");
	}

	private static void InsertMeisho(ExDatabaseSqlite db, long id, string kubun, string code, string name) =>
		db.Execute(
			$"INSERT INTO {nameof(MasterMeisho)} (Id, Kubun, Code, Name) VALUES (@0, @1, @2, @3)",
			id, kubun, code, name);

	private static Tran00Uriage CreateUriage(string kakeDay, long idTokui, EnumUri00 kubun, int total, int tax) {
		var tran = new Tran00Uriage {
			DenDay = kakeDay,
			KakeDay = kakeDay,
			Id_Tokui = idTokui,
			// 集計対象(Uriage/Henpin/Nebiki等)はKingakuTotal(税抜)。Totalは実伝票と同じ関係
			// |KingakuTotal|+Tax1+Tax2+Tax3(税込)にしておく(仕様3.8。Totalで集計するとTax1が二重計上になる)。
			KingakuTotal = total,
			Total = Math.Abs(total) + tax,
			Tax1 = tax,
			// このヘルパーは丸め済みのTax1を直接指定するテスト用途なので、伝票単位(そのまま合算)を明示する。
			// 既定の請求単位(TaxCalcUnit=0)のままだとTaxableAmountが未設定のため税額が0扱いになってしまう(3.3/3.5)。
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			IsPay = 1,
		};
		tran.EnKubun = kubun;
		return tran;
	}

	private static Tran00Uriage CreateUriageWithKubun(string kakeDay, long idTokui, int kubun, int total, int tax) {
		var tran = CreateUriage(kakeDay, idTokui, EnumUri00.Uriage, total, tax);
		tran.Kubun = kubun;
		return tran;
	}

	private ExDatabaseSqlite PrepareKaiShiTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryKaiShi), true, false);
		db.CreateTable(typeof(MasterShiire), true, false);
		db.CreateTable(typeof(Tran03Shiire), true, false);
		db.CreateTable(typeof(Tran02Material), true, false);
		db.CreateTable(typeof(Tran07Shiharai), true, false);
		InsertKinMaster(db);
		return db;
	}

	// CreateUriage/CreateShiireが既にKingakuTotalを集計対象とする実伝票と同じ関係で作っているため、
	// 請求残(SummaryUriSei/SummaryKaiShi)向けの別途のTotal上書きは不要になった(仕様3.8)。
	private static Tran00Uriage CreateBillingUriage(string kakeDay, long idTokui, EnumUri00 kubun, int total, int tax) =>
		CreateUriage(kakeDay, idTokui, kubun, total, tax);

	private static Tran03Shiire CreateBillingShiire(string kakeDay, long idShiire, EnumShiire kubun, int total, int tax) =>
		CreateShiire(kakeDay, idShiire, kubun, total, tax);

	private static Tran03Shiire CreateShiire(string kakeDay, long idShiire, EnumShiire kubun, int total, int tax) {
		var tran = new Tran03Shiire {
			DenDay = kakeDay,
			KakeDay = kakeDay,
			Id_Shiire = idShiire,
			// 集計対象(Shiire/Henpin/Nebiki等)はKingakuTotal(税抜)。Totalは実伝票と同じ関係
			// |KingakuTotal|+Tax1+Tax2+Tax3(税込)にしておく(仕様3.8。Totalで集計するとTax1が二重計上になる)。
			KingakuTotal = total,
			Total = Math.Abs(total) + tax,
			Tax1 = tax,
			// このヘルパーは丸め済みのTax1を直接指定するテスト用途なので、伝票単位(そのまま合算)を明示する。
			// 既定の請求単位(TaxCalcUnit=0)のままだとTaxableAmountが未設定のため税額が0扱いになってしまう(3.3/3.5)。
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			IsPay = 1,
		};
		tran.EnKubun = kubun;
		return tran;
	}

	private static Tran03Shiire CreateShiireWithKubun(string kakeDay, long idShiire, int kubun, int total, int tax) {
		var tran = CreateShiire(kakeDay, idShiire, EnumShiire.Shiire, total, tax);
		tran.Kubun = kubun;
		return tran;
	}

	private static Tran06Nyukin CreateNyukin(string kakeDay, long idTorisaki, (long IdKin, int Kingaku)[] meisai) =>
		new() {
			KakeDay = kakeDay,
			Id_Torisaki = idTorisaki,
			KingakuTotal = meisai.Sum(x => x.Kingaku) + 10000,
			Jmeisai = BuildKinMeisai(meisai),
		};

	private static Tran07Shiharai CreateShiharai(string kakeDay, long idTorisaki, (long IdKin, int Kingaku)[] meisai) =>
		new() {
			KakeDay = kakeDay,
			Id_Torisaki = idTorisaki,
			KingakuTotal = meisai.Sum(x => x.Kingaku) + 10000,
			Jmeisai = BuildKinMeisai(meisai),
		};

	private static List<TranKinMeisai> BuildKinMeisai((long IdKin, int Kingaku)[] meisai) =>
		[.. meisai.Select((x, i) => new TranKinMeisai { No = i + 1, Id_Kin = x.IdKin, Kingaku = x.Kingaku })];

	private static string[] GetUriKakeSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryUriKake>("order by Id_Tokui, DenMonth")
			.Select(x => $"{x.Id_Tokui}:{x.DenMonth}:{x.Balance}:{x.TotalIn}:{x.TotalSales}:{x.Uriage}:{x.Henpin}:{x.Nebiki}:{x.Tax1}:{x.Cash}:{x.Fee}:{x.Densai}:{x.Offset}:{x.Other}")];

	private static string[] GetKaiKakeSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryKaiKake>("order by Id_Shiire, DenMonth")
			.Select(x => $"{x.Id_Shiire}:{x.DenMonth}:{x.Balance}:{x.TotalOut}:{x.TotalShiire}:{x.Shiire}:{x.Henpin}:{x.Nebiki}:{x.Tax1}:{x.Cash}:{x.Fee}:{x.Densai}:{x.Offset}:{x.Other}")];

	private static string[] GetKaiShiSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryKaiShi>("order by Id_Shiire, DenDay")
			.Select(x => $"{x.Id_Shiire}:{x.DenDay}:{x.Balance}:{x.TotalOut}:{x.TotalShiire}:{x.Shiire}:{x.Henpin}:{x.Nebiki}:{x.Tax1}:{x.Cash}:{x.Fee}:{x.Densai}:{x.Offset}:{x.Other}:{x.ShiharaiYoteiDay}")];
}
