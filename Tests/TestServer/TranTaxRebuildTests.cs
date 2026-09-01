using System;
using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 明細別消費税の計算（軽減税率の混在・非課税・税率切替日・返品符号）。
/// 仕様は `Doc/spec/2026-08-25_明細別消費税計算_詳細設計.md` の 7章。
/// </summary>
[TestClass]
public class TranTaxRebuildTests {

	/// <summary>開発DB(server-user163.db)と同じ税率定義。Id=1 標準/Id=2 軽減/Id=3 未使用</summary>
	static MasterSysman CreateSysman() => new() {
		Id = 1,
		Jsub = [
			new MasterSysTax { Id = 1, TaxRate = 8, DateFrom = "20191001", TaxNewRate = 10 },
			new MasterSysTax { Id = 2, TaxRate = 8, DateFrom = "20191001", TaxNewRate = 8 },
			new MasterSysTax { Id = 3, TaxRate = 15, DateFrom = "19010101", TaxNewRate = 0 },
		],
	};

	/// <summary>商品Id → 消費税区分。10=標準/20=軽減/30=非課税</summary>
	static Dictionary<long, long> CreateTaxMap() => new() {
		[10] = 1,
		[20] = 2,
		[30] = 0,
	};

	static Tran99Meisai Line(int no, long idShohin, int kingaku) =>
		new() { No = no, Id_Shohin = idShohin, Kingaku = kingaku };

	[TestMethod]
	public void ApplyMeisaiTax_軽減税率と標準税率が混在しても明細ごとに税率が分かれる() {
		var meisai = new List<Tran99Meisai> {
			Line(1, 20, 10000),  // 軽減 8% → 800
			Line(2, 20, 1500),   // 軽減 8% → 120
			Line(3, 10, 6000),   // 標準 10% → 600
		};

		var headerTax = TranTaxRebuildDb.ApplyMeisaiTax(meisai, CreateSysman(), CreateTaxMap(), "20260825");

		Assert.AreEqual(2L, meisai[0].Id_Tax);
		Assert.AreEqual(8, meisai[0].TaxRate);
		Assert.AreEqual(800, meisai[0].Tax);
		Assert.AreEqual(120, meisai[1].Tax);
		Assert.AreEqual(1L, meisai[2].Id_Tax);
		Assert.AreEqual(10, meisai[2].TaxRate);
		Assert.AreEqual(600, meisai[2].Tax);
		// ヘッダは税区分ごとの明細税額合計。全件10%一括(1750)とは一致しない
		Assert.AreEqual(600L, headerTax.Tax1);
		Assert.AreEqual(920L, headerTax.Tax2);
		Assert.AreEqual(0L, headerTax.Tax3);
		Assert.AreEqual(1520L, headerTax.Tax1 + headerTax.Tax2 + headerTax.Tax3);
		Assert.AreEqual(meisai.Sum(m => (long)m.Tax), headerTax.Tax1 + headerTax.Tax2 + headerTax.Tax3);
	}

	[TestMethod]
	public void ApplyMeisaiTax_非課税の明細は税率も税額も0になる() {
		var meisai = new List<Tran99Meisai> { Line(1, 30, 5000), Line(2, 10, 5000) };

		var headerTax = TranTaxRebuildDb.ApplyMeisaiTax(meisai, CreateSysman(), CreateTaxMap(), "20260825");

		Assert.AreEqual(0L, meisai[0].Id_Tax);
		Assert.AreEqual(0, meisai[0].TaxRate);
		Assert.AreEqual(0, meisai[0].Tax);
		// 非課税でない行はそのまま課税される
		Assert.AreEqual(500, meisai[1].Tax);
		Assert.AreEqual(500, headerTax.Tax1 + headerTax.Tax2 + headerTax.Tax3);
	}

	[TestMethod]
	public void ApplyMeisaiTax_商品マスタが引けない明細は標準税率を既定にする() {
		var meisai = new List<Tran99Meisai> {
			Line(1, 0, 2000),    // Id_Shohin 未設定
			Line(2, 999, 2000),  // マップに無い商品
		};

		TranTaxRebuildDb.ApplyMeisaiTax(meisai, CreateSysman(), CreateTaxMap(), "20260825");

		Assert.AreEqual(1L, meisai[0].Id_Tax);
		Assert.AreEqual(200, meisai[0].Tax);
		Assert.AreEqual(1L, meisai[1].Id_Tax);
		Assert.AreEqual(200, meisai[1].Tax);
	}

