using System.Linq;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 出荷処理（<see cref="ShippingDb.CreateShippingSlips"/>）が
/// 仮想ヘッダ（<see cref="HaibunHeaderKey"/>・決定 I5）単位で伝票を作ることを固定する。
/// <para>
/// キーは <c>DenDay + NouhinDay + Id_Soko + Id_Tenpo + Kubun + RelateNo1</c> の6列である。
/// このうち1列でも括りから落とすと別々の出荷が1伝票に混ざるため、列ごとに分かれることを検証する。
/// </para>
/// </summary>
[TestClass]
public class ShippingDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"ShippingDbTests-{System.Guid.NewGuid():N}";
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
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	/// <summary>同一の仮想ヘッダキーなら1伝票へまとまり、明細が積まれることを確認する</summary>
	[TestMethod]
	public void CreateShippingSlips_SameHeaderKey_CreatesSingleSlip() {
		CreateTokui(id: 5, tenType: 1);
		var a = InsertConfirmed(idShohin: 10);
		var b = InsertConfirmed(idShohin: 20);

		var created = new ShippingDb(Db).CreateShippingSlips([a, b], "20260901", idShain: 9);

		Assert.AreEqual(1, created.Count, "キーが同じ2行は1伝票になる");
		var slip = Db.Fetch<Tran00Uriage>("where Id=@0", created[0]).Single();
		Assert.AreEqual(2, slip.Jmeisai?.Count, "明細が2行積まれる");
		Assert.AreEqual(6, slip.SuTotal, "実数量の合計がヘッダへ入る");
		Assert.IsTrue(Db.Fetch<TranHaibun>("where EndFlag = 0").Count == 0, "全行が完了(引当解除)になる");
	}

	/// <summary>キーの6列それぞれで伝票が分かれることを確認する（1列でも落とすと混ざる）</summary>
	[TestMethod]
	public void CreateShippingSlips_DifferentHeaderKeyColumn_SplitsSlips() {
		CreateTokui(id: 5, tenType: 1);
		CreateTokui(id: 6, tenType: 1);

		var baseline = InsertConfirmed(idShohin: 10);
		long[] variants = [
			InsertConfirmed(idShohin: 11, denDay: "20260812"),                    // DenDay 違い
			InsertConfirmed(idShohin: 12, nouhinDay: "20260825"),                 // NouhinDay 違い
			InsertConfirmed(idShohin: 13, idSoko: 2),                             // Id_Soko 違い
			InsertConfirmed(idShohin: 14, idTenpo: 6),                            // Id_Tenpo 違い
			InsertConfirmed(idShohin: 15, kubun: EnumHaibun.Juchu),               // Kubun 違い
			InsertConfirmed(idShohin: 16, relateNo1: 777),                        // RelateNo1 違い
		];

		var created = new ShippingDb(Db).CreateShippingSlips([baseline, .. variants], "20260901", idShain: 9);

		Assert.AreEqual(1 + variants.Length, created.Count, "キー列が1つ違えば別伝票になる");
		Assert.IsTrue(created.All(id => Db.Fetch<Tran00Uriage>("where Id=@0", id).Single().Jmeisai?.Count == 1),
			"どの伝票も明細1行だけになる");
	}

	/// <summary>出荷先の店種区分で伝票種別が分かれる（卸先=出荷売上 / 倉庫=移動出庫）</summary>
	[TestMethod]
	public void CreateShippingSlips_TenType_SwitchesSlipType() {
		CreateTokui(id: 5, tenType: 1);
		CreateTokui(id: 6, tenType: 0);
		var oroshi = InsertConfirmed(idShohin: 10, idTenpo: 5);
		var soko = InsertConfirmed(idShohin: 20, idTenpo: 6);

		new ShippingDb(Db).CreateShippingSlips([oroshi, soko], "20260901", idShain: 9);

		Assert.AreEqual(1, Db.Fetch<Tran00Uriage>("").Count, "卸先は出荷売上になる");
		Assert.AreEqual(1, Db.Fetch<Tran10IdoOut>("").Count, "倉庫は移動出庫になる");
	}

	/// <summary>全量欠品の行は伝票を作らず、完了だけ立てて引当から外す</summary>
	[TestMethod]
	public void CreateShippingSlips_AllShortage_CreatesNoSlipButReleasesReserve() {
		CreateTokui(id: 5, tenType: 1);
		var id = InsertConfirmed(idShohin: 10, su: 3, jitsuSu: 0, shortSu: 3);

		var created = new ShippingDb(Db).CreateShippingSlips([id], "20260901", idShain: 9);

		Assert.AreEqual(0, created.Count, "出荷数0なら伝票を作らない");
		var row = Db.Fetch<TranHaibun>("where Id=@0", id).Single();
		Assert.AreEqual(1, row.EndFlag, "完了は立てて引当から外す");
		Assert.AreEqual(0, row.RelateNo2, "伝票が無いので関連No2は0のまま");
	}

	/// <summary>キー列の定義とSQL展開が食い違わないことを確認する</summary>
	[TestMethod]
	public void HaibunHeaderKey_KeyColumns_MatchesSqlAndRow() {
		CollectionAssert.AreEqual(
			new[] { "DenDay", "NouhinDay", "Id_Soko", "Id_Tenpo", "Kubun", "RelateNo1" },
			HaibunHeaderKey.KeyColumns,
			"仮想ヘッダは決定 I5 の6列で括る");
		Assert.AreEqual("h.DenDay, h.NouhinDay, h.Id_Soko, h.Id_Tenpo, h.Kubun, h.RelateNo1",
			HaibunHeaderKey.KeyColumnsSql("h"), "別名付きで展開できる");
		Assert.AreEqual("DenDay, NouhinDay, Id_Soko, Id_Tenpo, Kubun, RelateNo1",
			HaibunHeaderKey.KeyColumnsSql(), "別名なしでも展開できる");

		var row = new TranHaibun {
			DenDay = "20260815", NouhinDay = "20260820", Id_Soko = 1, Id_Tenpo = 5,
			Kubun = (int)EnumHaibun.Juchu, RelateNo1 = 42,
		};
		Assert.AreEqual(new HaibunHeaderKey("20260815", "20260820", 1, 5, (int)EnumHaibun.Juchu, 42),
			HaibunHeaderKey.From(row), "配分行からキーを作れる");
	}

	// ===== 準備 =====

	private void PrepareTables() {
		Db.CreateTable(typeof(MasterSysman), true, false);
		Db.Insert(new MasterSysman { ShimeBi = 99 });
		Db.CreateTable(typeof(TranHaibun), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(Tran10IdoOut), true, false);
		Db.CreateTable(typeof(MasterTokui), true, false);
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryRealStock), true, false);
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
	}

	private void CreateTokui(long id, int tenType) {
		var tokui = new MasterTokui { Code = $"T{id}", Name = $"得意先{id}", TenType = tenType };
		Db.Insert(tokui);
		// Idは採番されるので、テストが使う固定Idへ寄せる
		Db.Execute("update MasterTokui set Id=@0 where Id=@1", id, tokui.Id);
	}

	/// <summary>確定済み(KakuteiDay有効)・未完了の配分行を1件作る。既定は指示3・実数3</summary>
	private long InsertConfirmed(long idShohin, string denDay = "20260815", string nouhinDay = "20260820",
		long idSoko = 1, long idTenpo = 5, EnumHaibun kubun = EnumHaibun.Zaiko, int relateNo1 = 0,
		int su = 3, int jitsuSu = 3, int shortSu = 0) {
		var row = new TranHaibun {
			DenDay = denDay,
			NouhinDay = nouhinDay,
			Id_Soko = idSoko,
			Id_Tenpo = idTenpo,
			Kubun = (int)kubun,
			RelateNo1 = relateNo1,
			Id_Shohin = idShohin,
			Id_Col = 100,
			Id_Siz = 1000,
			Su = su,
			Tanka = 1000,
			Kingaku = su * 1000,
			JitsuSu = jitsuSu,
			ShortSu = shortSu,
			KakuteiDay = "20260830",
		};
		Db.Insert(row);
		return row.Id;
	}
}
