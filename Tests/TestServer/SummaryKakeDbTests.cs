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
/// `Doc/spec/archive/2026-08-18_請求計算・支払計算_詳細設計.md` 2.1 の確定ルールを固定する。
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
	public void CalcSummaryUriKake_SeparatesSonotaAndUsesPositiveBalanceForUnrecovered() {
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
		Assert.AreEqual(1000 + 500 + 400 + 19 + 89, row.Uriage, "区分99(その他売上)はSonotaへ分離し、売上へは畳み込まない");
		Assert.AreEqual(200 + 100 + 29, row.Henpin);
		Assert.AreEqual(300 + 39, row.Nebiki);
		Assert.AreEqual(700, row.Sonota, "区分99は独立してSonotaへ分離集計する");
		Assert.AreEqual(100 + 50 - 20 - 10 + 30 + 70 + 40 + 1 - 2 + 3 + 4 + 5, row.Tax1);
		Assert.AreEqual(row.Uriage - row.Henpin - row.Nebiki + row.Sonota + row.Tax1, row.TotalSales);
		Assert.AreEqual(row.TotalSales, row.Balance, "受取が無いので当月分の純増減がそのままBalance(正=未回収)になる");
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
		Assert.AreEqual(-1200, august.Balance, "売上が無く入金のみなので負(過入金)になる");
	}

	[TestMethod]
	public void CalcSummaryUriKake_DoesNotCarryBalanceAcrossMonths() {
		// 繰越はテーブルに持たない(2.1)。各月のBalanceはその月だけの純増減であり、
		// 前月の残高を積み上げない(旧仕様のウィンドウ関数による繰越を廃止)。
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateNyukin("20260805", 1, [(KinCash, 400)]));

		summaryDb.CalcSummaryUriKake("202607", "202608");
		var rows = db.Fetch<SummaryUriKake>("where Id_Tokui=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(1000, rows[0].Balance, "7月は売上1000のみ(正=未回収)");
		Assert.AreEqual(-400, rows[1].Balance, "8月は入金400のみ。7月の残1000は積み上がらない(繰越なら-600になるはず)");
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
	public void CalcSummaryUriKake_RecalculatesOnlyTargetMonthLeavingLaterMonthsUntouched() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateUriage("20260810", 1, EnumUri00.Uriage, 500, 0));
		summaryDb.CalcSummaryUriKake("202607", "202608");
		var augustBefore = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202608");
		Assert.AreEqual(500, augustBefore.Balance);

		// 7月の伝票を増やして7月だけを指定して再計算する。繰越を持たないため8月の行は一切変化しない
		db.Insert(CreateUriage("20260720", 1, EnumUri00.Uriage, 300, 0));
		summaryDb.CalcSummaryUriKake("202607", "202607");
		var rows = db.Fetch<SummaryUriKake>("where Id_Tokui=@0 order by DenMonth", 1);
		var augustAfter = rows.Single(x => x.DenMonth == "202608");

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(1300, rows[0].Balance, "7月分の伝票が増えた分だけ増える");
		Assert.AreEqual(500, augustAfter.Balance, "8月の行は繰越を持たないため変化しない");
		Assert.AreEqual(augustBefore.Vdc, augustAfter.Vdc, "8月の行はDELETE→INSERTされていない(行そのものが未変更)");
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
	public void CalcSummaryUriKake_FreezesPreFiscalOpeningBalanceRow() {
		// 繰越はテーブルに持たない(4.2)。期首行は「期首直前の1期間の実績行」として保持されるだけで、
		// 再計算で上書きされたり、当月の値が積み上げられたりしないことを検証する(積み上げの検証は削除)。
		var db = PrepareUriKakeTables();
		AddFiscalStartDate(db, "20260701"); // 期首 = 2026年7月
		var summaryDb = new SummaryDb(db);

		// 期首前(202606)に期首売掛残をCSV取込相当で投入した状態
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000, TotalSales = 5000 });
		// 期首前の伝票は集計対象外
		db.Insert(CreateUriage("20260620", 1, EnumUri00.Uriage, 9999, 0));
		// 当月(202607)の伝票
		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));

		// 期首をまたぐ範囲を指定しても開始は期首月へ切り上がる
		summaryDb.CalcSummaryUriKake("202605", "202607");

		var opening = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.AreEqual(5000, opening.Balance, "期首前の残は再計算で上書きしてはいけない");
		Assert.IsNull(db.FirstOrDefault<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202605"), "期首前の月に行を作ってはいけない");

		var july = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");
		Assert.AreEqual(1000, july.Uriage, "期首前(202606)の伝票は集計されない");
		Assert.AreEqual(1000, july.Balance, "当月分のみの純増減。期首残(5000)は積み上げない");
	}

	[TestMethod]
	public void CalcSummaryUriKake_SkipsRangeEntirelyBeforeFiscalStart() {
		var db = PrepareUriKakeTables();
		AddFiscalStartDate(db, "20260701");
		var summaryDb = new SummaryDb(db);
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 5000 });
		db.Insert(CreateUriage("20260620", 1, EnumUri00.Uriage, 1000, 0));

		var count = summaryDb.CalcSummaryUriKake("202605", "202606");

		Assert.AreEqual(0, count, "期首前だけの範囲は再計算しない");
		var opening = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202606");
		Assert.AreEqual(5000, opening.Balance, "期首前の残は変更されない");
	}

	[TestMethod]
	public void CalcSummaryUriKake_PreviousBalanceSumsPastPeriods() {
		// PreviousBalanceの標準SQL(7.3): SUM(TotalSales - TotalIn) WHERE DenMonth < 対象年月
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new SummaryUriKake { Id_Tokui = 1, DenMonth = "202606", Balance = 70000, TotalSales = 70000 });
		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateNyukin("20260715", 1, [(KinCash, 300)]));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var july = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");
		Assert.AreEqual(700, july.Balance, "1000 - 300");

		var previousBalance = db.FirstOrDefault<long>(
			"SELECT SUM(TotalSales - TotalIn) FROM SummaryUriKake WHERE Id_Tokui=@0 AND DenMonth < @1", 1, "202607");
		Assert.AreEqual(70000L, previousBalance, "期首行だけがPreviousBalanceの累計に含まれる");
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
		Assert.AreEqual(450, row.Balance, "正=未回収");
		Assert.AreEqual("1-20260731-01", row.SeikyuNo);
		Assert.AreEqual(1, row.Renban);
		Assert.AreEqual("20260731", row.NyukinYoteiDay);
		Assert.IsNull(db.FirstOrDefault<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 2, "20260720"));
	}

	[TestMethod]
	public void CalcSummaryUriSei_PreviousBalanceSumsPastPeriodsIncludingOpeningRow() {
		// PreviousBalance(表示専用、DB非実体)は SUM(TotalSales - TotalIn) WHERE DayTo < 対象期間開始日 で
		// 読み出し時に算出する(2.3)。Balance列の再計算漏れに左右されないよう、Balance列そのものではなく
		// 内訳合計から積む式を正とする。期首残高CSVで投入した行もこのSUMへ自然に含まれることを確認する。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		// 期首残高CSV取込相当(6月末より前の1期間ぶんの実績行)
		db.Insert(new SummaryUriSei {
			Id_Tokui = 1, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630",
			TotalSales = 150000, TotalIn = 0, Balance = 150000,
		});
		db.Insert(CreateBillingUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateNyukin("20260715", 1, [(KinCash, 300)]));
		db.Insert(CreateBillingUriage("20260810", 1, EnumUri00.Uriage, 2000, 200));
		db.Insert(CreateNyukin("20260815", 1, [(KinCash, 500)]));

		summaryDb.CalcSummaryUriSei("202607", 99, "A001", "A999");
		summaryDb.CalcSummaryUriSei("202608", 99, "A001", "A999");

		var july = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260731");
		var august = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 1, "20260831");
		Assert.AreEqual(800, july.Balance, "7月: Uriage1000+Tax100-TotalIn300");

		// PreviousBalanceの標準SQL(7.3)を実際に実行して検証する
		var previousForAugust = db.FirstOrDefault<long>(
			"SELECT SUM(TotalSales - TotalIn) FROM SummaryUriSei WHERE Id_Tokui=@0 AND DayTo < @1", 1, august.DayFrom);
		Assert.AreEqual(150000 + july.Balance, previousForAugust, "期首行 + 7月分の当月増減の合計が8月の前残になる");

		var previousForJuly = db.FirstOrDefault<long>(
			"SELECT SUM(TotalSales - TotalIn) FROM SummaryUriSei WHERE Id_Tokui=@0 AND DayTo < @1", 1, july.DayFrom);
		Assert.AreEqual(150000, previousForJuly, "7月の前残は期首行だけになる");

		// 当月末残高が必要な帳票は PreviousBalance + Balance で求める(2.3)
		Assert.AreEqual(150000 + july.Balance + august.Balance, previousForAugust + august.Balance);
	}

	[TestMethod]
	public void CalcSummaryUriSei_SeparatesKubun99AsSonotaWithoutFoldingIntoUriage() {
		// E11 / 4.3: 区分99(その他売上)は請求残(SummaryUriSei)でもSonotaへ分離集計し、TotalSalesへ加算する。
		// 掛集計(SummaryUriKake)側も同様にSonotaへ分離する
		// (CalcSummaryUriKake_SeparatesSonotaAndUsesPositiveBalanceForUnrecovered 参照)。
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
	public void CalcSummaryKaiKake_SeparatesSonotaAndUsesPositiveBalanceForUnpaid() {
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
		Assert.AreEqual(1000 + 400 + 19 + 89, row.Shiire, "区分99(その他仕入)はSonotaへ分離し、仕入へは畳み込まない");
		Assert.AreEqual(200 + 29, row.Henpin);
		Assert.AreEqual(100 + 39, row.Nebiki);
		Assert.AreEqual(400, row.Sonota, "区分99は独立してSonotaへ分離集計する");
		Assert.AreEqual(100 - 20 + 10 + 40 + 40 + 1 - 2 + 3 + 4 + 5, row.Tax1);
		Assert.AreEqual(row.Shiire - row.Henpin - row.Nebiki + row.Sonota + row.Tax1, row.TotalShiire);
		Assert.AreEqual(600, row.Cash);
		Assert.AreEqual(50, row.Offset);
		Assert.AreEqual(row.TotalOut, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
		Assert.AreEqual(row.TotalShiire - row.TotalOut, row.Balance, "正=未払");
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
	public void CalcSummaryKaiKake_DoesNotCarryBalanceAcrossMonths() {
		// 繰越はテーブルに持たない(2.1)。各月のBalanceはその月だけの純増減であり、
		// 前月の残高を積み上げない(旧仕様のウィンドウ関数による繰越を廃止)。
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiharai("20260805", 1, [(KinCash, 400)]));

		summaryDb.CalcSummaryKaiKake("202607", "202608");
		var rows = db.Fetch<SummaryKaiKake>("where Id_Shiire=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(1000, rows[0].Balance, "7月は仕入1000のみ(正=未払)");
		Assert.AreEqual(-400, rows[1].Balance, "8月は支払400のみ。7月の残1000は積み上がらない(繰越なら-600になるはず)");
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
	public void CalcSummaryKaiKake_RecalculatesOnlyTargetMonthLeavingLaterMonthsUntouched() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiire("20260810", 1, EnumShiire.Shiire, 500, 0));
		summaryDb.CalcSummaryKaiKake("202607", "202608");
		var augustBefore = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202608");
		Assert.AreEqual(500, augustBefore.Balance);

		// 7月の伝票を増やして7月だけを指定して再計算する。繰越を持たないため8月の行は一切変化しない
		db.Insert(CreateShiire("20260720", 1, EnumShiire.Shiire, 300, 0));
		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var rows = db.Fetch<SummaryKaiKake>("where Id_Shiire=@0 order by DenMonth", 1);
		var augustAfter = rows.Single(x => x.DenMonth == "202608");

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(1300, rows[0].Balance, "7月分の伝票が増えた分だけ増える");
		Assert.AreEqual(500, augustAfter.Balance, "8月の行は繰越を持たないため変化しない");
		Assert.AreEqual(augustBefore.Vdc, augustAfter.Vdc, "8月の行はDELETE→INSERTされていない(行そのものが未変更)");
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

	[TestMethod]
	public void CalcSummaryKaiKake_AddsTran02MaterialKubun99FullyIntoTax1WithoutSonota() {
		// A-6回帰防止(4.3): Tran02Materialの区分99は丸めずTax1へ全額を積む特殊処理であり、
		// Tran03Shiireの区分99(Sonotaへ分離集計)とは別物として扱う(混同すると二重計上になる)。
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(CreateMaterial("20260710", 1, 99, 1000, 0));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(0, row.Shiire);
		Assert.AreEqual(0, row.Sonota, "Tran02Materialの区分99はSonotaではなくTax1へ積む");
		Assert.AreEqual(1000, row.Tax1, "丸めずそのままTax1へ全額加算する(A-6)");
		Assert.AreEqual(1000, row.TotalShiire);
	}

	[TestMethod]
	public void CalcSummaryKaiKake_PreviousBalanceSumsPastPeriods() {
		// PreviousBalanceの標準SQL(7.3): SUM(TotalShiire - TotalOut) WHERE DenMonth < 対象年月
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new SummaryKaiKake { Id_Shiire = 1, DenMonth = "202606", Balance = 70000, TotalShiire = 70000 });
		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiharai("20260715", 1, [(KinCash, 300)]));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var july = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");
		Assert.AreEqual(700, july.Balance, "1000 - 300");

		var previousBalance = db.FirstOrDefault<long>(
			"SELECT SUM(TotalShiire - TotalOut) FROM SummaryKaiKake WHERE Id_Shiire=@0 AND DenMonth < @1", 1, "202607");
		Assert.AreEqual(70000L, previousBalance, "期首行だけがPreviousBalanceの累計に含まれる");
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
		Assert.AreEqual(450, row.Balance, "正=未払");
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
	public void CalcSummaryKaiShi_SeparatesTran03ShiireKubun99AsSonota() {
		// 回帰防止(4.3): 従来はTran03Shiireの区分99(その他仕入)がCalcSummaryKaiShiではどこにも
		// 入らず欠落していた。買掛(CalcSummaryKaiKake)と揃え、Sonotaへ分離集計してTotalShiireへ加算する。
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(CreateBillingShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateBillingShiire("20260711", 1, EnumShiire.Other, 300, 0));

		summaryDb.CalcSummaryKaiShi("202607", 99, "A001", "A999");
		var row = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 1, "20260731");

		Assert.AreEqual(1000, row.Shiire);
		Assert.AreEqual(300, row.Sonota, "区分99はSonotaへ分離集計する(欠落の回帰防止)");
		Assert.AreEqual(1300, row.TotalShiire, "SonotaはTotalShiireへ加算される");
	}

	[TestMethod]
	public void CalcSummaryKaiShi_AddsTran02MaterialKubun99FullyIntoTax1WithoutSonota() {
		// A-6回帰防止(4.3): Tran02Materialの区分99は丸めずTax1へ全額を積む特殊処理であり、
		// Tran03Shiireの区分99(Sonotaへ分離集計)とは別物として扱う(混同すると二重計上になる)。
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(CreateMaterial("20260710", 1, 99, 1000, 0));

		summaryDb.CalcSummaryKaiShi("202607", 99, "A001", "A999");
		var row = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 1, "20260731");

		Assert.AreEqual(0, row.Shiire);
		Assert.AreEqual(0, row.Sonota, "Tran02Materialの区分99はSonotaではなくTax1へ積む");
		Assert.AreEqual(1000, row.Tax1, "丸めずそのままTax1へ全額加算する(A-6)");
		Assert.AreEqual(1000, row.TotalShiire);
	}

	[TestMethod]
	public void CalcSummaryKaiShi_PreviousBalanceSumsPastPeriods() {
		// PreviousBalanceの標準SQL(7.3): SUM(TotalShiire - TotalOut) WHERE DayTo < 対象期間開始日
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(new SummaryKaiShi {
			Id_Shiire = 1, DenDay = "20260630", DayFrom = "20260601", DayTo = "20260630",
			TotalShiire = 70000, TotalOut = 0, Balance = 70000,
		});
		db.Insert(CreateBillingShiire("20260710", 1, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateShiharai("20260715", 1, [(KinCash, 300)]));

		summaryDb.CalcSummaryKaiShi("202607", 99, "A001", "A999");
		var july = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", 1, "20260731");
		Assert.AreEqual(700, july.Balance, "1000 - 300");

		var previousBalance = db.FirstOrDefault<long>(
			"SELECT SUM(TotalShiire - TotalOut) FROM SummaryKaiShi WHERE Id_Shiire=@0 AND DayTo < @1", 1, july.DayFrom);
		Assert.AreEqual(70000L, previousBalance, "期首行だけがPreviousBalanceの累計に含まれる");
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

	// ---- 複数締日 ------------------------------------------------------------------
	// `Doc/spec/archive/2026-09-02_複数締日対応_詳細設計.md` 6.2 の受入条件を固定する。
	// 締日ごとの期間分割・「すべての締日」の冪等性・取引先ごとのDayFrom差・0フォールバック・
	// DELETEスコープ・PayDay=0フォールバックを対象にする。既存の単一締日テストの期待値は変更しない
	// (3.3の一致保証・受入条件8)。

	[TestMethod]
	public void CalcSummaryUriSei_MultiShime_SplitsPeriodsPerClosingDay() {
		// 3.3の境界例([10,20,99]・請求月202609)どおり、締日10/20/99を3回実行すると3行できて、
		// 各行のDayFrom/DayTo/DenDayと金額が期間どおりに割れること(6.2-1)。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "A001").Id;
		db.Insert(CreateBillingUriage("20260905", idTokui, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateBillingUriage("20260915", idTokui, EnumUri00.Uriage, 2000, 0));
		db.Insert(CreateBillingUriage("20260925", idTokui, EnumUri00.Uriage, 3000, 0));

		summaryDb.CalcSummaryUriSei("202609", 10);
		summaryDb.CalcSummaryUriSei("202609", 20);
		summaryDb.CalcSummaryUriSei("202609", 99);

		var rows = db.Fetch<SummaryUriSei>("where Id_Tokui=@0 order by DenDay", idTokui);
		Assert.AreEqual(3, rows.Count, "締日10/20/99の3回で3行できる");

		Assert.AreEqual("20260901", rows[0].DayFrom);
		Assert.AreEqual("20260910", rows[0].DayTo);
		Assert.AreEqual("20260910", rows[0].DenDay);
		Assert.AreEqual(1000, rows[0].Uriage, "9/5の売上は締日10の期間(9/1-9/10)");

		Assert.AreEqual("20260911", rows[1].DayFrom);
		Assert.AreEqual("20260920", rows[1].DayTo);
		Assert.AreEqual("20260920", rows[1].DenDay);
		Assert.AreEqual(2000, rows[1].Uriage, "9/15の売上は締日20の期間(9/11-9/20)");

		Assert.AreEqual("20260921", rows[2].DayFrom);
		Assert.AreEqual("20260930", rows[2].DayTo);
		Assert.AreEqual("20260930", rows[2].DenDay);
		Assert.AreEqual(3000, rows[2].Uriage, "9/25の売上は締日99(末日)の期間(9/21-9/30)");
	}

	[TestMethod]
	public async Task SummaryUriSeiAsyncStream_AllShime_MatchesIndividualShimeRuns() {
		// 締日[10,20,99]の得意先1件について、「すべての締日」(Shime=0)1回の実行が、
		// 締日3回の個別実行と同じ結果になること(冪等性、6.2-2)。
		var individual = BuildMultiShimeUriSeiFixture(out var individualDb);
		individual.CalcSummaryUriSei("202609", 10);
		individual.CalcSummaryUriSei("202609", 20);
		individual.CalcSummaryUriSei("202609", 99);
		var individualSnapshot = GetUriSeiComparableSnapshot(individualDb);

		var all = BuildMultiShimeUriSeiFixture(out var allDb);
		await foreach (var progress in all.SummaryUriSeiAsyncStream(new BillingParameter("202609", 0, "", ""))) {
			Assert.IsFalse(progress.IsError, progress.ErrorMessage);
		}
		var allSnapshot = GetUriSeiComparableSnapshot(allDb);

		Assert.AreEqual(3, allSnapshot.Length, "すべての締日の展開で3行できる");
		CollectionAssert.AreEqual(individualSnapshot, allSnapshot);
	}

	[TestMethod]
	public void CalcSummaryUriSei_MixedClosingPatterns_DayFromDiffersPerOwner() {
		// 2.3の例: 締日[10,20,99]のA社と[20]のB社が混在する状態で締日20の回を実行すると、
		// DayFromが取引先ごとに異なること(6.2-3)。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(new MasterTokui { Code = "B001", Shime1 = 20, PayMonth = 0, PayDay = 0 });
		var idA = db.Single<MasterTokui>("where Code=@0", "A001").Id;
		var idB = db.Single<MasterTokui>("where Code=@0", "B001").Id;
		db.Insert(CreateBillingUriage("20260915", idA, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateBillingUriage("20260825", idB, EnumUri00.Uriage, 500, 0));

		summaryDb.CalcSummaryUriSei("202609", 20);

		var rowA = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idA, "20260920");
		var rowB = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idB, "20260920");
		Assert.AreEqual("20260911", rowA.DayFrom, "A社の直前の締めは同月10日");
		Assert.AreEqual("20260821", rowB.DayFrom, "B社の直前の締めは前月20日");
		Assert.AreNotEqual(rowA.DayFrom, rowB.DayFrom, "同じ締日20の回でもDayFromは取引先ごとに異なる");
		Assert.AreEqual(1000, rowA.Uriage);
		Assert.AreEqual(500, rowB.Uriage);
	}

	[TestMethod]
	public void CalcSummaryUriSei_UnsetShimeFallsBackToOwnClosingDay() {
		// Shime1=0の得意先がMasterSysman.ShimeBiの回で集計対象になること(3.1、6.2-4)。
		var db = PrepareUriSeiTables(); // AddOwnClosingDayでShimeBi=99
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "C001", Shime1 = 0, PayMonth = 0, PayDay = 0 });
		var idC = db.Single<MasterTokui>("where Code=@0", "C001").Id;
		db.Insert(CreateBillingUriage("20260910", idC, EnumUri00.Uriage, 700, 0));

		summaryDb.CalcSummaryUriSei("202609", 99);

		var row = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idC, "20260930");
		Assert.AreEqual(700, row.Uriage, "Shime1=0は自社締日(99)の回で集計される");
	}

	[TestMethod]
	public void CalcSummaryUriSei_RerunningOneClosingDayKeepsOtherClosingDayRows() {
		// 締日10の回を再実行しても締日20・99の行が消えないこと(DELETEスコープの回帰防止、6.2-5)。
		var db = PrepareUriSeiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "A001").Id;
		db.Insert(CreateBillingUriage("20260905", idTokui, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateBillingUriage("20260915", idTokui, EnumUri00.Uriage, 2000, 0));
		db.Insert(CreateBillingUriage("20260925", idTokui, EnumUri00.Uriage, 3000, 0));

		summaryDb.CalcSummaryUriSei("202609", 10);
		summaryDb.CalcSummaryUriSei("202609", 20);
		summaryDb.CalcSummaryUriSei("202609", 99);
		summaryDb.CalcSummaryUriSei("202609", 10); // 締日10だけ再実行

		var rows = db.Fetch<SummaryUriSei>("where Id_Tokui=@0 order by DenDay", idTokui);
		Assert.AreEqual(3, rows.Count, "締日10の再実行で20・99の行が消えてはいけない");
		CollectionAssert.AreEqual(new[] { "20260910", "20260920", "20260930" }, rows.Select(x => x.DenDay).ToArray());
	}

	[TestMethod]
	public void CalcSummaryUriSei_PayDayZero_UsesOwnClosingDayAsNyukinYoteiDay() {
		// PayDay=0 かつ ShimeBi=20 のとき NyukinYoteiDayが20日になること(3.4、6.2-6)。
		var db = PrepareUriSeiTables();
		db.Execute($"UPDATE {nameof(MasterSysman)} SET ShimeBi=@0", 20);
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 99, PayMonth = 0, PayDay = 0 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "A001").Id;
		db.Insert(CreateBillingUriage("20260910", idTokui, EnumUri00.Uriage, 1000, 0));

		summaryDb.CalcSummaryUriSei("202609", 99);

		var row = db.Single<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", idTokui, "20260930");
		Assert.AreEqual("20260920", row.NyukinYoteiDay, "PayDay=0はShimeBi(20)を予定日の日として使う");
	}

	[TestMethod]
	public void CalcSummaryKaiShi_MultiShime_SplitsPeriodsPerClosingDay() {
		// 請求残と同じ検証を支払残(CalcSummaryKaiShi)でも行う(6.2-7)。
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		var idShiire = db.Single<MasterShiire>("where Code=@0", "A001").Id;
		db.Insert(CreateBillingShiire("20260905", idShiire, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateBillingShiire("20260915", idShiire, EnumShiire.Shiire, 2000, 0));
		db.Insert(CreateBillingShiire("20260925", idShiire, EnumShiire.Shiire, 3000, 0));

		summaryDb.CalcSummaryKaiShi("202609", 10);
		summaryDb.CalcSummaryKaiShi("202609", 20);
		summaryDb.CalcSummaryKaiShi("202609", 99);

		var rows = db.Fetch<SummaryKaiShi>("where Id_Shiire=@0 order by DenDay", idShiire);
		Assert.AreEqual(3, rows.Count);
		Assert.AreEqual("20260901", rows[0].DayFrom);
		Assert.AreEqual("20260910", rows[0].DayTo);
		Assert.AreEqual(1000, rows[0].Shiire);
		Assert.AreEqual("20260911", rows[1].DayFrom);
		Assert.AreEqual("20260920", rows[1].DayTo);
		Assert.AreEqual(2000, rows[1].Shiire);
		Assert.AreEqual("20260921", rows[2].DayFrom);
		Assert.AreEqual("20260930", rows[2].DayTo);
		Assert.AreEqual(3000, rows[2].Shiire);
	}

	[TestMethod]
	public void CalcSummaryKaiShi_MixedClosingPatterns_DayFromDiffersPerOwner() {
		// 2.3の例を支払残(CalcSummaryKaiShi)でも確認する(6.2-7)。
		var db = PrepareKaiShiTables();
		var summaryDb = new SummaryDb(db);
		db.Insert(new MasterShiire { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		db.Insert(new MasterShiire { Code = "B001", Shime1 = 20, PayMonth = 0, PayDay = 0 });
		var idA = db.Single<MasterShiire>("where Code=@0", "A001").Id;
		var idB = db.Single<MasterShiire>("where Code=@0", "B001").Id;
		db.Insert(CreateBillingShiire("20260915", idA, EnumShiire.Shiire, 1000, 0));
		db.Insert(CreateBillingShiire("20260825", idB, EnumShiire.Shiire, 500, 0));

		summaryDb.CalcSummaryKaiShi("202609", 20);

		var rowA = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", idA, "20260920");
		var rowB = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", idB, "20260920");
		Assert.AreEqual("20260911", rowA.DayFrom);
		Assert.AreEqual("20260821", rowB.DayFrom);
		Assert.AreNotEqual(rowA.DayFrom, rowB.DayFrom);
	}

	/// <summary>
	/// 締日[10,20,99]・9/5・9/15・9/25の売上を持つ得意先1件の請求残フィクスチャを、
	/// 個別実行と「すべての締日」実行を比較するために独立したDBへ作る(共有 <c>_db</c> は使わない)。
	/// </summary>
	private static SummaryDb BuildMultiShimeUriSeiFixture(out ExDatabaseSqlite db) {
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = $"MultiShimeUriSei-{Guid.NewGuid():N}",
			Mode = SqliteOpenMode.Memory,
		}.ToString();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { ShimeBi = 99 });
		db.CreateTable(typeof(SummaryUriSei), true, false);
		db.CreateTable(typeof(MasterTokui), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		InsertKinMaster(db);
		db.Insert(new MasterTokui { Code = "A001", Shime1 = 10, Shime2 = 20, Shime3 = 99, PayMonth = 0, PayDay = 0 });
		var idTokui = db.Single<MasterTokui>("where Code=@0", "A001").Id;
		db.Insert(CreateBillingUriage("20260905", idTokui, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateBillingUriage("20260915", idTokui, EnumUri00.Uriage, 2000, 0));
		db.Insert(CreateBillingUriage("20260925", idTokui, EnumUri00.Uriage, 3000, 0));
		return new SummaryDb(db);
	}

	/// <summary>Id_Tokui(DB採番のため実行間で値が変わりうる)を除いた比較用スナップショット。</summary>
	private static string[] GetUriSeiComparableSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryUriSei>("order by DenDay")
			.Select(x => $"{x.DenDay}:{x.DayFrom}:{x.DayTo}:{x.Balance}:{x.TotalIn}:{x.TotalSales}:{x.Uriage}:{x.NyukinYoteiDay}")];

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
	public void SummaryRebuildClosingCheck_UsesMonthEndAndRejectsOutOfRangeShime() {
		// 締日の有効値は1〜28と99に統一済み(3.5)。1〜28はどの月にも実在するため月末丸め(Math.Min)は
		// 到達しなくなり、丸めが効くのは99(末日)だけになった。範囲外は false で弾く。
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260228", 1, out var firstDay));
		Assert.AreEqual("20260201", firstDay);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260228", 28, out var february28));
		Assert.AreEqual("20260228", february28);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260428", 28, out var april28));
		Assert.AreEqual("20260428", april28);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260201", 99, out var februaryEnd));
		Assert.AreEqual("20260228", februaryEnd);
		Assert.IsTrue(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20240201", 99, out var leapFebruaryEnd));
		Assert.AreEqual("20240229", leapFebruaryEnd, "うるう年の末日締めは29日");
		foreach (var invalid in new[] { 0, 29, 30, 31, 100 }) {
			Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20260201", invalid, out _),
				$"締日{invalid}は有効値(1〜28,99)ではないため受け付けてはいけない");
		}
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_TreatsNullEmptyAndInvalidDayToAsMismatch() {
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay(null, 31, out _));
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay(string.Empty, 31, out _));
		Assert.IsFalse(SummaryRebuildClosingCheck.TryGetExpectedClosingDay("20261331", 31, out _));
	}

	[TestMethod]
	public void SummaryRebuildClosingCheck_MultiShime_MatchesAnyElementOfTheSet() {
		// 6.3: 保存済みDayToが有効締日集合(複数)のいずれかに一致すれば正常、どれとも一致しなければ不一致。
		// 要素数違い(3件 vs 1件)・値違いのケースを合わせて確認する。
		var matches = new[] {
			new SummaryClosingCheckRow { TorihikiCode = "T010", DayTo = "20260910", Shime1 = 10, Shime2 = 20, Shime3 = 99 },
			new SummaryClosingCheckRow { TorihikiCode = "T020", DayTo = "20260920", Shime1 = 10, Shime2 = 20, Shime3 = 99 },
			new SummaryClosingCheckRow { TorihikiCode = "T099", DayTo = "20260930", Shime1 = 10, Shime2 = 20, Shime3 = 99 },
		};
		Assert.AreEqual(0, SummaryRebuildClosingCheck.FindMismatches("売掛", matches).Count,
			"保存済みDayToが集合(要素数3)のいずれかに一致すれば正常");

		var mismatch = new[] {
			new SummaryClosingCheckRow { TorihikiCode = "T015", DayTo = "20260915", Shime1 = 10, Shime2 = 20, Shime3 = 99 },
		};
		Assert.AreEqual(1, SummaryRebuildClosingCheck.FindMismatches("売掛", mismatch).Count,
			"どの要素とも一致しない日付は不一致");

		var fewerElements = new[] {
			new SummaryClosingCheckRow { TorihikiCode = "T020b", DayTo = "20260920", Shime1 = 20 },
		};
		Assert.AreEqual(0, SummaryRebuildClosingCheck.FindMismatches("売掛", fewerElements).Count,
			"要素数が1件(単一締日)でも保存値と一致すれば正常");
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
		// UriClosingCheckSql / KaiClosingCheckSql が自社締日(ClosingDaySet.OwnShimeSubquerySql)を
		// サブクエリで読むため必要(4.5 #4)。
		db.CreateTable(typeof(MasterSysman), true, false);
		db.Insert(new MasterSysman { ShimeBi = 99 });
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
		// 請求計算は締日未設定(Shime1=0)のフォールバックとPayDay=0の予定日算出で自社締日を使うため、
		// MasterSysmanが必ず要る(複数締日対応 3.1/3.4)。PrepareKaiKakeTablesと扱いを揃える。
		AddOwnClosingDay(db);
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
		InsertMeisho(db, 201, MasterMeisho.KubunItem, "01", "別区分の01");
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
		// 支払計算も PrepareUriSeiTables と同じ理由で自社締日が必要。
		AddOwnClosingDay(db);
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

	/// <summary>
	/// Tran02Material(生地・付属仕入)の伝票を作る。区分99(その他)はA-6により丸めずTax1へ全額積む
	/// 特殊処理の対象であり、Tran03Shiireの区分99(Sonotaへ分離集計)とは別物であることを確認するために使う。
	/// </summary>
	private static Tran02Material CreateMaterial(string kakeDay, long idShiire, int kubun, int total, int tax) {
		var tran = new Tran02Material {
			DenDay = kakeDay,
			KakeDay = kakeDay,
			Id_Shiire = idShiire,
			Kubun = kubun,
			KingakuTotal = total,
			Total = Math.Abs(total) + tax,
			Tax1 = tax,
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			IsPay = 1,
		};
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
