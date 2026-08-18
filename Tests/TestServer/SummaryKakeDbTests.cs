using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 売掛(<see cref="SummaryUriKake"/>) / 買掛(<see cref="SummaryKaiKake"/>)集計のテスト。
/// <para>
/// 2026-08-16 の決定（`Doc/spec/2026-08-16_phase1_業務仕様決定ドラフト.md` 3.5）を固定する。
/// 区分別内訳、`IsPay` による除外、対象期間より後の月の再計算、入金・支払の `KakeDay` 基準を対象にする。
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
	public void CalcSummaryUriKake_SplitsSalesByKubun_AndKeepsBreakdownSum() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 100));
		db.Insert(CreateUriage("20260711", 1, EnumUri00.UriSale, 500, 50));
		db.Insert(CreateUriage("20260712", 1, EnumUri00.Henpin, 200, 20));
		db.Insert(CreateUriage("20260713", 1, EnumUri00.HenSale, 100, 10));
		db.Insert(CreateUriage("20260714", 1, EnumUri00.Nebiki, 300, 30));
		db.Insert(CreateUriage("20260715", 1, EnumUri00.Other, 700, 70));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		// その他(99)は売上へ含める。返品・値引は CalcFlag=-1 なのでマイナスで入る
		Assert.AreEqual(1000 + 500 + 700, row.Uriage);
		Assert.AreEqual(-(200 + 100), row.Henpin);
		Assert.AreEqual(-300, row.Nebiki);
		// 内訳の合計 = 全区分の符号付き合計
		Assert.AreEqual(1000 + 500 + 700 - 200 - 100 - 300, row.Uriage + row.Henpin + row.Nebiki);
		Assert.AreEqual(100 + 50 + 70 - 20 - 10 - 30, row.Tax);
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
	public void CalcSummaryUriKake_SplitsReceiptsByKinCode_AndFallsBackToOther() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateNyukin("20260710", 1, [
			(KinCash, 5000),
			(KinFee, 440),
			(KinDensai, 3000),
			(KinOffset, 200),
			(KinOther, 60),
			// KIN マスタに存在しない Id_Kin。移行途中の 0 と同じ扱いで「その他」へ寄せる
			(0, 7),
		]));

		summaryDb.CalcSummaryUriKake("202607", "202607");
		var row = db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(5000, row.Cash);
		Assert.AreEqual(440, row.Fee);
		Assert.AreEqual(3000, row.Densai);
		Assert.AreEqual(200, row.Offset);
		Assert.AreEqual(60 + 7, row.Other);
		// 内訳の合計は必ずヘッダ総額に一致する
		Assert.AreEqual(row.TotalIn, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
		Assert.AreEqual(5000 + 440 + 3000 + 200 + 60 + 7, row.TotalIn);
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
		Assert.AreEqual(-1200, august.Balance);
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
		Assert.AreEqual(1000, rows[0].Balance);
		Assert.AreEqual(600, rows[1].Balance);
	}

	[TestMethod]
	public void CalcSummaryUriKake_RecalculatesMonthsAfterTargetPeriod() {
		var db = PrepareUriKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateUriage("20260710", 1, EnumUri00.Uriage, 1000, 0));
		db.Insert(CreateUriage("20260810", 1, EnumUri00.Uriage, 500, 0));
		summaryDb.CalcSummaryUriKake("202607", "202608");
		Assert.AreEqual(1500, db.Single<SummaryUriKake>("where Id_Tokui=@0 and DenMonth=@1", 1, "202608").Balance);

		// 7月の伝票を増やして7月だけを指定して再計算する。8月の繰越も追随しなければならない
		db.Insert(CreateUriage("20260720", 1, EnumUri00.Uriage, 300, 0));
		summaryDb.CalcSummaryUriKake("202607", "202607");
		var rows = db.Fetch<SummaryUriKake>("where Id_Tokui=@0 order by DenMonth", 1);

		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual(1300, rows[0].Balance);
		Assert.AreEqual(1800, rows[1].Balance);
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
		Assert.AreEqual(90, row.Tax);
		Assert.AreEqual(790, row.TotalSales);
		Assert.AreEqual(340, row.TotalIn);
		Assert.AreEqual(-950, row.Balance);
		Assert.AreEqual("1-20260731-01", row.SeikyuNo);
		Assert.AreEqual(1, row.Renban);
		Assert.AreEqual("20260731", row.NyukinYoteiDay);
		Assert.IsNull(db.FirstOrDefault<SummaryUriSei>("where Id_Tokui=@0 and DenDay=@1", 2, "20260720"));
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
	public void CalcSummaryKaiKake_SplitsPurchasesAndPayments() {
		var db = PrepareKaiKakeTables();
		var summaryDb = new SummaryDb(db);

		db.Insert(CreateShiire("20260710", 1, EnumShiire.Shiire, 1000, 100));
		db.Insert(CreateShiire("20260711", 1, EnumShiire.Henpin, 200, 20));
		db.Insert(CreateShiire("20260712", 1, EnumShiire.Nebiki, 100, 10));
		db.Insert(CreateShiire("20260713", 1, EnumShiire.Other, 400, 40));
		db.Insert(CreateShiharai("20260714", 1, [(KinCash, 600), (KinOffset, 50)]));

		summaryDb.CalcSummaryKaiKake("202607", "202607");
		var row = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", 1, "202607");

		Assert.AreEqual(1000 + 400, row.Shiire);
		Assert.AreEqual(-200, row.Henpin);
		Assert.AreEqual(-100, row.Nebiki);
		Assert.AreEqual(1000 + 400 - 200 - 100, row.Shiire + row.Henpin + row.Nebiki);
		Assert.AreEqual(600, row.Cash);
		Assert.AreEqual(50, row.Offset);
		Assert.AreEqual(row.TotalOut, row.Cash + row.Fee + row.Densai + row.Offset + row.Other);
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
		Assert.AreEqual(90, row.Tax);
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

	// ---- 準備 --------------------------------------------------------------------

	/// <summary>KIN 区分マスタの Id。実DBの値ではなくテスト内で採番した値を使う</summary>
	private const long KinCash = 101;
	private const long KinFee = 102;
	private const long KinDensai = 103;
	private const long KinOffset = 104;
	private const long KinOther = 105;

	private ExDatabaseSqlite PrepareUriKakeTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryUriKake), true, false);
		db.CreateTable(typeof(Tran00Uriage), true, false);
		db.CreateTable(typeof(Tran06Nyukin), true, false);
		InsertKinMaster(db);
		return db;
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
		db.CreateTable(typeof(SummaryKaiKake), true, false);
		db.CreateTable(typeof(Tran03Shiire), true, false);
		db.CreateTable(typeof(Tran07Shiharai), true, false);
		InsertKinMaster(db);
		return db;
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
		// 同じ Code を持つ別区分に引っ張られないことを担保する
		InsertMeisho(db, 201, "ITM", "01", "別区分の01");
	}

	private static void InsertMeisho(ExDatabaseSqlite db, long id, string kubun, string code, string name) =>
		db.Execute(
			$"INSERT INTO {nameof(MasterMeisho)} (Id, Kubun, Code, Name) VALUES (@0, @1, @2, @3)",
			id, kubun, code, name);

	private static Tran00Uriage CreateUriage(string kakeDay, long idTokui, EnumUri00 kubun, int kingaku, int tax) {
		var tran = new Tran00Uriage {
			DenDay = kakeDay,
			KakeDay = kakeDay,
			Id_Tokui = idTokui,
			KingakuTotal = kingaku,
			Tax = tax,
			IsPay = 1,
		};
		tran.EnKubun = kubun;
		return tran;
	}

	private ExDatabaseSqlite PrepareKaiShiTables() {
		var db = _db ?? throw new AssertFailedException("Database not initialized");
		db.CreateTable(typeof(SummaryKaiShi), true, false);
		db.CreateTable(typeof(MasterShiire), true, false);
		db.CreateTable(typeof(Tran03Shiire), true, false);
		db.CreateTable(typeof(Tran07Shiharai), true, false);
		InsertKinMaster(db);
		return db;
	}

	private static Tran00Uriage CreateBillingUriage(string kakeDay, long idTokui, EnumUri00 kubun, int total, int tax) {
		var tran = CreateUriage(kakeDay, idTokui, kubun, total, tax);
		tran.Total = total;
		return tran;
	}

	private static Tran03Shiire CreateBillingShiire(string kakeDay, long idShiire, EnumShiire kubun, int total, int tax) {
		var tran = CreateShiire(kakeDay, idShiire, kubun, total, tax);
		tran.Total = total;
		return tran;
	}

	private static Tran03Shiire CreateShiire(string kakeDay, long idShiire, EnumShiire kubun, int kingaku, int tax) {
		var tran = new Tran03Shiire {
			DenDay = kakeDay,
			KakeDay = kakeDay,
			Id_Shiire = idShiire,
			KingakuTotal = kingaku,
			Tax = tax,
			IsPay = 1,
		};
		tran.EnKubun = kubun;
		return tran;
	}

	private static Tran06Nyukin CreateNyukin(string kakeDay, long idTorisaki, (long IdKin, int Kingaku)[] meisai) =>
		new() {
			KakeDay = kakeDay,
			Id_Torisaki = idTorisaki,
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = BuildKinMeisai(meisai),
		};

	private static Tran07Shiharai CreateShiharai(string kakeDay, long idTorisaki, (long IdKin, int Kingaku)[] meisai) =>
		new() {
			KakeDay = kakeDay,
			Id_Torisaki = idTorisaki,
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = BuildKinMeisai(meisai),
		};

	private static List<TranKinMeisai> BuildKinMeisai((long IdKin, int Kingaku)[] meisai) =>
		[.. meisai.Select((x, i) => new TranKinMeisai { No = i + 1, Id_Kin = x.IdKin, Kingaku = x.Kingaku })];

	private static string[] GetUriKakeSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryUriKake>("order by Id_Tokui, DenMonth")
			.Select(x => $"{x.Id_Tokui}:{x.DenMonth}:{x.Balance}:{x.TotalIn}:{x.TotalSales}:{x.Uriage}:{x.Henpin}:{x.Nebiki}:{x.Tax}:{x.Cash}:{x.Fee}:{x.Densai}:{x.Offset}:{x.Other}")];

	private static string[] GetKaiShiSnapshot(ExDatabaseSqlite db) =>
		[.. db.Fetch<SummaryKaiShi>("order by Id_Shiire, DenDay")
			.Select(x => $"{x.Id_Shiire}:{x.DenDay}:{x.Balance}:{x.TotalOut}:{x.TotalShiire}:{x.Shiire}:{x.Henpin}:{x.Nebiki}:{x.Tax}:{x.Cash}:{x.Fee}:{x.Densai}:{x.Offset}:{x.Other}:{x.ShiharaiYoteiDay}")];
}
