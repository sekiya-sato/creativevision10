using CvBase.Share;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Tests.CvServer;

/// <summary>
/// HHTデータ更新（<see cref="HhtProcess.UpdateVulcan2Tran"/>）の変換規則を固定する。
/// <para>
/// 仕様は `Doc/spec/archive/2026-08-24_HHTデータ更新詳細設計.md`。
/// 実DB(server-user163.db)のHHTデータは商品・店舗の採番空間がマスタと一致しないため、
/// 正常系はここでテスト用マスタを作って検証する（同ドキュメント 13-2 / 14.3）。
/// </para>
/// </summary>
[TestClass]
public class HhtProcessUpdateTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	/// <summary>テスト用マスタの固定値</summary>
	private const string SokoCode = "000016";
	private const string TenpoCode = "000022";
	private const string IdoCode = "000017";
	private const string TokuiCode = "100003";
	private const string ShiireCode = "001";
	private const string ShainCode = "000505";
	private const string Jan1Ok = "2830000000018";
	private const string Jan3Ok = "2800323016157";
	private const string JanPairUpper = "2830000000025";
	private const string JanPairLower = "2800323026156";
	private const int TankaJodai = 1000;
	private const int TankaGenka = 400;

	private long _idSoko;
	private long _idTenpo;
	private long _idIdo;
	private long _idTokui;
	private long _idShiire;
	private long _idShain;
	private long _idShohin;

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"HhtProcessUpdateTests-{Guid.NewGuid():N}";
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
		PrepareTables();
		PrepareMasters();
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	#region 正常系

	/// <summary>棚卸は倉庫＋棚番単位で伝票が分かれ、棚番が TanaNo へ入る</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Tanaoroshi_SplitsByTanaNo() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok, su: 3);
		InsertVulcan(type0: 7, serial: 2, denNo: "00021201", jan1: Jan3Ok, su: 5);
		InsertVulcan(type0: 7, serial: 3, denNo: "00021301", jan1: Jan1Ok, su: 7);

		var result = Run();

		Assert.AreEqual(2, result.SlipCount, "棚番が変わると別伝票になる");
		Assert.AreEqual(3, result.SuccessRows);
		var slips = Db.Fetch<Tran60Tana>("order by TanaNo");
		Assert.AreEqual("00021201", slips[0].TanaNo);
		Assert.AreEqual(2, slips[0].Jmeisai?.Count, "同一棚番の2行が1伝票の明細になる");
		Assert.AreEqual(8, slips[0].SuTotal);
		Assert.AreEqual("00021301", slips[1].TanaNo);
		Assert.AreEqual(7, slips[1].SuTotal);

		var rows = Db.Fetch<TranVulcanHht>("order by Serial");
		Assert.IsTrue(rows.All(x => x.VdCnvDate > 0), "変換済みは VdCnvDate が入る");
		Assert.IsTrue(rows.All(x => x.TargetTableName == nameof(Tran60Tana)), "対象テーブル名が入る");
		Assert.IsTrue(rows.All(x => x.TargetId > 0), "対象Idが入る");
		Assert.IsTrue(rows.All(x => x.ErrorMsg == string.Empty), "エラーは残らない");
	}

	/// <summary>卸売は本部売上になり、掛率が Rate、消費税は税率から自動計算される</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Oroshi_SetsRateAsKakeritsuAndCalculatesTax() {
		// 掛率は5桁前0埋めで 999.9 を整数表現する。"00650" は 65.0%
		InsertVulcan(type0: 9, serial: 1, shop: SokoCode, toriSaki: TokuiCode,
			denNo: "00001234", jan1: Jan1Ok, su: 2, tanka: 500, kakeRitsu: "00650");

		var result = Run();

		Assert.AreEqual(1, result.SlipCount);
		var slip = Db.Fetch<Tran00Uriage>().Single();
		Assert.AreEqual(_idTokui, slip.Id_Tokui);
		Assert.AreEqual(_idSoko, slip.Id_Soko);
		Assert.AreEqual((int)EnumUri00.Uriage, slip.Kubun);
		Assert.AreEqual(1, slip.CalcFlag);
		Assert.AreEqual(1, slip.IsPay, "掛計上しないと売掛集計へ入らない");
		Assert.AreEqual(65, slip.Rate, "Rate は掛率のパーセント整数（10倍整数ではない）");
		Assert.AreEqual(1000, slip.KingakuTotal, "数量2 × 単価500");
		Assert.AreEqual(100, slip.Tax1, "税率10%（MasterSysman）から自動計算する");
		Assert.AreEqual(1100, slip.Total);
		var meisai = slip.Jmeisai!.Single();
		Assert.AreEqual(TankaJodai, meisai.Jodai);
		Assert.AreEqual(650, meisai.Gedai, "下代 = 上代1000 × 掛率65.0%");
	}

	/// <summary>卸返品は返品区分になり、数量が負で計上される</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_OroshiHenpin_NegatesQuantity() {
		InsertVulcan(type0: 10, serial: 1, shop: SokoCode, toriSaki: TokuiCode, jan1: Jan1Ok, su: 3, tanka: 500);

		Run();

		var slip = Db.Fetch<Tran00Uriage>().Single();
		Assert.AreEqual((int)EnumUri00.Henpin, slip.Kubun);
		Assert.AreEqual(-1, slip.CalcFlag, "返品は CalcFlag=-1");
		Assert.AreEqual(-3, slip.SuTotal, "数量は負で格納する");
		Assert.AreEqual(-1500, slip.KingakuTotal);
		Assert.AreEqual(150, slip.Tax1, "税額は絶対値ベースで計算する");
	}

	/// <summary>セールは区分11、プロパーは区分10になる</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_HanKubunSale_UsesSaleKubun() {
		InsertVulcan(type0: 9, serial: 1, shop: SokoCode, toriSaki: TokuiCode, jan1: Jan1Ok, hanKubun: 1);

		Run();

		Assert.AreEqual((int)EnumUri00.UriSale, Db.Fetch<Tran00Uriage>().Single().Kubun);
	}

	/// <summary>店舗売上は倉庫と店舗が同じIdになり、顧客CDが解決される</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Uriage_ResolvesCustomer() {
		InsertVulcan(type0: 1, serial: 1, shop: TenpoCode, denNo: "9990000000001", jan1: Jan1Ok);

		Run();

		var slip = Db.Fetch<Tran01Tenuri>().Single();
		Assert.AreEqual(_idTenpo, slip.Id_Soko);
		Assert.AreEqual(_idTenpo, slip.Id_Tenpo, "店舗売上は倉庫と店舗が同じ");
		Assert.AreEqual("9990000000001", slip.Code_Customer);
		Assert.IsTrue(slip.Id_Customer > 0, "登録済みの顧客CDはIdが解決される");
		Assert.AreEqual(0, slip.Rate, "Rate は掛率。店舗売上に掛率は来ない");
	}

	/// <summary>入庫(積送受)は Shop が移動先、取引先が移動元になる</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Nyuko_MapsShopToIdoSaki() {
		InsertVulcan(type0: 3, serial: 1, shop: SokoCode, toriSaki: IdoCode, jan1: Jan1Ok, su: 4);

		Run();

		var slip = Db.Fetch<Tran11IdoIn>().Single();
		Assert.AreEqual(_idSoko, slip.Id_Ido, "受け側でスキャンするので Shop は移動先(Id_Ido)");
		Assert.AreEqual(_idIdo, slip.Id_Soko, "取引先が出庫元(Id_Soko)");
	}

	/// <summary>出庫(積送出)は Shop が倉庫、取引先が移動先になる</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Shukko_MapsShopToSoko() {
		InsertVulcan(type0: 4, serial: 1, shop: SokoCode, toriSaki: IdoCode, jan1: Jan1Ok, su: 4);

		Run();

		var slip = Db.Fetch<Tran10IdoOut>().Single();
		Assert.AreEqual(_idSoko, slip.Id_Soko);
		Assert.AreEqual(_idIdo, slip.Id_Ido);
	}

	/// <summary>仕入は掛率欄の発注番号が RelateNo1 へ入る</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Shiire_StoresHachuNoInRelateNo1() {
		InsertVulcan(type0: 5, serial: 1, shop: SokoCode, toriSaki: ShiireCode, jan1: Jan1Ok, kakeRitsu: "00000777");

		Run();

		var slip = Db.Fetch<Tran03Shiire>().Single();
		Assert.AreEqual(_idShiire, slip.Id_Shiire);
		Assert.AreEqual(777, slip.RelateNo1, "仕入の掛率欄は発注番号");
		Assert.AreEqual(0, slip.Rate, "Rate は掛率。仕入に掛率は来ない");
	}

	/// <summary>発注は掛率欄の納品日が NouhinDay へ入る</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Hachu_StoresNouhinDay() {
		InsertVulcan(type0: 8, serial: 1, shop: SokoCode, toriSaki: ShiireCode, jan1: Jan1Ok, kakeRitsu: "20260315");

		Run();

		Assert.AreEqual("20260315", Db.Fetch<Tran13Hachu>().Single().NouhinDay);
	}

	/// <summary>移動(即時)は Shop が倉庫、取引先が移動先。委託と掛率はメモへ残す</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Ido_RecordsItakuAndKakeritsuInMemo() {
		InsertVulcan(type0: 11, serial: 1, shop: SokoCode, toriSaki: IdoCode, jan1: Jan1Ok, hanKubun: 1, kakeRitsu: "00650");

		Run();

		var slip = Db.Fetch<Tran05Ido>().Single();
		Assert.AreEqual(_idSoko, slip.Id_Soko);
		Assert.AreEqual(_idIdo, slip.Id_Ido);
		StringAssert.Contains(slip.Memo, "委託", "移動伝票に買取/委託の列がないためメモへ残す");
		StringAssert.Contains(slip.Memo, "掛率=65", "移動伝票に掛率の列がないためメモへ残す");
	}

	/// <summary>上段と下段の両方があるときは Jan1 と Jan2 のAND条件で照合する</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_JanPair_MatchesByAndCondition() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: JanPairUpper, jan2: JanPairLower);

		var result = Run();

		Assert.AreEqual(1, result.SlipCount);
		Assert.AreEqual("2", Db.Fetch<Tran60Tana>().Single().Jmeisai!.Single().Code_Siz);
	}

	/// <summary>上段と下段の組み合わせが一致しなければ商品未登録になる</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_JanPairMismatch_ReportsNotFound()	{
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: JanPairUpper, jan2: Jan3Ok);

		var result = Run();

		Assert.AreEqual(0, result.SlipCount);
		StringAssert.StartsWith(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E103");
	}

	/// <summary>同じヘッダキーが非連続で再出現したら別伝票にする（連続ラン方式）</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_NonContiguousSameKey_SplitsSlips() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok);
		InsertVulcan(type0: 7, serial: 2, denNo: "00021301", jan1: Jan1Ok);
		InsertVulcan(type0: 7, serial: 3, denNo: "00021201", jan1: Jan3Ok);

		var result = Run();

		Assert.AreEqual(3, result.SlipCount, "非連続で同キーが戻ってきたら別伝票にする");
	}

	/// <summary>客数は対応する伝票がないため、伝票を作らず完了扱いにする</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Kyakusu_MarksConvertedWithoutSlip() {
		InsertVulcan(type0: 12, serial: 1, shop: TenpoCode, denNo: "00000042", jan1: Jan1Ok);

		var result = Run();

		Assert.AreEqual(0, result.SlipCount);
		Assert.AreEqual(1, result.SkippedRows);
		var row = Db.Fetch<TranVulcanHht>().Single();
		Assert.IsTrue(row.VdCnvDate > 0, "対象外でも未変換のまま残さない");
		Assert.AreEqual(string.Empty, row.TargetTableName, "伝票を作らないので対象テーブル名は空");
		Assert.AreEqual(string.Empty, row.ErrorMsg, "対象外はエラーではない");
	}

	#endregion

	#region エラー系

	/// <summary>店舗が未登録なら伝票を作らず E010 を残し、未変換へ戻す</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_UnknownShop_ReportsE010() {
		InsertVulcan(type0: 7, serial: 1, shop: "00008002", denNo: "00021201", jan1: Jan1Ok);

		var result = Run();

		Assert.AreEqual(0, result.SlipCount);
		Assert.AreEqual(1, result.ErrorRows);
		Assert.AreEqual(0, Db.Fetch<Tran60Tana>().Count, "エラー時は伝票を作らない");
		var row = Db.Fetch<TranVulcanHht>().Single();
		Assert.AreEqual(0, row.VdCnvDate, "エラーは未変換(0)へ戻す");
		StringAssert.StartsWith(row.ErrorMsg, "E010");
	}

	/// <summary>JANがマスタに無ければ E103</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_UnknownJan_ReportsE103() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: "2300000887211");

		Run();

		StringAssert.StartsWith(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E103");
	}

	/// <summary>JANが8桁未満なら E105（サイズCDの誤登録と衝突させない）</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_ShortJan_ReportsE105() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: "24");

		Run();

		StringAssert.StartsWith(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E105");
	}

	/// <summary>
	/// マスタ側の Jan1 に2桁のサイズCDが誤登録されていても、正しいJANの照合が壊れない。
	/// 桁数フィルタを外すと E104（複数一致）が誤発火する。
	/// </summary>
	[TestMethod]
	public void UpdateVulcan2Tran_ShortJanInMaster_DoesNotBreakMatching() {
		// 実データと同じ汚染を再現する（サイズCD "24" が Jan1 に入っている行を複数作る）
		Db.Insert(new DerivedShohinColSiz { Id_Shohin = _idShohin, RowIdx = 90, Code = "TEST", Jan1 = "24" });
		Db.Insert(new DerivedShohinColSiz { Id_Shohin = _idShohin, RowIdx = 91, Code = "TEST", Jan1 = "24" });
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok);

		var result = Run();

		Assert.AreEqual(1, result.SlipCount, "2桁の誤登録は照合対象外なので影響しない");
	}

	/// <summary>数量0は E110</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_ZeroQuantity_ReportsE110() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok, su: 0);

		Run();

		StringAssert.StartsWith(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E110");
	}

	/// <summary>日付が不正なら E002</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_InvalidDate_ReportsE002() {
		InsertVulcan(type0: 7, serial: 1, denDay: "20261301", denNo: "00021201", jan1: Jan1Ok);

		Run();

		StringAssert.Contains(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E002");
	}

	/// <summary>社販(販売区分=2)は未対応なので E015</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Shahan_ReportsE015() {
		InsertVulcan(type0: 9, serial: 1, shop: SokoCode, toriSaki: TokuiCode, jan1: Jan1Ok, hanKubun: 2);

		Run();

		StringAssert.Contains(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E015");
	}

	/// <summary>卸売に倉庫コードを渡したら店種区分の不一致 E013</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_WrongTenType_ReportsE013() {
		InsertVulcan(type0: 9, serial: 1, shop: SokoCode, toriSaki: SokoCode, jan1: Jan1Ok);

		Run();

		StringAssert.Contains(Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "E013");
	}

	/// <summary>同一バッチ内の重複受信は先頭だけを変換し、残りを E016 にする</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_DuplicateInBatch_ReportsE016() {
		InsertVulcan(type0: 7, serial: 10, denNo: "00021201", jan1: Jan1Ok, fileName: "20260402124512_001.txt", lineNo: 1);
		InsertVulcan(type0: 7, serial: 10, denNo: "00021201", jan1: Jan1Ok, fileName: "20260402124554_001.txt", lineNo: 1);

		var result = Run();

		Assert.AreEqual(1, result.SlipCount, "重複した2件目は伝票を作らない");
		Assert.AreEqual(1, result.DuplicateRows);
		var dup = Db.Fetch<TranVulcanHht>("where VdCnvDate = 0").Single();
		StringAssert.StartsWith(dup.ErrorMsg, "E016");
		Assert.AreEqual("20260402124554_001.txt", dup.BackupFileName, "ファイル名の昇順で後ろがエラーになる");
	}

	/// <summary>変換済みデータと同一キーの再受信も E016</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_DuplicateWithConverted_ReportsE016() {
		InsertVulcan(type0: 7, serial: 10, denNo: "00021201", jan1: Jan1Ok, fileName: "a.txt", lineNo: 1);
		Run();
		Assert.AreEqual(1, Db.Fetch<Tran60Tana>().Count);

		InsertVulcan(type0: 7, serial: 10, denNo: "00021201", jan1: Jan1Ok, fileName: "b.txt", lineNo: 1);
		var result = Run();

		Assert.AreEqual(0, result.SlipCount);
		Assert.AreEqual(1, result.DuplicateRows);
		StringAssert.StartsWith(Db.Fetch<TranVulcanHht>("where VdCnvDate = 0").Single().ErrorMsg, "E016");
	}

	/// <summary>同一伝票内に1行でもエラーがあれば伝票を作らず、他行には連鎖 E900 を残す</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_ErrorInSlip_MarksOtherRowsWithE900() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok);
		InsertVulcan(type0: 7, serial: 2, denNo: "00021201", jan1: "2300000887211");

		var result = Run();

		Assert.AreEqual(0, result.SlipCount, "1行でもエラーなら伝票全体を作らない");
		Assert.AreEqual(2, result.ErrorRows);
		var rows = Db.Fetch<TranVulcanHht>("order by Serial");
		StringAssert.StartsWith(rows[0].ErrorMsg, "E900", "原因行以外は連鎖エラーになる");
		StringAssert.StartsWith(rows[1].ErrorMsg, "E103");
	}

	/// <summary>再変換では前回のエラー内容を必ずクリアする（直したのに直らないと見えるのを防ぐ）</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Retry_ClearsPreviousError() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: "2300000887211");
		Run();
		Assert.AreNotEqual(string.Empty, Db.Fetch<TranVulcanHht>().Single().ErrorMsg);

		// 画面で修正した状態にしてから再実行する
		Db.Execute("update TranVulcanHht set Jan1=@0", Jan1Ok);
		var result = Run();

		Assert.AreEqual(1, result.SlipCount);
		Assert.AreEqual(string.Empty, Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "成功したらエラーは消える");
	}

	/// <summary>RetryError=false ならエラー済みの行を対象にしない</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_RetryErrorFalse_SkipsErrorRows() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: "2300000887211");
		Run();
		Db.Execute("update TranVulcanHht set Jan1=@0", Jan1Ok);

		var result = new HhtProcess(Db).UpdateVulcan2Tran(
			new HhtUpdateParameter(string.Empty, string.Empty, [], RetryError: false, []));

		Assert.AreEqual(0, result.SlipCount, "エラー行は対象外なので変換されない");
		Assert.AreNotEqual(string.Empty, Db.Fetch<TranVulcanHht>().Single().ErrorMsg, "ErrorMsgもクリアされない");
	}

	/// <summary>TargetIds を指定するとその行だけを対象にする（エラー修正画面からの再実行）</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_TargetIds_LimitsScope() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok);
		InsertVulcan(type0: 7, serial: 2, denNo: "00021301", jan1: Jan1Ok);
		var first = Db.Fetch<TranVulcanHht>("order by Serial")[0];

		var result = new HhtProcess(Db).UpdateVulcan2Tran(
			new HhtUpdateParameter(string.Empty, string.Empty, [], RetryError: true, [first.Id]));

		Assert.AreEqual(1, result.SlipCount);
		Assert.AreEqual(1, Db.Fetch<TranVulcanHht>("where VdCnvDate = 0").Count, "指定外の行は未変換のまま");
	}

	/// <summary>日付範囲と区分で対象を絞れる</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_DateAndTypeFilter_LimitsScope() {
		InsertVulcan(type0: 7, serial: 1, denDay: "20260130", denNo: "00021201", jan1: Jan1Ok);
		InsertVulcan(type0: 7, serial: 2, denDay: "20260228", denNo: "00021301", jan1: Jan1Ok);
		InsertVulcan(type0: 9, serial: 3, denDay: "20260130", shop: SokoCode, toriSaki: TokuiCode, jan1: Jan1Ok);

		var result = new HhtProcess(Db).UpdateVulcan2Tran(
			new HhtUpdateParameter("20260101", "20260131", [7], RetryError: true, []));

		Assert.AreEqual(1, result.SlipCount, "日付範囲と区分の両方で絞る");
		Assert.AreEqual(2, Db.Fetch<TranVulcanHht>("where VdCnvDate = 0").Count);
	}

	#endregion

	#region 在庫への反映

	/// <summary>仕入は倉庫在庫を増やす</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Shiire_IncreasesStock() {
		InsertVulcan(type0: 5, serial: 1, shop: SokoCode, toriSaki: ShiireCode, jan1: Jan1Ok, su: 6);

		Run();

		var stock = Db.Fetch<SummaryStock>($"where Id_Soko = {_idSoko}").Single();
		Assert.AreEqual("202601", stock.SumMonth);
		Assert.AreEqual(6, stock.Su, "仕入は入庫なので在庫が増える");
	}

	/// <summary>棚卸は在庫を動かさない（棚卸確定処理が ActualQty へ拾う）</summary>
	[TestMethod]
	public void UpdateVulcan2Tran_Tanaoroshi_DoesNotMoveStock() {
		InsertVulcan(type0: 7, serial: 1, denNo: "00021201", jan1: Jan1Ok, su: 6);

		Run();

		Assert.AreEqual(0, Db.Fetch<SummaryStock>().Count, "棚卸伝票は在庫集計へ入らない");
	}

	#endregion

	#region 準備

	private HhtProcess.HhtUpdateResult Run() =>
		new HhtProcess(Db).UpdateVulcan2Tran(
			new HhtUpdateParameter(string.Empty, string.Empty, [], RetryError: true, []));

	private void PrepareTables() {
		foreach (var t in new[] {
			typeof(TranVulcanHht), typeof(Tran00Uriage), typeof(Tran01Tenuri), typeof(Tran03Shiire),
			typeof(Tran02Material),
			typeof(Tran05Ido), typeof(Tran10IdoOut), typeof(Tran11IdoIn), typeof(Tran12Jyuchu),
			typeof(Tran13Hachu), typeof(Tran60Tana),
			typeof(MasterTokui), typeof(MasterShiire), typeof(MasterShain), typeof(MasterShohin),
			typeof(MasterEndCustomer), typeof(MasterSysman), typeof(DerivedShohinColSiz),
			typeof(SummaryStock), typeof(SummaryRealStock), typeof(SummaryUriKake), typeof(SummaryKaiKake),
			typeof(Tran06Nyukin), typeof(Tran07Shiharai), typeof(MasterMeisho),
		}) {
			Db.CreateTable(t, true, false);
		}
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
	}

	private void PrepareMasters() {
		// 消費税率10%
		Db.Insert(new MasterSysman { ShimeBi = 99, Jsub = [new MasterSysTax { Id = 1, TaxRate = 10, DateFrom = string.Empty, TaxNewRate = 0 }] });

		_idSoko = InsertTokui(SokoCode, (int)EnumTokui._0_Soko, isZaiko: 1);
		_idTenpo = InsertTokui(TenpoCode, (int)EnumTokui._6_Tenpo, isZaiko: 1);
		_idIdo = InsertTokui(IdoCode, (int)EnumTokui._0_Soko, isZaiko: 1);
		// 卸得意先は伝票単位(TaxCalcUnit=Slip)にして、HHT変換の税額計算(ApplyTaxOnly)が
		// 伝票へTax1を確定させることを検証できるようにする。既定(請求単位)のままだと
		// Doc/spec/2026-09-01 3.4 により伝票のTax1/2/3は常に0になり、税額計算の検証にならない。
		_idTokui = InsertTokui(TokuiCode, (int)EnumTokui._1_Oroshi, isZaiko: 0, taxCalcUnit: (int)EnumTaxCalcUnit.Slip);

		Db.Insert(new MasterShiire { Code = ShiireCode, Name = "仕入先1" });
		_idShiire = Db.Single<MasterShiire>("where Code=@0", ShiireCode).Id;

		Db.Insert(new MasterShain { Code = ShainCode, Name = "担当者" });
		_idShain = Db.Single<MasterShain>("where Code=@0", ShainCode).Id;

		Db.Insert(new MasterShohin { Code = "00211161001", Name = "テスト商品", TankaJodai = TankaJodai, TankaGenka = TankaGenka });
		_idShohin = Db.Single<MasterShohin>("where Code=@0", "00211161001").Id;

		// SKU 1: Jan1/Jan3 のどちらでも引ける（上段のみの照合）
		Db.Insert(new DerivedShohinColSiz {
			Id_Shohin = _idShohin, RowIdx = 1, Code = "00211161001",
			Code_Col = "014", Mei_Col = "赤", Code_Siz = "24", Mei_Siz = "24",
			Jan1 = Jan1Ok, Jan2 = string.Empty, Jan3 = Jan3Ok,
		});
		// SKU 2: Jan1 と Jan2 の組み合わせで引く（上段+下段の照合）
		Db.Insert(new DerivedShohinColSiz {
			Id_Shohin = _idShohin, RowIdx = 2, Code = "00211161001",
			Code_Col = "015", Mei_Col = "青", Code_Siz = "2", Mei_Siz = "M",
			Jan1 = JanPairUpper, Jan2 = JanPairLower, Jan3 = string.Empty,
		});

		Db.Insert(new MasterEndCustomer { Code = "9990000000001", Name = "顧客1" });
	}

	private long InsertTokui(string code, int tenType, int isZaiko, int taxCalcUnit = (int)EnumTaxCalcUnit.Billing) {
		Db.Insert(new MasterTokui { Code = code, Name = $"取引先{code}", TenType = tenType, IsZaiko = isZaiko, TaxCalcUnit = taxCalcUnit });
		return Db.Single<MasterTokui>("where Code=@0", code).Id;
	}

	private void InsertVulcan(
		int type0,
		int serial,
		string denDay = "20260130",
		string shop = SokoCode,
		string tanto = ShainCode,
		int hanKubun = 9,
		string denNo = "",
		string jan1 = "",
		string jan2 = "",
		int su = 1,
		int tanka = 0,
		string toriSaki = "00000000",
		string kakeRitsu = "00000000",
		string fileName = "test_001.txt",
		int lineNo = 0) {
		Db.Insert(new TranVulcanHht {
			Type0 = type0,
			HhtNo = 1,
			Serial = serial,
			DenDay = denDay,
			Shop = shop,
			Tanto = tanto,
			HanKubun = hanKubun,
			DenNo = denNo,
			Jan1 = jan1,
			Jan2 = jan2,
			Su = su,
			Tanka = tanka,
			ToriSaki = toriSaki,
			KakeRitsu = kakeRitsu,
			TotalCnt = 0,
			Filler = string.Empty,
			BackupFileName = fileName,
			// ComputerName/UserName は string? だが生成DDLは NOT NULL なので必ず入れる（受信画面も必ずセットする）
			ComputerName = "TESTPC",
			UserName = "tester",
			LineNo = lineNo == 0 ? serial : lineNo,
			VdCnvDate = 0,
		});
	}

	#endregion
}
