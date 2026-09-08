using System;
using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 上代一括変更 Step2 の回帰テスト。
/// <para>
/// 最優先要件は「既存伝票が現行と1行も違わずに展開されること」。<see cref="DerivedJodai.CreateSql"/>への
/// <c>No_Scope</c>等値結合追加、<see cref="TranJodai.Normalize"/>/<see cref="TranJodai.FindDuplicates"/>の
/// 複合キー化、<see cref="TranJodai.NormalizeLegacyScope"/>の後方互換を検証する。
/// 仕様は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 第2章・第4章・3.4・3.10。
/// </para>
/// </summary>
[TestClass]
public class JodaiExpandTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"JodaiExpandTests-{Guid.NewGuid():N}";
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
		// DerivedJodai はインデックスも張る。段階値下げ(S2)が uk1(DayFrom込み) に
		// 触れないことを本当に検証するには、一意インデックスが存在している必要がある。
		Db.CreateTable(typeof(DerivedJodai), true, true);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	// ============================================================
	// 展開SQL（DerivedJodai.CreateSql）の回帰テスト
	// ============================================================

	/// <summary>
	/// R1（最重要）: 既存形式。Jscope空、Jshop/JmeisaiのJSONに No_Scope キー自体が無い伝票は、
	/// 店舗×商品の全直積に展開される（現行と同一）。
	/// <para>
	/// NPoco経由でC#オブジェクトを保存すると必ず No_Scope:0 が出力されてしまうため、
	/// 「キー自体が無い」状態を再現するには生JSON文字列を直接 UPDATE で流し込むしかない。
	/// これが本テストの核心（回帰テストの意味そのもの）。
	/// </para>
	/// </summary>
	[TestMethod]
	public void CreateSql_R1_既存形式No_Scopeキー無しは店舗商品の全直積に展開される() {
		var tranId = InsertTran(status: 1, dayFrom: "20260901", dayTo: "20260930");
		SetRawJson(tranId,
			jshop: "[" +
				"{\"Id_Tenpo\":101,\"Code_Tenpo\":\"S1\",\"Mei_Tenpo\":\"店舗1\",\"DayFrom\":\"20260910\",\"DayTo\":\"20260920\"}," +
				"{\"Id_Tenpo\":102,\"Code_Tenpo\":\"S2\",\"Mei_Tenpo\":\"店舗2\",\"DayFrom\":\"20260910\",\"DayTo\":\"20260920\"}" +
				"]",
			jmeisai: "[" +
				"{\"No\":1,\"Id_Shohin\":201,\"Code_Shohin\":\"P1\",\"Mei_Shohin\":\"商品1\",\"JodaiNew\":900,\"RateOff\":10}," +
				"{\"No\":2,\"Id_Shohin\":202,\"Code_Shohin\":\"P2\",\"Mei_Shohin\":\"商品2\",\"JodaiNew\":800,\"RateOff\":20}" +
				"]");
		AssertJsonHasNoNoScopeKey(tranId);

		Db.Execute(DerivedJodai.CreateSql);

		var rows = Db.Fetch<DerivedJodai>("where Id_Tran=@0 order by Id_Tenpo, Id_Shohin", tranId);
		Assert.AreEqual(4, rows.Count, "2店舗×2商品の全直積");
		AssertHasCell(rows, 101, 201, 900);
		AssertHasCell(rows, 101, 202, 800);
		AssertHasCell(rows, 102, 201, 900);
		AssertHasCell(rows, 102, 202, 800);
		Assert.IsTrue(rows.All(r => r.DayFrom == "20260910" && r.DayTo == "20260920"), "店舗個別期間が優先される（現行動作）");
	}

	/// <summary>
	/// R2: 既存形式で Jshop.DayFrom/DayTo が空文字のとき、ヘッダの T.DayFrom/DayTo へフォールバックする（現行動作）。
	/// </summary>
	[TestMethod]
	public void CreateSql_R2_店舗別期間が空文字ならヘッダ既定期間へフォールバックする() {
		var tranId = InsertTran(status: 1, dayFrom: "20260901", dayTo: "20260930");
		SetRawJson(tranId,
			jshop: "[{\"Id_Tenpo\":101,\"Code_Tenpo\":\"S1\",\"Mei_Tenpo\":\"店舗1\",\"DayFrom\":\"\",\"DayTo\":\"\"}]",
			jmeisai: "[{\"No\":1,\"Id_Shohin\":201,\"Code_Shohin\":\"P1\",\"Mei_Shohin\":\"商品1\",\"JodaiNew\":900,\"RateOff\":10}]");

		Db.Execute(DerivedJodai.CreateSql);

		var row = Db.Fetch<DerivedJodai>("where Id_Tran=@0", tranId).Single();
		Assert.AreEqual("20260901", row.DayFrom);
		Assert.AreEqual("20260930", row.DayTo);
	}

	/// <summary>
	/// R3: No_Scope が両側とも 0（新規保存されたScope未使用伝票）は R1 と同じ直積になる。
	/// C#オブジェクト経由（NPoco既定シリアライズ）でも成立することを確認する。
	/// </summary>
	[TestMethod]
	public void CreateSql_R3_No_Scopeが両側とも0ならR1と同じ全直積になる() {
		var tran = new TranJodai {
			Status = 1,
			TaishoType = (int)EnumJodaiTaisho.Tenpo,
			DayFrom = "20260901",
			DayTo = "20260930",
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, DayFrom = "20260910", DayTo = "20260920", No_Scope = 0 },
				new TranJodaiShop { Id_Tenpo = 102, DayFrom = "20260910", DayTo = "20260920", No_Scope = 0 },
			],
			Jmeisai = [
				new TranJodaiMeisai { No = 1, Id_Shohin = 201, JodaiNew = 900, RateOff = 10, No_Scope = 0 },
				new TranJodaiMeisai { No = 2, Id_Shohin = 202, JodaiNew = 800, RateOff = 20, No_Scope = 0 },
			],
		};
		Db.Insert(tran);

		Db.Execute(DerivedJodai.CreateSql);

		var rows = Db.Fetch<DerivedJodai>("where Id_Tran=@0 order by Id_Tenpo, Id_Shohin", tran.Id);
		Assert.AreEqual(4, rows.Count, "2店舗×2商品の全直積");
		AssertHasCell(rows, 101, 201, 900);
		AssertHasCell(rows, 101, 202, 800);
		AssertHasCell(rows, 102, 201, 900);
		AssertHasCell(rows, 102, 202, 800);
	}

	/// <summary>
	/// S1: Scopeが2本（#1に店舗A・商品X、#2に店舗B・商品Y）のとき、交差しない。
	/// A×XとB×Yの2行のみで、A×YやB×Xは出ない。
	/// </summary>
	[TestMethod]
	public void CreateSql_S1_異なるScope同士は交差しない() {
		var tran = new TranJodai {
			Status = 1,
			DayFrom = "20260901",
			DayTo = "20260930",
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, DayFrom = "20260910", DayTo = "20260920", No_Scope = 1 },
				new TranJodaiShop { Id_Tenpo = 102, DayFrom = "20260910", DayTo = "20260920", No_Scope = 2 },
			],
			Jmeisai = [
				new TranJodaiMeisai { No = 1, Id_Shohin = 201, JodaiNew = 900, No_Scope = 1 },
				new TranJodaiMeisai { No = 1, Id_Shohin = 202, JodaiNew = 800, No_Scope = 2 },
			],
		};
		Db.Insert(tran);

		Db.Execute(DerivedJodai.CreateSql);

		var rows = Db.Fetch<DerivedJodai>("where Id_Tran=@0 order by Id_Tenpo, Id_Shohin", tran.Id);
		Assert.AreEqual(2, rows.Count, "Scope#1の店舗A×商品XとScope#2の店舗B×商品Yのみ");
		AssertHasCell(rows, 101, 201, 900);
		AssertHasCell(rows, 102, 202, 800);
		Assert.IsFalse(rows.Any(r => r.Id_Tenpo == 101 && r.Id_Shohin == 202), "A×Yは出ない");
		Assert.IsFalse(rows.Any(r => r.Id_Tenpo == 102 && r.Id_Shohin == 201), "B×Xは出ない");
	}

	/// <summary>
	/// S2: 段階値下げ。同一店舗×同一商品で No_Scope=1(9/10-9/20) と 2(9/21-9/30) のとき、
	/// 2行展開され、uk1（DayFrom込み）違反にならない。
	/// </summary>
	[TestMethod]
	public void CreateSql_S2_段階値下げは同一店舗商品で2行に展開される() {
		var tran = new TranJodai {
			Status = 1,
			DayFrom = "20260901",
			DayTo = "20260930",
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, DayFrom = "20260910", DayTo = "20260920", No_Scope = 1 },
				new TranJodaiShop { Id_Tenpo = 101, DayFrom = "20260921", DayTo = "20260930", No_Scope = 2 },
			],
			Jmeisai = [
				new TranJodaiMeisai { No = 1, Id_Shohin = 201, JodaiNew = 900, No_Scope = 1 },
				new TranJodaiMeisai { No = 1, Id_Shohin = 201, JodaiNew = 800, No_Scope = 2 },
			],
		};
		Db.Insert(tran);

		Db.Execute(DerivedJodai.CreateSql);

		var rows = Db.Fetch<DerivedJodai>("where Id_Tran=@0 order by DayFrom", tran.Id);
		Assert.AreEqual(2, rows.Count);
		Assert.AreEqual("20260910", rows[0].DayFrom);
		Assert.AreEqual("20260920", rows[0].DayTo);
		Assert.AreEqual(900, rows[0].Jodai);
		Assert.AreEqual("20260921", rows[1].DayFrom);
		Assert.AreEqual("20260930", rows[1].DayTo);
		Assert.AreEqual(800, rows[1].Jodai);
	}

	/// <summary>
	/// S2 の担保。uk1 が実際に効いていることを確認する。
	/// <para>
	/// S2 は「段階値下げが uk1 に触れない」ことを主張するテストなので、そもそも一意インデックスが
	/// 張られていなければ何も検証していないことになる。DayFrom まで一致する行を2本入れて
	/// 実際に制約違反になることを示し、S2 の前提を守る。
	/// </para>
	/// </summary>
	[TestMethod]
	public void DerivedJodai_uk1が有効で同一期間の重複行は拒否される() {
		var row = new DerivedJodai {
			Id_Tran = 1, TaishoType = 0, Id_Tenpo = 101, Id_Shohin = 201,
			DayFrom = "20260910", DayTo = "20260920", Jodai = 900,
		};
		Db.Insert(row);

		var duplicate = new DerivedJodai {
			Id_Tran = 1, TaishoType = 0, Id_Tenpo = 101, Id_Shohin = 201,
			DayFrom = "20260910", DayTo = "20260930", Jodai = 800,
		};
		Assert.Throws<SqliteException>(() => Db.Insert(duplicate), "uk1(DayFrom込み)が効いていること");

		// DayFrom が違えば通る（＝段階値下げが成立する）
		var nextStage = new DerivedJodai {
			Id_Tran = 1, TaishoType = 0, Id_Tenpo = 101, Id_Shohin = 201,
			DayFrom = "20260921", DayTo = "20260930", Jodai = 800,
		};
		Db.Insert(nextStage);
		Assert.AreEqual(2, Db.Fetch<DerivedJodai>("where Id_Tran=1").Count);
	}

	/// <summary>
	/// S3: Status=0(入力中)/2(取消)の伝票は展開されない（現行どおり）。
	/// </summary>
	[TestMethod]
	[DataRow(0)]
	[DataRow(2)]
	public void CreateSql_S3_Status0または2の伝票は展開されない(int status) {
		var tran = new TranJodai {
			Status = status,
			DayFrom = "20260901",
			DayTo = "20260930",
			Jshop = [new TranJodaiShop { Id_Tenpo = 101, DayFrom = "20260910", DayTo = "20260920" }],
			Jmeisai = [new TranJodaiMeisai { No = 1, Id_Shohin = 201, JodaiNew = 900 }],
		};
		Db.Insert(tran);

		Db.Execute(DerivedJodai.CreateSql);

		Assert.AreEqual(0, Db.Fetch<DerivedJodai>("where Id_Tran=@0", tran.Id).Count);
	}

	// ============================================================
	// TranJodai.Normalize() / FindDuplicates()
	// ============================================================

	/// <summary>
	/// No_Scopeが全て0（既存伝票）なら、重複除去も採番も現行と完全に同一の結果になる。
	/// </summary>
	[TestMethod]
	public void Normalize_No_Scopeが全て0なら現行と同一の重複除去と採番になる() {
		var tran = new TranJodai {
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101 },
				new TranJodaiShop { Id_Tenpo = 102 },
				new TranJodaiShop { Id_Tenpo = 101 }, // 重複（後の指定を残す）
			],
			Jmeisai = [
				new TranJodaiMeisai { Id_Shohin = 201 },
				new TranJodaiMeisai { Id_Shohin = 202 },
				new TranJodaiMeisai { Id_Shohin = 201 }, // 重複（後の指定を残す）
			],
		};

		var removed = tran.Normalize();

		Assert.AreEqual(2, removed, "店舗1件+商品1件の重複を除去");
		Assert.AreEqual(2, tran.Jshop.Count);
		Assert.AreEqual(102, tran.Jshop[0].Id_Tenpo, "先に入力した重複行は削除される");
		Assert.AreEqual(101, tran.Jshop[1].Id_Tenpo, "後の指定（3件目）が残る");
		Assert.AreEqual(2, tran.Jmeisai.Count);
		Assert.AreEqual(202, tran.Jmeisai[0].Id_Shohin);
		Assert.AreEqual(201, tran.Jmeisai[1].Id_Shohin);
		Assert.AreSequenceEqual(new[] { 1, 2 }, tran.Jmeisai.Select(m => m.No).ToArray(), "0グループ内の1..nに採番（現行と同一）");
		Assert.AreEqual(2, tran.ShopCnt);
		Assert.AreEqual(2, tran.MeisaiCnt);
		Assert.AreEqual(0, tran.ScopeCnt);
	}

	/// <summary>
	/// No_Scopeが異なれば同一店舗・同一商品でも重複扱いにしない（段階値下げを壊さない）。
	/// </summary>
	[TestMethod]
	public void Normalize_No_Scopeが異なれば同一店舗商品でも重複扱いにしない() {
		var tran = new TranJodai {
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, No_Scope = 1 },
				new TranJodaiShop { Id_Tenpo = 101, No_Scope = 2 },
			],
			Jmeisai = [
				new TranJodaiMeisai { Id_Shohin = 201, No_Scope = 1 },
				new TranJodaiMeisai { Id_Shohin = 201, No_Scope = 2 },
			],
		};

		var removed = tran.Normalize();

		Assert.AreEqual(0, removed, "Scopeが異なるので重複ではない");
		Assert.AreEqual(2, tran.Jshop.Count);
		Assert.AreEqual(2, tran.Jmeisai.Count);
	}

	/// <summary>
	/// Jmeisai.No は Scope 内での連番へ採番し直し、Jmeisai の並び順自体は変えない。
	/// </summary>
	[TestMethod]
	public void Normalize_JmeisaiのNoはScope内の連番になり並び順は変えない() {
		var tran = new TranJodai {
			Jmeisai = [
				new TranJodaiMeisai { Id_Shohin = 201, No_Scope = 2 }, // Scope2の1件目（並び順は先頭のまま）
				new TranJodaiMeisai { Id_Shohin = 301, No_Scope = 1 }, // Scope1の1件目
				new TranJodaiMeisai { Id_Shohin = 302, No_Scope = 1 }, // Scope1の2件目
				new TranJodaiMeisai { Id_Shohin = 202, No_Scope = 2 }, // Scope2の2件目
			],
		};

		tran.Normalize();

		Assert.AreSequenceEqual(new long[] { 201, 301, 302, 202 }, tran.Jmeisai.Select(m => m.Id_Shohin).ToArray(), "並び順は不変");
		Assert.AreSequenceEqual(new[] { 1, 1, 2, 2 }, tran.Jmeisai.Select(m => m.No).ToArray(), "ScopeごとにNo=1から採番");
	}

	/// <summary>
	/// No_Scope=0（Scope未使用）のときのFindDuplicatesメッセージは、Scope概念導入前と文言が変わらない。
	/// </summary>
	[TestMethod]
	public void FindDuplicates_No_Scopeが0のときは現行と同じ文言のメッセージになる() {
		var tran = new TranJodai {
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, Code_Tenpo = "S1", Mei_Tenpo = "店舗1" },
				new TranJodaiShop { Id_Tenpo = 101, Code_Tenpo = "S1", Mei_Tenpo = "店舗1" },
			],
			Jmeisai = [
				new TranJodaiMeisai { Id_Shohin = 201, Code_Shohin = "P1", Mei_Shohin = "商品1" },
				new TranJodaiMeisai { Id_Shohin = 201, Code_Shohin = "P1", Mei_Shohin = "商品1" },
			],
		};

		var messages = tran.FindDuplicates();

		Assert.AreEqual(2, messages.Count);
		Assert.AreEqual("対象店舗が重複しています：S1 店舗1（2件）", messages[0]);
		Assert.AreEqual("対象商品が重複しています：P1 商品1（2件）", messages[1]);
	}

	/// <summary>
	/// No_Scopeが0以外のときは、メッセージ末尾にScope情報（NoとJscopeから引けるName）を付す。
	/// </summary>
	[TestMethod]
	public void FindDuplicates_No_Scopeが0以外ならScope情報を付す() {
		var tran = new TranJodai {
			Jscope = [new TranJodaiScope { No = 1, Name = "全国" }],
			Jshop = [
				new TranJodaiShop { Id_Tenpo = 101, Code_Tenpo = "S1", Mei_Tenpo = "店舗1", No_Scope = 1 },
				new TranJodaiShop { Id_Tenpo = 101, Code_Tenpo = "S1", Mei_Tenpo = "店舗1", No_Scope = 1 },
			],
		};

		var messages = tran.FindDuplicates();

		Assert.AreEqual(1, messages.Count);
		StringAssert.Contains(messages[0], "Scope1");
		StringAssert.Contains(messages[0], "全国");
	}

	// ============================================================
	// TranJodai.NormalizeLegacyScope()（設計 3.10）
	// ============================================================

	/// <summary>
	/// Jscopeが空でJshop/Jmeisaiがある既存伝票は、No=1の全店Scopeを補いNo_Scopeを1に振り直す。
	/// CalcType=0(金額指定)はPriceMethod=0(固定額)・FixedPrice=CalcValueへ写す。
	/// </summary>
	[TestMethod]
	public void NormalizeLegacyScope_金額指定の既存伝票に全店Scopeを補う() {
		var tran = new TranJodai {
			DayFrom = "20260901",
			DayTo = "20260930",
			CalcType = 0,
			CalcValue = 7900,
			CalcRate = 0,
			RoundUnit = 1,
			RoundType = 2,
			Jshop = [new TranJodaiShop { Id_Tenpo = 101 }],
			Jmeisai = [new TranJodaiMeisai { Id_Shohin = 201 }],
		};

		tran.NormalizeLegacyScope();

		Assert.AreEqual(1, tran.Jscope.Count);
		var scope = tran.Jscope[0];
		Assert.AreEqual(1, scope.No);
		Assert.AreNotEqual(string.Empty, scope.Name, "空だとPrice Matrixの列見出しが空になるため既定名を入れる");
		Assert.AreEqual((int)EnumJodaiRangeType.All, scope.RangeType);
		Assert.AreEqual((int)EnumJodaiIncExc.Include, scope.IncExc);
		Assert.AreEqual("20260901", scope.DayFrom);
		Assert.AreEqual("20260930", scope.DayTo);
		Assert.AreEqual((int)EnumJodaiPriceMethod.FixedPrice, scope.PriceMethod);
		Assert.AreEqual(7900, scope.FixedPrice);
		Assert.AreEqual(1, scope.RoundUnit);
		Assert.AreEqual(2, scope.RoundType);
		Assert.AreEqual(1, tran.Jshop[0].No_Scope);
		Assert.AreEqual(1, tran.Jmeisai[0].No_Scope);
	}

	/// <summary>
	/// CalcType=1(率指定)はPriceMethod=1(値下率)・RateOff=CalcRateへ写す。
	/// </summary>
	[TestMethod]
	public void NormalizeLegacyScope_率指定の既存伝票はPriceMethod1へ写す() {
		var tran = new TranJodai {
			CalcType = 1,
			CalcRate = 30,
			Jshop = [new TranJodaiShop { Id_Tenpo = 101 }],
			Jmeisai = [new TranJodaiMeisai { Id_Shohin = 201 }],
		};

		tran.NormalizeLegacyScope();

		var scope = tran.Jscope.Single();
		Assert.AreEqual((int)EnumJodaiPriceMethod.RateOff, scope.PriceMethod);
		Assert.AreEqual(30, scope.RateOff);
	}

	/// <summary>
	/// Jscopeが既にある伝票は何もしない（Scope対応済みの伝票を壊さない）。
	/// </summary>
	[TestMethod]
	public void NormalizeLegacyScope_Jscopeが既にあれば何もしない() {
		var tran = new TranJodai {
			Jscope = [new TranJodaiScope { No = 1, Name = "既存Scope" }],
			Jshop = [new TranJodaiShop { Id_Tenpo = 101, No_Scope = 0 }],
			Jmeisai = [new TranJodaiMeisai { Id_Shohin = 201, No_Scope = 0 }],
		};

		tran.NormalizeLegacyScope();

		Assert.AreEqual(1, tran.Jscope.Count);
		Assert.AreEqual("既存Scope", tran.Jscope[0].Name);
		Assert.AreEqual(0, tran.Jshop[0].No_Scope, "既にScope対応済みの伝票の値を書き換えない");
		Assert.AreEqual(0, tran.Jmeisai[0].No_Scope);
	}

	/// <summary>
	/// Jshop/Jmeisaiが両方とも空なら（新規未入力の伝票）何もしない。
	/// </summary>
	[TestMethod]
	public void NormalizeLegacyScope_JshopとJmeisaiが両方空なら何もしない() {
		var tran = new TranJodai();

		tran.NormalizeLegacyScope();

		Assert.AreEqual(0, tran.Jscope.Count);
	}

	// ============================================================
	// ヘルパー
	// ============================================================

	private long InsertTran(int status, string dayFrom = "20260901", string dayTo = "20260930") {
		var row = new TranJodai {
			Status = status,
			DayFrom = dayFrom,
			DayTo = dayTo,
			TaishoType = (int)EnumJodaiTaisho.Tenpo,
		};
		Db.Insert(row);
		return row.Id;
	}

	/// <summary>
	/// 生JSON文字列を直接UPDATEで流し込む。C#オブジェクト経由（NPoco）では必ず No_Scope:0 が
	/// 出力されてしまうため、「No_Scopeキー自体が無い」既存形式の再現にはこの方法しかない。
	/// </summary>
	private void SetRawJson(long tranId, string jshop, string jmeisai) =>
		Db.Execute("UPDATE TranJodai SET Jshop=@0, Jmeisai=@1 WHERE Id=@2", jshop, jmeisai, tranId);

	private void AssertJsonHasNoNoScopeKey(long tranId) {
		var jshop = Db.ExecuteScalar<string>("SELECT Jshop FROM TranJodai WHERE Id=@0", tranId);
		var jmeisai = Db.ExecuteScalar<string>("SELECT Jmeisai FROM TranJodai WHERE Id=@0", tranId);
		StringAssert.DoesNotMatch(jshop, new System.Text.RegularExpressions.Regex("No_Scope"));
		StringAssert.DoesNotMatch(jmeisai, new System.Text.RegularExpressions.Regex("No_Scope"));
	}

	private static void AssertHasCell(List<DerivedJodai> rows, long idTenpo, long idShohin, int jodai) {
		var row = rows.SingleOrDefault(r => r.Id_Tenpo == idTenpo && r.Id_Shohin == idShohin);
		Assert.IsNotNull(row, $"Id_Tenpo={idTenpo}, Id_Shohin={idShohin} の行が見つからない");
		Assert.AreEqual(jodai, row!.Jodai);
	}
}