	[TestMethod]
	public void ApplyMeisaiTax_税率切替日の前後で適用税率が変わる() {
		var sysman = CreateSysman();
		var map = CreateTaxMap();

		var before = new List<Tran99Meisai> { Line(1, 10, 10000) };
		TranTaxRebuildDb.ApplyMeisaiTax(before, sysman, map, "20190930");
		Assert.AreEqual(8, before[0].TaxRate);
		Assert.AreEqual(800, before[0].Tax);

		var after = new List<Tran99Meisai> { Line(1, 10, 10000) };
		TranTaxRebuildDb.ApplyMeisaiTax(after, sysman, map, "20191001");
		Assert.AreEqual(10, after[0].TaxRate);
		Assert.AreEqual(1000, after[0].Tax);

		// 軽減税率は切替後も8%のまま
		var reduced = new List<Tran99Meisai> { Line(1, 20, 10000) };
		TranTaxRebuildDb.ApplyMeisaiTax(reduced, sysman, map, "20260825");
		Assert.AreEqual(8, reduced[0].TaxRate);
	}

	[TestMethod]
	public void ApplyMeisaiTax_金額が負でも明細税額は正値になる() {
		// 返品の符号はヘッダ Kubun の CalcFlag が集計側で担うため、明細では持たない
		var meisai = new List<Tran99Meisai> { Line(1, 20, -5000), Line(2, 10, -5000) };

		var headerTax = TranTaxRebuildDb.ApplyMeisaiTax(meisai, CreateSysman(), CreateTaxMap(), "20260825");

		Assert.AreEqual(400, meisai[0].Tax);
		Assert.AreEqual(500, meisai[1].Tax);
		Assert.AreEqual(900, headerTax.Tax1+headerTax.Tax2+headerTax.Tax3);
	}

	[TestMethod]
	public void ApplyMeisaiTax_同じ入力を2回適用しても結果が変わらない() {
		var sysman = CreateSysman();
		var map = CreateTaxMap();
		var meisai = new List<Tran99Meisai> { Line(1, 20, 10000), Line(2, 10, 6000) };

		var first = TranTaxRebuildDb.ApplyMeisaiTax(meisai, sysman, map, "20260825");
		var second = TranTaxRebuildDb.ApplyMeisaiTax(meisai, sysman, map, "20260825");

		Assert.AreEqual(first, second);
		Assert.AreEqual(800, meisai[0].Tax);
		Assert.AreEqual(600, meisai[1].Tax);
	}

	[TestMethod]
	public void ResolveTaxRatePercent_税区分0は非課税として0を返す() {
		// LogicGetTax(0,...) は MasterSysTax を引けず例外になるため、0 はここで確定させる
		Assert.AreEqual(0, TaxRateResolver.ResolveTaxRatePercent(CreateSysman(), 0, "20260825"));
		Assert.AreEqual(0, TaxRateResolver.ResolveTaxRatePercent(CreateSysman(), -1, "20260825"));
	}

	[TestMethod]
	public void ResolveTaxRatePercent_日付が不正なら切替前の税率を使う() {
		var sysman = CreateSysman();
		// 8桁でない日付は CvAsset.Common.CompareYmd が例外を投げるため、渡す前に弾いている
		Assert.AreEqual(8, TaxRateResolver.ResolveTaxRatePercent(sysman, 1, ""));
		Assert.AreEqual(8, TaxRateResolver.ResolveTaxRatePercent(sysman, 1, "2026"));
	}
}

