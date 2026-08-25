using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvDomainLogic;
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
		// ヘッダは明細税額の合計。全件10%一括(1750)とは一致しない
		Assert.AreEqual(1520, headerTax);
		Assert.AreEqual(meisai.Sum(m => m.Tax), headerTax);
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
		Assert.AreEqual(500, headerTax);
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
		Assert.AreEqual(900, headerTax);
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
