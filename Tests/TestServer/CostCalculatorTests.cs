using System;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="CostCalculator"/> の原価計算純ロジック。
/// 仕様は `Doc/spec/2026-09-05_原価4項目_詳細設計.md` の §5・§6・§16 を参照する。
/// </summary>
[TestClass]
public class CostCalculatorTests {

	// ------------------------------------------------------------
	// 総平均原価: 設計書§11.5 T-01・T-02
	// ------------------------------------------------------------

	[TestMethod]
	public void CalcTotalAverageCost_T01_前月在庫と当月仕入から総平均原価を求める() {
		// 前月在庫10個×5,000円、当月仕入14個・68,000円 → (50,000+68,000)/(10+14) = 4,916円(floor)
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: 50_000, PurchaseQty: 14, PurchaseAmount: 68_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.IsFalse(result.IsError);
		Assert.AreEqual(24, result.Denominator);
		Assert.AreEqual(118_000, result.Numerator);
		Assert.AreEqual(4_916L, result.AfterCost);
	}

	[TestMethod]
	public void CalcTotalAverageCost_T02_諸掛が分子へ加算され再実行しても結果は変わらない() {
		// 商品Aへの諸掛明細が3行(30円・40円・30円) → SundryAmount=100円
		const long sundryAmount = 30 + 40 + 30;
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: 50_000, PurchaseQty: 14, PurchaseAmount: 68_000, SundryAmount: sundryAmount);

