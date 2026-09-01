using System;
using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="TaxCalculator.Apply"/> の共通消費税計算（伝票単位・請求単位・按分・端数処理）。
/// 仕様は `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` の 3.1〜3.7 を参照する。
/// </summary>
[TestClass]
public class TaxCalculatorTests {

	/// <summary>Id_Tax=1 は10%、Id_Tax=2 は8%を返す単純な rateOf</summary>
	static int RateOf(long taxId) => taxId switch {
		1 => 10,
		2 => 8,
		_ => 0,
	};

	static Tran99Meisai Line(int no, long idTax, int kingaku) =>
		new() { No = no, Id_Tax = idTax, Kingaku = kingaku };

	static Tran99MaterialMeisai MaterialLine(int no, long idTax, long kingaku) =>
		new() { No = no, Id_Tax = idTax, Kingaku = kingaku };

	[TestMethod]
	public void Apply_伝票単位_単一税区分は税区分ごとに1回だけ丸める() {
		var meisai = new List<Tran99Meisai> { Line(1, 1, 100), Line(2, 1, 200), Line(3, 1, 300) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(600L, totals.TaxableAmount1);
		Assert.AreEqual(60L, totals.Tax1);
		Assert.AreEqual(0L, totals.Tax2);
		Assert.AreEqual(0L, totals.Tax3);
		Assert.AreEqual(60L, meisai.Sum(m => m.Tax));
	}

	[TestMethod]
	public void Apply_伝票単位_税区分混在は別々に1回ずつ丸める() {
		// Id_Tax=1(10%) と Id_Tax=2(8%) が混在。全件10%一括にすると結果が変わることを確認する
		var meisai = new List<Tran99Meisai> { Line(1, 1, 1000), Line(2, 2, 1000) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(1000L, totals.TaxableAmount1);
		Assert.AreEqual(1000L, totals.TaxableAmount2);
		Assert.AreEqual(100L, totals.Tax1); // 1000 * 10%
		Assert.AreEqual(80L, totals.Tax2);  // 1000 * 8%
		Assert.AreEqual(100, meisai[0].Tax);
		Assert.AreEqual(80, meisai[1].Tax);
	}

	[TestMethod]
	public void Apply_端数処理3通りで結果が変わる() {
		// 105円 * 10% = 10.5円。四捨五入11/切上11/切捨10 になるよう、切上と四捨五入が一致するケースも混ぜて確認する
		var round = TaxCalculator.Apply(
			Cast(Line(1, 1, 105)), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);
		var ceiling = TaxCalculator.Apply(
			Cast(Line(1, 1, 105)), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Ceiling);
		var floor = TaxCalculator.Apply(
			Cast(Line(1, 1, 105)), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Floor);

		Assert.AreEqual(11L, round.Tax1);   // 10.5 → 四捨五入で11
		Assert.AreEqual(11L, ceiling.Tax1); // 10.5 → 切上で11
		Assert.AreEqual(10L, floor.Tax1);   // 10.5 → 切捨で10
	}

	static IEnumerable<ITaxMeisaiLine> Cast(params Tran99Meisai[] lines) => lines.Cast<ITaxMeisaiLine>();

	[TestMethod]
	public void Apply_按分は端数を最終行へ寄せて明細合計をヘッダと必ず一致させる() {
		// 100/200/303 の合計603に10%→60.3→四捨五入60。単純比例配分だと 9+19+30=58 のように合計がずれるケース
		var meisai = new List<Tran99Meisai> { Line(1, 1, 100), Line(2, 1, 200), Line(3, 1, 303) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(60L, totals.Tax1);
		Assert.AreEqual(60, meisai.Sum(m => m.Tax));
		// 端数は課税対象額が最大の行(No.3, 303円)へ寄せる
		var expectedShare2 = 60 * 200 / 603; // = 19
		var expectedShare1 = 60 * 100 / 603; // = 9
		Assert.AreEqual(expectedShare1, meisai[0].Tax);
		Assert.AreEqual(expectedShare2, meisai[1].Tax);
		Assert.AreEqual(60 - expectedShare1 - expectedShare2, meisai[2].Tax);
	}

	[TestMethod]
	public void Apply_請求単位はTaxが0でTaxableAmountのみ確定する() {
		var meisai = new List<Tran99Meisai> { Line(1, 1, 100), Line(2, 1, 200), Line(3, 2, 500) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Billing, EnumRounding.Round);

		Assert.AreEqual(0L, totals.Tax1);
		Assert.AreEqual(0L, totals.Tax2);
		Assert.AreEqual(0L, totals.Tax3);
		Assert.AreEqual(300L, totals.TaxableAmount1);
		Assert.AreEqual(500L, totals.TaxableAmount2);
		Assert.IsTrue(meisai.All(m => m.Tax == 0));
	}

	[TestMethod]
	public void Apply_非課税は課税対象額にも税額にも含まれない() {
		var meisai = new List<Tran99Meisai> { Line(1, 0, 5000), Line(2, 1, 1000) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(1000L, totals.TaxableAmount1);
		Assert.AreEqual(100L, totals.Tax1);
		Assert.AreEqual(0, meisai[0].Tax);
		Assert.AreEqual(0, meisai[0].TaxRate);
	}

	[TestMethod]
	public void Apply_負の明細金額でも課税対象額と税額は正値になる() {
		var meisai = new List<Tran99Meisai> { Line(1, 1, -1000), Line(2, 1, -2000) };

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(3000L, totals.TaxableAmount1);
		Assert.AreEqual(300L, totals.Tax1);
		Assert.IsTrue(meisai.All(m => m.Tax > 0));
		Assert.AreEqual(300, meisai.Sum(m => m.Tax));
	}

	[TestMethod]
	public void Apply_Tran99MaterialMeisaiでも同じ結果になる() {
		var meisai = new List<Tran99MaterialMeisai> {
			MaterialLine(1, 1, 100), MaterialLine(2, 1, 200), MaterialLine(3, 1, 303),
		};

		var totals = TaxCalculator.Apply(
			meisai.Cast<ITaxMeisaiLine>(), RateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);

		Assert.AreEqual(60L, totals.Tax1);
		Assert.AreEqual(60L, meisai.Sum(m => m.Tax));
		var expectedShare1 = 60 * 100 / 603;
		var expectedShare2 = 60 * 200 / 603;
		Assert.AreEqual(expectedShare1, meisai[0].Tax);
		Assert.AreEqual(expectedShare2, meisai[1].Tax);
		Assert.AreEqual(60 - expectedShare1 - expectedShare2, meisai[2].Tax);
	}
}
