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
	// GetTotalAverageExclusion: 設計書§6.5「2026-09-06改訂」、§13 U-06・U-15
	// OpeningQtyが-1/0/1、beforeCostが0/1の組み合わせで境界値を固定する。
	// ------------------------------------------------------------

	[TestMethod]
	public void GetTotalAverageExclusion_前月在庫が負は原価に関わらず負在庫で対象外() {
		Assert.AreEqual(EnumCostTargetExclusion.NegativeOpeningQty, CostCalculator.GetTotalAverageExclusion(openingQty: -1, beforeCost: 0));
		Assert.AreEqual(EnumCostTargetExclusion.NegativeOpeningQty, CostCalculator.GetTotalAverageExclusion(openingQty: -1, beforeCost: 1));
	}

	[TestMethod]
	public void GetTotalAverageExclusion_前月在庫0は原価0でも対象外にならない() {
		// 最重要: 新規商品(前月在庫0・原価未設定)を対象外に巻き込むと、当月仕入で原価が永久に決まらなくなる。
		Assert.AreEqual(EnumCostTargetExclusion.None, CostCalculator.GetTotalAverageExclusion(openingQty: 0, beforeCost: 0));
		Assert.AreEqual(EnumCostTargetExclusion.None, CostCalculator.GetTotalAverageExclusion(openingQty: 0, beforeCost: 1));
	}

	[TestMethod]
	public void GetTotalAverageExclusion_前月在庫が正で原価0以下は対象外() {
		Assert.AreEqual(EnumCostTargetExclusion.NoCostWithOpeningStock, CostCalculator.GetTotalAverageExclusion(openingQty: 1, beforeCost: 0));
	}

	[TestMethod]
	public void GetTotalAverageExclusion_前月在庫が正で原価も正なら対象外にならない() {
		Assert.AreEqual(EnumCostTargetExclusion.None, CostCalculator.GetTotalAverageExclusion(openingQty: 1, beforeCost: 1));
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

	// ------------------------------------------------------------------
	// C-14 オーバーフロー(設計書§11.1): 数量×単価・在庫×原価・按分中間値がlongの範囲で
	// 破綻しないことを固定する。本プロジェクトはCheckForOverflowUnderflowを設定していないため、
	// long同士の算術は既定でunchecked(オーバーフロー時に例外を出さず、2の補数でラップする)。
	// 以下は「壊れていないこと」の確認と、「壊れている箇所」を再現して固定する目的の両方を含む。
	// 壊れている箇所はテストで固定するのみとし、production コードは修正しない(私の判断を待つ)。
	// ------------------------------------------------------------------

	[TestMethod]
	public void CalcTotalAverageCost_Denominatorの2項オーバーフローは必ず負になりエラーで捕捉される() {
		// OpeningQty・PurchaseQtyはどちらも正の値として渡ってくる想定であり、2つの正のlongの加算が
		// オーバーフローする場合、2の補数表現の性質上、結果は必ず負になる(0<=a,b<2^63のときa+b<2^64であり、
		// 2^63を超えた場合のみ符号ビットが立つため)。したがって既存の「Denominator<=0はエラー」判定に
		// 必ず引っかかり、誤った正の値を分母に使うことはない。安全側であることをここで固定する。
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: long.MaxValue - 5, OpeningAmount: 1, PurchaseQty: 10, PurchaseAmount: 1, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 1);

		Assert.AreEqual(EnumCostCalcError.NonPositiveDenominator, result.Error);
		Assert.IsTrue(result.Denominator < 0, $"Denominator={result.Denominator}");
	}

	/// <summary>
	/// 【既知の限界。実務データでは到達しない】<see cref="CostCalculator.CalcTotalAverageCost"/>のNumerator計算
	/// (<c>OpeningAmount + PurchaseAmount + SundryAmount</c>)はchecked演算ではないため、
	/// 3項の加算が2度ラップアラウンドすると、本来は表現不能な巨大な金額のはずが、
	/// 何の検知もされずに全く別の「もっともらしい」小さな正の値になり得る。
	/// <para>
	/// 本テストは OpeningAmount・PurchaseAmount をそれぞれ<c>long.MaxValue</c>近くまで積み、
	/// SundryAmount で帳尻を合わせることで、意図的に「一見正常に見える」誤った計算結果を再現する。
	/// Denominatorの2項加算(<see cref="CalcTotalAverageCost_Denominatorの2項オーバーフローは必ず負になりエラーで捕捉される"/>)
	/// と異なり、Numeratorは3項の加算であるため「2つの正が負にラップし、3つ目の正でまた正に戻る」
	/// 経路が存在し、safety netにならない。
	/// </para>
	/// <para>
	/// 実務データでOpeningAmount・PurchaseAmountがlong.MaxValue付近(約922京円)に達することは現実的ではないが、
	/// 設計書§11.1 C-14は「longの範囲で破綻しないこと」を求めており、この関数がchecked/BigIntegerを
	/// 使っていない以上、境界では誤った値を返し得ることをここで固定して報告する。
	/// </para>
	/// </summary>
	[TestMethod]
	public void CalcTotalAverageCost_Numeratorの3項オーバーフローは検知されない既知の限界() {
		const long openingAmount = long.MaxValue - 100; // 9223372036854775707
		const long purchaseAmount = long.MaxValue - 100; // 同上。2つ合計はunchecked long加算で-202へラップする
		const long sundryAmount = 100_202; // -202 + 100_202 = 100_000 (本来あり得ない巨大な合計が消えて小さい正値になる)
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 10, OpeningAmount: openingAmount, PurchaseQty: 14, PurchaseAmount: purchaseAmount, SundryAmount: sundryAmount);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 1);

		// 現在の実装が実際に返す値をそのまま固定する(退行検知が目的。これが「正しい」という意味ではない)。
		Assert.AreEqual(EnumCostCalcError.None, result.Error, "オーバーフローがエラーとして検知されていない");
		Assert.AreEqual(100_000L, result.Numerator, "3項加算がラップアラウンドし、本来の巨大な合計とは無関係な値になっている");
		Assert.AreEqual(4166L, result.AfterCost); // floor(100000/24) — 何の警告もなく「もっともらしい」原価が返る
	}

	[TestMethod]
	public void RoundToUnit_極端に大きい値はdecimalからlongへの変換でOverflowExceptionになる() {
		// RoundToUnitの最終行 (long)(roundedScaled * unit) はdecimal→long変換であり、
		// C#仕様上decimalが絡む数値変換は常にchecked相当でOverflowExceptionを送出する
		// (int/long同士のプリミティブ演算のようにcheckedコンテキスト指定が必要なわけではない)。
		// したがってRoundToUnit自体は「誤った値を返す」のではなく「例外で検知される」安全側であることを固定する。
		var huge = (decimal)long.MaxValue * 1000m; // long表現域を明らかに超える
		Assert.ThrowsExactly<OverflowException>(() => CostCalculator.RoundToUnit(huge, unit: 1, EnumRounding.Floor));
	}

	[TestMethod]
	public void CalcLastPurchaseCost_保存先のint列に収まらないAfterCostはエラーになる() {
		// CalcLastPurchaseCostはkingaku/suをdecimalで割ってからMath.Round・(long)キャストするため、
		// 計算そのものはlongの範囲まで破綻しない。ただし保存先のTranGenka.AfterCostと
		// MasterShohin.TankaGenkaはint列であり、long→intのナローイングキャストはuncheckedである。
		// そのまま通すと符号が反転した負の原価が無警告で保存されるため、範囲外はここでエラーにする
		// (設計書§2.2「DB保存値は現行互換の円単位整数」、§11.1 C-14)。
		var result = CostCalculator.CalcLastPurchaseCost(kingaku: long.MaxValue - 1, su: 1);

		Assert.AreEqual(EnumCostCalcError.AfterCostOutOfRange, result.Error);
	}

	[TestMethod]
	public void CalcLastPurchaseCost_int範囲の上限ちょうどは正常に計算される() {
		// 境界。int.MaxValue は保存できるのでエラーにしない。
		var result = CostCalculator.CalcLastPurchaseCost(kingaku: int.MaxValue, su: 1);

		Assert.AreEqual(EnumCostCalcError.None, result.Error);
		Assert.AreEqual(int.MaxValue, result.AfterCost);
	}

	[TestMethod]
	public void CalcTotalAverageCost_int範囲を超えるAfterCostはエラーになる() {
		// 総平均原価も保存先は同じint列であり、同じ理由で範囲外をエラーにする。
		var input = new CostCalculator.TotalAverageInput(
			OpeningQty: 0, OpeningAmount: 0, PurchaseQty: 1, PurchaseAmount: 3_000_000_000L, SundryAmount: 0);

		var result = CostCalculator.CalcTotalAverageCost(input, beforeCost: 0);

		Assert.AreEqual(EnumCostCalcError.AfterCostOutOfRange, result.Error);
	}

	/// <summary>
	/// 【既知の限界。実務データでは到達しない】<see cref="CostCalculator.CalcConsumptionUnitCostByRate"/>の
	/// <c>raw = uriageTanka * rateBasisPoints / 10000m</c> は、乗算 <c>uriageTanka * rateBasisPoints</c> が
	/// 「long(uriageTanka) × int(rateBasisPoints、long へ昇格)」というlong同士の乗算として先に評価され、
	/// 10000mによる除算(decimal昇格)より前にオーバーフローし得る。乗算は加算と違い、オーバーフロー時に
	/// 符号や大小関係が保証されないため、Denominatorの2項加算のような「安全側に倒れる」性質がない。
	/// </summary>
	[TestMethod]
	public void CalcConsumptionUnitCostByRate_乗算段階のオーバーフローは検知されない既知の限界() {
		const long hugeUriageTanka = 2_000_000_000_000_000L; // 2×10^15。現実の売上単価としてはあり得ないが、long範囲内
		const int rateBasisPoints = 10000; // 100%。本来ならAfterCost==hugeUriageTankaになるはず

		var result = CostCalculator.CalcConsumptionUnitCostByRate(hugeUriageTanka, rateBasisPoints, roundingUnit: 1, rounding: EnumRounding.Round);

		Assert.AreEqual(EnumCostCalcError.None, result.Error, "オーバーフローがエラーとして検知されていない");
		// 本来100%掛率ならAfterCost==hugeUriageTankaになるはずだが、乗算オーバーフローにより一致しない。
		Assert.AreNotEqual(hugeUriageTanka, result.AfterCost);
		// 現在の実装が実際に返す値をそのまま固定する(退行検知が目的)。
		Assert.AreEqual(155_325_592_629_045L, result.AfterCost);
	}
}