		var first = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);
		var second = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);

		Assert.AreEqual(118_100, first.Numerator);
		// 純関数の冪等性: 同じ入力を2回渡しても結果は完全に一致する(再実行で値が増えない)。
		Assert.AreEqual(first, second);
	}

	// ------------------------------------------------------------
	// 最終仕入原価の丸め方向: 設計書§5.3
	// ------------------------------------------------------------

	[TestMethod]
	public void CalcLastPurchaseCost_割り切れる場合は明細単価と一致する() {
		var result = CostCalculator.CalcLastPurchaseCost(kingaku: 11_490, su: 30);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(383L, result.AfterCost);
	}

	[TestMethod]
	public void CalcLastPurchaseCost_割り切れない場合はfloorではなく最も近い整数へ丸める() {
		// 11,489/30 = 382.966... floorなら382になるが、round_away_from_zeroで383。
		// 最終仕入原価だけこの丸め方向にする(floorとの差)を固定するのがこのテストの目的。
		var result = CostCalculator.CalcLastPurchaseCost(kingaku: 11_489, su: 30);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(383L, result.AfterCost);
	}

	[TestMethod]
	public void CalcTotalAverageCost_総平均はfloorであることを割り切れない入力で確認する() {
		// 68,003/14 = 4,857.35... 総平均はfloorのため4,857円(最終仕入と同じ入力ならround_away_from_zeroで4,858円になるはずの値)。
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 14, PurchaseAmount: 68_003, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(4_857L, result.AfterCost);
	}

	// ------------------------------------------------------------
	// 総平均原価: 設計書§6.5 境界値表(1条件1テスト)
	// ------------------------------------------------------------

	[TestMethod]
	public void CalcTotalAverageCost_前月在庫が負はエラー() {
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: -1, OpeningAmount: -5_000, PurchaseQty: 10, PurchaseAmount: 50_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);

		Assert.AreEqual(EnumCostCalcError.NegativeOpeningQty, result.Error);
		Assert.IsTrue(result.IsError);
	}

	[TestMethod]
	public void CalcTotalAverageCost_前月在庫0かつ当月仕入ありは正常() {
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 10, PurchaseAmount: 50_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(5_000L, result.AfterCost);
	}

	[TestMethod]
	public void CalcTotalAverageCost_前月在庫ありで計算前原価が0以下はエラー() {
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: 0, PurchaseQty: 10, PurchaseAmount: 50_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.NonPositiveBeforeCost, result.Error);
	}

	[TestMethod]
	public void CalcTotalAverageCost_分母が0以下はエラー() {
		// 前月在庫・当月仕入とも0数量、返品で相殺されて分母が0になるケース
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 0, PurchaseAmount: 0, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.NonPositiveDenominator, result.Error);
		Assert.AreEqual(0, result.Denominator);
	}

	[TestMethod]
	public void CalcTotalAverageCost_分子が0以下はエラー() {
		// 分母は正だが、返品分の金額(負値)で分子が0以下になるケース
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: 50_000, PurchaseQty: 5, PurchaseAmount: -50_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);

		Assert.AreEqual(EnumCostCalcError.NonPositiveNumerator, result.Error);
		Assert.AreEqual(15, result.Denominator);
		Assert.AreEqual(0, result.Numerator);
	}

	[TestMethod]
	public void CalcTotalAverageCost_計算後原価が0以下はエラー() {
		// 分母・分子とも正だが、分子が分母を大きく下回り floor すると0になるケース
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 100, PurchaseAmount: 50, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.NonPositiveAfterCost, result.Error);
		Assert.AreEqual(0L, result.AfterCost);
	}

	[TestMethod]
	public void CalcTotalAverageCost_当月仕入額はあるが数量0はエラー() {
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: 50_000, PurchaseQty: 0, PurchaseAmount: 10_000, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 5_000);

		Assert.AreEqual(EnumCostCalcError.PurchaseAmountWithoutQty, result.Error);
	}

	[TestMethod]
	public void CalcTotalAverageCost_当月仕入が無く諸掛だけがある場合はエラー() {
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 0, PurchaseAmount: 0, SundryAmount: 100);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.SundryOnlyWithoutBase, result.Error);
	}

	[TestMethod]
	public void CalcTotalAverageCost_諸掛が負で分子が0以下になる場合はエラー() {
		// 返品諸掛が過大で分子が0以下になるケース(設計書§6.5「諸掛の合計が負でNumerator<=0」)
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 10, PurchaseAmount: 10_000, SundryAmount: -20_000);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.NonPositiveNumerator, result.Error);
	}

	// ------------------------------------------------------------
	// RoundToUnit: 単位×丸め方式の組み合わせ、境界(ちょうど半分)、不正unit
	// ------------------------------------------------------------

	[TestMethod]
	[DataRow(1250.0, 1, EnumRounding.Round, 1250L)]
	[DataRow(1250.0, 10, EnumRounding.Round, 1250L)]
	[DataRow(1255.0, 10, EnumRounding.Round, 1260L)] // 125.5 → 四捨五入で126*10
	[DataRow(1245.0, 10, EnumRounding.Round, 1250L)] // 124.5 → 四捨五入で125*10
	[DataRow(1201.0, 100, EnumRounding.Ceiling, 1300L)]
	[DataRow(1200.0, 100, EnumRounding.Ceiling, 1200L)]
	[DataRow(1250.0, 100, EnumRounding.Ceiling, 1300L)] // 12.5 → 切上で13*100
	[DataRow(1299.0, 100, EnumRounding.Floor, 1200L)]
	[DataRow(1250.0, 100, EnumRounding.Floor, 1200L)] // 12.5 → 切捨で12*100
	[DataRow(1500.0, 1000, EnumRounding.Round, 2000L)] // 1.5 → 四捨五入で2*1000
	[DataRow(500.0, 1000, EnumRounding.Round, 1000L)] // 0.5 → 四捨五入で1*1000(AwayFromZero)
	public void RoundToUnit_単位と丸め方式の組み合わせ(double raw, int unit, EnumRounding rounding, long expected) {
		Assert.AreEqual(expected, CostCalculator.RoundToUnit((decimal)raw, unit, rounding));
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(5)]
	[DataRow(-1)]
	public void RoundToUnit_不正な端数単位は例外(int unit) {
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CostCalculator.RoundToUnit(100m, unit, EnumRounding.Round));
	}

	// ------------------------------------------------------------
	// 消化仕入: 計算区分1(上代×掛率)
	// ------------------------------------------------------------

	[TestMethod]
	public void CalcConsumptionUnitCostByRate_掛率65パーセントを端数単位100円で四捨五入する() {
		// 上代10,000円 × 65.00%(rateBasisPoints=6500) = 6,500円 → 100円単位で四捨五入(丸め不要)
		var result = CostCalculator.CalcConsumptionUnitCostByRate(uriageTanka: 10_000, rateBasisPoints: 6500, roundingUnit: 100, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(6_500L, result.AfterCost);
	}

	[TestMethod]
	public void CalcConsumptionUnitCostByRate_端数のある掛率計算を100円単位で四捨五入する() {
		// 上代9,980円 × 65.00% = 6,487 → 100円単位で四捨五入すると6,500円
		var result = CostCalculator.CalcConsumptionUnitCostByRate(uriageTanka: 9_980, rateBasisPoints: 6500, roundingUnit: 100, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(6_500L, result.AfterCost);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(10001)]
	public void CalcConsumptionUnitCostByRate_掛率が範囲外はInvalidRate(int rateBasisPoints) {
		var result = CostCalculator.CalcConsumptionUnitCostByRate(uriageTanka: 10_000, rateBasisPoints: rateBasisPoints, roundingUnit: 100, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.InvalidRate, result.Error);
		Assert.IsTrue(result.IsError);
	}

	// ------------------------------------------------------------
	// 評価替え: 設計書§16.5、§13 U-18
	// ------------------------------------------------------------

	[TestMethod]
	public void CalcRevalCostByRate_掛率70パーセントは30パーセント引きではなく掛率そのもの() {
		// 掛率70% → BeforeCost=1000 → 700(30%引きの300ではない。§13 U-18)
		var result = CostCalculator.CalcRevalCostByRate(beforeCost: 1_000, ratePercent: 70, roundingUnit: 1, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(700L, result.AfterCost);
		Assert.AreNotEqual(300L, result.AfterCost);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(101)]
	public void CalcRevalCostByRate_掛率が範囲外はInvalidRate(int ratePercent) {
		var result = CostCalculator.CalcRevalCostByRate(beforeCost: 1_000, ratePercent: ratePercent, roundingUnit: 1, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.InvalidRate, result.Error);
	}

	[TestMethod]
	public void CalcRevalCostByFixed_指定単価をそのまま採用する() {
		var result = CostCalculator.CalcRevalCostByFixed(beforeCost: 1_000, fixedCost: 800, roundingUnit: 10, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(800L, result.AfterCost);
	}

	[TestMethod]
	public void IsRevalTarget_計算後原価が計算前原価以上は対象外でありエラーではない() {
		var result = CostCalculator.CalcRevalCostByRate(beforeCost: 1_000, ratePercent: 100, roundingUnit: 1, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.IsFalse(result.IsError);
		// AfterCost(1000) >= BeforeCost(1000) は評価替えの「対象外」であり、エラーではない。
		Assert.IsFalse(CostCalculator.IsRevalTarget(beforeCost: 1_000, afterCost: result.AfterCost));
	}

	[TestMethod]
	public void IsRevalTarget_計算後原価が計算前原価を下回れば対象() {
		var result = CostCalculator.CalcRevalCostByRate(beforeCost: 1_000, ratePercent: 70, roundingUnit: 1, rounding: EnumRounding.Round);

		Assert.IsTrue(CostCalculator.IsRevalTarget(beforeCost: 1_000, afterCost: result.AfterCost));
	}

	// ------------------------------------------------------------
	// LastPurchaseKey: 設計書§5.2
	// ------------------------------------------------------------

	[TestMethod]
	public void LastPurchaseKey_同日は仕入Idの大きい方が最終行になる() {
		var older = new CostCalculator.LastPurchaseKey("20260901", ShiireId: 100, MeisaiNo: 1);
		var newer = new CostCalculator.LastPurchaseKey("20260901", ShiireId: 200, MeisaiNo: 1);

		Assert.IsTrue(newer.CompareTo(older) > 0);
	}

	[TestMethod]
	public void LastPurchaseKey_同日同仕入は明細Noの大きい方が最終行になる() {
		var first = new CostCalculator.LastPurchaseKey("20260901", ShiireId: 100, MeisaiNo: 1);
		var second = new CostCalculator.LastPurchaseKey("20260901", ShiireId: 100, MeisaiNo: 2);

		Assert.IsTrue(second.CompareTo(first) > 0);
	}

	[TestMethod]
	public void LastPurchaseKey_伝票日が異なれば伝票日だけで順序が決まる() {
		var early = new CostCalculator.LastPurchaseKey("20260831", ShiireId: 999, MeisaiNo: 99);
		var late = new CostCalculator.LastPurchaseKey("20260901", ShiireId: 1, MeisaiNo: 1);

		Assert.IsTrue(late.CompareTo(early) > 0);
	}
}
