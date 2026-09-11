using System;
using System.Collections.Generic;
using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 上代一括変更 Step3 <see cref="JodaiPriceRule"/> の単体テスト。
/// <para>
/// 純粋クラスのためDBは不要。仕様は `Doc/spec/2026-09-05_上代一括変更_詳細設計.md` 2.7・6.1。
/// 丸めは現行 <c>MasterJouDaiBulkChangeViewModel.ApplyRound</c>（<c>double</c>版）と1円も違わないことが
/// 最優先要件（Step3タスク指示）なので、境界値（5ちょうど・-5相当・切上の既にちょうどの値）を重点的に検証する。
/// </para>
/// </summary>
[TestClass]
public class JodaiPriceRuleTests {
	// ============================================================
	// 方式0〜5 基本ケース
	// ============================================================

	[TestMethod]
	public void FixedPrice_ReturnsFixedPriceAsIs_NoRounding() {
		// 方式0固定額は現行実装（CalcType=0）同様、丸めを適用しない。
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.FixedPrice, baseJodai: 12800, fixedPrice: 7999, rateOff: 0, amount: 0, rateOn: 0, roundUnit: 2, roundType: 1);
		Assert.AreEqual(7999, actual);
	}

	[TestMethod]
	public void RateOff_CalculatesFromBaseJodai() {
		// 12,800円の30%OFF=8,960円 → 百円四捨五入で9,000円
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOff, baseJodai: 12800, fixedPrice: 0, rateOff: 30m, amount: 0, rateOn: 0, roundUnit: 2, roundType: 1);
		Assert.AreEqual(9000, actual);
	}

	[TestMethod]
	public void Amount_SubtractsFromBaseJodai() {
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.Amount, baseJodai: 12800, fixedPrice: 0, rateOff: 0, amount: 2000, rateOn: 0, roundUnit: 0, roundType: 0);
		Assert.AreEqual(10800, actual);
	}

	[TestMethod]
	public void RateOn_MultipliesBaseJodai() {
		// 12,800円 × 70% = 8,960円 → 十円切捨てで8,960円のまま
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOn, baseJodai: 12800, fixedPrice: 0, rateOff: 0, amount: 0, rateOn: 70m, roundUnit: 1, roundType: 0);
		Assert.AreEqual(8960, actual);
	}

	[TestMethod]
	public void RateOffFromEffective_SameFormulaAsRateOff_DifferentBase() {
		// 方式4は「呼び出し側が渡す基準額が実効上代」であること以外、方式1と計算式は同一。
		var viaRateOff = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOff, baseJodai: 9800, fixedPrice: 0, rateOff: 20m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0);
		var viaEffective = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOffFromEffective, baseJodai: 9800, fixedPrice: 0, rateOff: 20m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0);
		Assert.AreEqual(viaRateOff, viaEffective);
		Assert.AreEqual(7840, viaEffective);
	}

	[TestMethod]
	public void PricePoint_SnapsCalculatedValueToNearestPoint() {
		// 設計書2.7の実例。5.4 の Price Matrix の JK-001（通常上代 12,800、OUTLET 40% OFF）が対応し、
		// 12,800 × 0.6 = 7,680 が「算出値」。それを価格ポイント表へ寄せて 7,900 になる。
		// 基準上代 12,800 をそのまま寄せるのではない（それでは 9,900 になってしまう）。
		var points = new[] { 5900, 6900, 7900, 8900, 9900 };
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.PricePoint, baseJodai: 12800, fixedPrice: 0, rateOff: 40m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0, pricePoints: points);
		Assert.AreEqual(7900, actual);
	}

	[TestMethod]
	public void PricePoint_丸めではなく価格ポイントへ寄せる() {
		// 同じ算出値でも、方式1（値下率＋丸め）なら 7,680 のまま（1円単位・切捨）。
		// 方式5は丸めを行わず価格ポイント表へ寄せるので 7,900 になる。両者の違いを固定する。
		var points = new[] { 5900, 6900, 7900, 8900, 9900 };
		var byRate = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOff, baseJodai: 12800, fixedPrice: 0, rateOff: 40m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0);
		var byPoint = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.PricePoint, baseJodai: 12800, fixedPrice: 0, rateOff: 40m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0, pricePoints: points);
		Assert.AreEqual(7680, byRate);
		Assert.AreEqual(7900, byPoint);
	}

	[TestMethod]
	public void PricePoint_表が空なら算出値をそのまま返す() {
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.PricePoint, baseJodai: 12800, fixedPrice: 0, rateOff: 40m, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0, pricePoints: null);
		Assert.AreEqual(7680, actual);
	}

	// ============================================================
	// 方式2: 値引額が基準額を超える場合は0
	// ============================================================

	[TestMethod]
	public void Amount_ExceedsBaseJodai_ClampsToZero() {
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.Amount, baseJodai: 1000, fixedPrice: 0, rateOff: 0, amount: 5000, rateOn: 0, roundUnit: 0, roundType: 0);
		Assert.AreEqual(0, actual);
	}

	[TestMethod]
	public void FixedPrice_Negative_ClampsToZero() {
		var actual = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.FixedPrice, baseJodai: 0, fixedPrice: -100, rateOff: 0, amount: 0, rateOn: 0, roundUnit: 0, roundType: 0);
		Assert.AreEqual(0, actual);
	}

	// ============================================================
	// ApplyRound: 丸めの全組み合わせ（4単位 × 3方法 = 12通り）を境界値で
	// ============================================================

	// --- 単位0: 1円（無丸め相当。切捨/四捨五入/切上いずれも値そのまま） ---

	[TestMethod]
	public void Round_Unit1Yen_Floor() => Assert.AreEqual(1234, JodaiPriceRule.ApplyRound(1234m, roundUnit: 0, roundType: 0));

	[TestMethod]
	public void Round_Unit1Yen_Nearest() => Assert.AreEqual(1234, JodaiPriceRule.ApplyRound(1234m, roundUnit: 0, roundType: 1));

	[TestMethod]
	public void Round_Unit1Yen_Ceiling() => Assert.AreEqual(1234, JodaiPriceRule.ApplyRound(1234m, roundUnit: 0, roundType: 2));

	// --- 単位1: 10円 ---

	[TestMethod]
	public void Round_Unit10Yen_Floor_1234_To_1230() => Assert.AreEqual(1230, JodaiPriceRule.ApplyRound(1234m, roundUnit: 1, roundType: 0));

	[TestMethod]
	public void Round_Unit10Yen_Nearest_AtMidpoint_1235_RoundsAwayFromZero_To_1240() =>
		// 境界値: ちょうど5(の位)。四捨五入は算術丸め（AwayFromZero）であり銀行丸めではないので1240（切り上がる）。
		Assert.AreEqual(1240, JodaiPriceRule.ApplyRound(1235m, roundUnit: 1, roundType: 1));

	[TestMethod]
	public void Round_Unit10Yen_Nearest_BelowMidpoint_1234_RoundsDown_To_1230() =>
		Assert.AreEqual(1230, JodaiPriceRule.ApplyRound(1234m, roundUnit: 1, roundType: 1));

	[TestMethod]
	public void Round_Unit10Yen_Ceiling_1231_To_1240() => Assert.AreEqual(1240, JodaiPriceRule.ApplyRound(1231m, roundUnit: 1, roundType: 2));

	[TestMethod]
	public void Round_Unit10Yen_Ceiling_ExactMultiple_1230_StaysAt_1230() =>
		// 切上は既にちょうどの値のとき余計に上がらないこと。
		Assert.AreEqual(1230, JodaiPriceRule.ApplyRound(1230m, roundUnit: 1, roundType: 2));

	// --- 単位2: 百円 ---

	[TestMethod]
	public void Round_Unit100Yen_Floor_1290_To_1200() => Assert.AreEqual(1200, JodaiPriceRule.ApplyRound(1290m, roundUnit: 2, roundType: 0));

	[TestMethod]
	public void Round_Unit100Yen_Nearest_AtMidpoint_1250_RoundsAwayFromZero_To_1300() =>
		Assert.AreEqual(1300, JodaiPriceRule.ApplyRound(1250m, roundUnit: 2, roundType: 1));

	[TestMethod]
	public void Round_Unit100Yen_Ceiling_ExactMultiple_1200_StaysAt_1200() =>
		Assert.AreEqual(1200, JodaiPriceRule.ApplyRound(1200m, roundUnit: 2, roundType: 2));

	// --- 単位3: 千円 ---

	[TestMethod]
	public void Round_Unit1000Yen_Floor_1900_To_1000() => Assert.AreEqual(1000, JodaiPriceRule.ApplyRound(1900m, roundUnit: 3, roundType: 0));

	[TestMethod]
	public void Round_Unit1000Yen_Nearest_AtMidpoint_1500_RoundsAwayFromZero_To_2000() =>
		Assert.AreEqual(2000, JodaiPriceRule.ApplyRound(1500m, roundUnit: 3, roundType: 1));

	[TestMethod]
	public void Round_Unit1000Yen_Ceiling_ExactMultiple_2000_StaysAt_2000() =>
		Assert.AreEqual(2000, JodaiPriceRule.ApplyRound(2000m, roundUnit: 3, roundType: 2));

	// --- 負値相当の境界（マイナス5相当）。丸め結果が負になる場合は0が下限 ---

	[TestMethod]
	public void Round_NegativeValue_Nearest_AtMidpoint_RoundsAwayFromZero_ThenClampedToZero() =>
		// -5円 を10円単位で四捨五入すると-0.5→AwayFromZeroで-1(単位)=-10円 だが、共通下限で0に丸める。
		Assert.AreEqual(0, JodaiPriceRule.ApplyRound(-5m, roundUnit: 1, roundType: 1));

	[TestMethod]
	public void Round_NegativeValue_Floor_ClampedToZero() =>
		Assert.AreEqual(0, JodaiPriceRule.ApplyRound(-100m, roundUnit: 1, roundType: 0));

	[TestMethod]
	public void Round_NegativeValue_Ceiling_ClampedToZero() =>
		Assert.AreEqual(0, JodaiPriceRule.ApplyRound(-5m, roundUnit: 1, roundType: 2));

	// ============================================================
	// 方式5 価格ポイント
	// ============================================================

	[TestMethod]
	public void SnapToPricePoint_EquidistantValues_PicksHigherOne() {
		// 6900と7900のちょうど中間(7400)は、値引きの取りすぎを避けるため上(高い方)を採る。
		var points = new[] { 5900, 6900, 7900, 8900 };
		Assert.AreEqual(7900, JodaiPriceRule.SnapToPricePoint(7400, points));
	}

	[TestMethod]
	public void SnapToPricePoint_EmptyTable_ReturnsValueAsIs() {
		Assert.AreEqual(7680, JodaiPriceRule.SnapToPricePoint(7680, Array.Empty<int>()));
	}

	[TestMethod]
	public void SnapToPricePoint_BelowMinimum_SnapsToMinimum() {
		var points = new[] { 5900, 6900, 7900 };
		Assert.AreEqual(5900, JodaiPriceRule.SnapToPricePoint(100, points));
	}

	[TestMethod]
	public void SnapToPricePoint_AboveMaximum_SnapsToMaximum() {
		var points = new[] { 5900, 6900, 7900 };
		Assert.AreEqual(7900, JodaiPriceRule.SnapToPricePoint(99999, points));
	}

	[TestMethod]
	public void SnapToPricePoint_UnsortedTable_StillFindsNearest() {
		var points = new[] { 8900, 5900, 7900, 6900 };
		Assert.AreEqual(6900, JodaiPriceRule.SnapToPricePoint(6800, points));
	}

	// ============================================================
	// ParsePricePoints 異常系
	// ============================================================

	[TestMethod]
	public void ParsePricePoints_EmptyString_ReturnsEmpty() {
		var actual = JodaiPriceRule.ParsePricePoints("");
		Assert.AreEqual(0, actual.Count);
	}

	[TestMethod]
	public void ParsePricePoints_Null_ReturnsEmpty() {
		var actual = JodaiPriceRule.ParsePricePoints(null);
		Assert.AreEqual(0, actual.Count);
	}

	[TestMethod]
	public void ParsePricePoints_SkipsEmptyAndWhitespaceElements() {
		var actual = JodaiPriceRule.ParsePricePoints("5900,,  ,6900");
		CollectionAssert.AreEqual(new List<int> { 5900, 6900 }, new List<int>(actual));
	}

	[TestMethod]
	public void ParsePricePoints_SkipsNonNumericElements() {
		var actual = JodaiPriceRule.ParsePricePoints("5900,abc,6900,90円");
		CollectionAssert.AreEqual(new List<int> { 5900, 6900 }, new List<int>(actual));
	}

	[TestMethod]
	public void ParsePricePoints_OutOfOrderInput_ReturnsAscending() {
		var actual = JodaiPriceRule.ParsePricePoints("7900,5900,6900");
		CollectionAssert.AreEqual(new List<int> { 5900, 6900, 7900 }, new List<int>(actual));
	}

	[TestMethod]
	public void ParsePricePoints_TrimsWhitespaceAroundElements() {
		var actual = JodaiPriceRule.ParsePricePoints(" 5900 , 6900 ");
		CollectionAssert.AreEqual(new List<int> { 5900, 6900 }, new List<int>(actual));
	}

	// ============================================================
	// 設計書2.7の実例: 算出値7,680円 → 7,900円（ParsePricePoints経由）
	// ============================================================

	[TestMethod]
	public void DesignDocExample_7680YenSnapsTo7900Yen_ViaParsedCsv() {
		var points = JodaiPriceRule.ParsePricePoints("5900,6900,7900,8900,9900");
		var actual = JodaiPriceRule.SnapToPricePoint(7680, points);
		Assert.AreEqual(7900, actual);
	}
}