/// <summary>
/// <see cref="TranTaxRebuildDb.RebuildAll"/>（明細別消費税へ移行するための一括再計算）の検証。
/// 仕様は `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` の 3.3/3.4/3.6 と 7章。
/// </summary>
[TestClass]
public class TranTaxRebuildDbTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"TranTaxRebuildDbTests-{Guid.NewGuid():N}";
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
		foreach (var t in new[] {
			typeof(MasterSysman), typeof(MasterTokui), typeof(MasterShiire), typeof(MasterShohin),
			typeof(MasterMaterial), typeof(Tran00Uriage), typeof(Tran01Tenuri), typeof(Tran02Material),
			typeof(Tran03Shiire), typeof(Tran12Jyuchu), typeof(Tran13Hachu),
		}) {
			Db.CreateTable(t, true, false);
		}
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	static MasterSysman MakeSysman(string fiscalStart = "19010101") => new() {
		Id = 1,
		FiscalStartDate = fiscalStart,
		TaxRounding = (int)EnumRounding.Round,
		Jsub = [
			new MasterSysTax { Id = 1, TaxRate = 10, DateFrom = "19010101" },
			new MasterSysTax { Id = 2, TaxRate = 8, DateFrom = "19010101" },
		],
	};

	long InsertTokui(int taxCalcUnit, int taxRounding = (int)EnumRounding.Round) {
		var code = Guid.NewGuid().ToString("N")[..8];
		Db.Insert(new MasterTokui { Code = code, Name = "得意先", TaxCalcUnit = taxCalcUnit, TaxRounding = taxRounding });
		return Db.Single<MasterTokui>("where Code=@0", code).Id;
	}

	long InsertShohin(long idTax) {
		var code = Guid.NewGuid().ToString("N")[..8];
		Db.Insert(new MasterShohin { Code = code, Name = "商品", Id_Tax = idTax });
		return Db.Single<MasterShohin>("where Code=@0", code).Id;
	}

	/// <summary>請求単位: ヘッダTax1/2/3=0、TaxableAmountは明細合計で埋まり、Totalは税抜のまま(3.4)</summary>
	[TestMethod]
	public void RebuildAll_請求単位はTaxが0でTaxableAmountが埋まる() {
		Db.Insert(MakeSysman());
		var idTokui = InsertTokui((int)EnumTaxCalcUnit.Billing);
		var idShohin = InsertShohin(1);
		Db.Insert(new Tran00Uriage {
			DenDay = "20260801",
			Id_Tokui = idTokui,
			KingakuTotal = 10000,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = idShohin, Kingaku = 10000 }],
		});

		new TranTaxRebuildDb(Db).RebuildAll();

		var slip = Db.Fetch<Tran00Uriage>().Single();
		Assert.AreEqual((int)EnumTaxCalcUnit.Billing, slip.TaxCalcUnit, "得意先マスタのTaxCalcUnitがヘッダへスナップショットされる");
		Assert.AreEqual(0, slip.Tax1);
		Assert.AreEqual(0, slip.Tax2);
		Assert.AreEqual(0, slip.Tax3);
		Assert.AreEqual(10000, slip.TaxableAmount1, "課税対象額は明細から埋まる");
		Assert.AreEqual(10000, slip.Total, "請求単位のTotalは税抜(|KingakuTotal|)のまま");
	}

	/// <summary>伝票単位: 税区分ごとに1回だけ丸めるため、明細ごとに丸めた合計とは意図的にずれる(3.3/3.6)</summary>
	[TestMethod]
	public void RebuildAll_伝票単位は税区分ごとに1回丸め() {
		Db.Insert(MakeSysman());
		var idTokui = InsertTokui((int)EnumTaxCalcUnit.Slip);
		var idShohin = InsertShohin(1); // 標準税率10%
		Db.Insert(new Tran00Uriage {
			DenDay = "20260801",
			Id_Tokui = idTokui,
			KingakuTotal = 30,
			Jmeisai = [
				new Tran99Meisai { No = 1, Id_Shohin = idShohin, Kingaku = 15 },
				new Tran99Meisai { No = 2, Id_Shohin = idShohin, Kingaku = 15 },
			],
		});

		new TranTaxRebuildDb(Db).RebuildAll();

		var slip = Db.Fetch<Tran00Uriage>().Single();
		// 明細ごとに丸めると 15*10%=1.5→2 が2行で4になるが、税区分(Id_Tax=1)ごとに1回丸めるため
		// (15+15)*10%=3.0→3 になる。ここが本設計の要点。
		Assert.AreEqual(3, slip.Tax1);
		Assert.AreEqual(30, slip.TaxableAmount1);
		Assert.AreEqual(33, slip.Total);
		Assert.AreEqual(3, slip.Jmeisai!.Sum(m => m.Tax), "明細Taxの合計はヘッダTax1と一致する(按分)");
	}

	/// <summary>複数回実行しても結果が変わらない(冪等)</summary>
	[TestMethod]
	public void RebuildAll_複数回実行しても結果が変わらない() {
		Db.Insert(MakeSysman());
		var idTokui = InsertTokui((int)EnumTaxCalcUnit.Slip);
		var idShohin = InsertShohin(1);
		Db.Insert(new Tran00Uriage {
			DenDay = "20260801",
			Id_Tokui = idTokui,
			KingakuTotal = 10000,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = idShohin, Kingaku = 10000 }],
		});

		var first = new TranTaxRebuildDb(Db).RebuildAll();
		var second = new TranTaxRebuildDb(Db).RebuildAll();

		var firstUriage = first.Single(r => r.TableName == nameof(Tran00Uriage));
		var secondUriage = second.Single(r => r.TableName == nameof(Tran00Uriage));
		Assert.AreEqual(1, firstUriage.HeaderTaxChanged, "初回は未計算(0)から計算値へ変わる");
		Assert.AreEqual(1, firstUriage.TaxableAmountFilled);
		Assert.AreEqual(0, secondUriage.HeaderTaxChanged, "2回目は前回と同じ値になり変化なし");
		Assert.AreEqual(0, secondUriage.TaxableAmountFilled, "2回目は既に埋まっているため新規カウントされない");

		var slip = Db.Fetch<Tran00Uriage>().Single();
		Assert.AreEqual(1000, slip.Tax1);
	}

	/// <summary>期首日より前の伝票は再計算の対象外(3.6注記/既存方針)</summary>
	[TestMethod]
	public void RebuildAll_期首日より前は対象外() {
		Db.Insert(MakeSysman(fiscalStart: "20260701"));
		var idTokui = InsertTokui((int)EnumTaxCalcUnit.Slip);
		var idShohin = InsertShohin(1);
		Db.Insert(new Tran00Uriage {
			DenDay = "20260630", // 期首(2026/07/01)より前
			Id_Tokui = idTokui,
			KingakuTotal = 10000,
			Tax1 = 999, // 再計算されないことを確認するための番兵値
			TaxCalcUnit = (int)EnumTaxCalcUnit.Billing, // 得意先マスタと食い違う値のまま残るはず
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = idShohin, Kingaku = 10000 }],
		});

		var results = new TranTaxRebuildDb(Db).RebuildAll();

		var slip = Db.Fetch<Tran00Uriage>().Single();
		Assert.AreEqual(999, slip.Tax1, "期首より前は触らない");
		Assert.AreEqual((int)EnumTaxCalcUnit.Billing, slip.TaxCalcUnit, "スナップショットも上書きされない");
		Assert.AreEqual(0, results.Single(r => r.TableName == nameof(Tran00Uriage)).Scanned, "走査対象にも含まれない");
	}

	/// <summary>Tran02Materialの区分99(その他/消費税調整)はKingakuTotal自体が実額でTax1/2/3は0のまま(3.8 A-6)</summary>
	[TestMethod]
	public void RebuildAll_Tran02MaterialのSonota99はTaxを0のままKingakuTotalの実額を保つ() {
		Db.Insert(MakeSysman());
		var codeShiire = Guid.NewGuid().ToString("N")[..8];
		Db.Insert(new MasterShiire { Code = codeShiire, Name = "仕入先", TaxCalcUnit = (int)EnumTaxCalcUnit.Slip });
		var idShiire = Db.Single<MasterShiire>("where Code=@0", codeShiire).Id;
		Db.Insert(new Tran02Material {
			DenDay = "20260801",
			Id_Shiire = idShiire,
			Kubun = 99,
			KingakuTotal = 50000, // ConvertDbTranが移行時に実額(旧内税+外税消費税)を入れている想定値
			Jmeisai = [new Tran99MaterialMeisai { No = 1, Id_Material = 0, Kingaku = 0 }],
		});

		new TranTaxRebuildDb(Db).RebuildAll();

		var slip = Db.Fetch<Tran02Material>().Single();
		Assert.AreEqual(0, slip.Tax1, "区分99は明細に課税対象が無いためTaxは0のまま");
		Assert.AreEqual(50000, slip.KingakuTotal, "実額はKingakuTotalに残る(触らない)");
		Assert.AreEqual(50000, slip.Total, "Total=|KingakuTotal|+0");
	}
}
