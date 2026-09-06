using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Tests.CvServer;

/// <summary>
/// <see cref="CostPreviewDisplay"/>（原価4画面の確認一覧が共通で使う表示整形・判定ロジック）。
/// 仕様は `Doc/spec/2026-09-05_原価4項目_詳細設計.md` の §8.1・§8.3 を参照する。
/// <para>
/// 本来はViewModel（`CvWpfclient`）に置く内容だが、`Tests/TestServer` から `CvWpfclient`
/// （WPFプロジェクト）を参照できないため（<c>TaxRateDuplicateTests</c>と同じ理由）、
/// 整形対象のenumと同じ `CvBase` へ切り出してある。
/// </para>
/// </summary>
[TestClass]
public class CostPreviewDisplayTests {
	[TestMethod]
	public void FormatRateBasisPoints_6500は65パーセント表示になる() {
		Assert.AreEqual("65.00%", CostPreviewDisplay.FormatRateBasisPoints(6500));
	}

	[TestMethod]
	public void FormatRateBasisPoints_1は0点01パーセント表示になる() {
		Assert.AreEqual("0.01%", CostPreviewDisplay.FormatRateBasisPoints(1));
	}

	[TestMethod]
	public void FormatRateBasisPoints_0は0パーセント表示になる() {
		Assert.AreEqual("0.00%", CostPreviewDisplay.FormatRateBasisPoints(0));
	}

	[TestMethod]
	public void IsErrorRow_エラーコードが0でメッセージも空なら正常行() {
		Assert.IsFalse(CostPreviewDisplay.IsErrorRow(EnumCostCalcError.None, string.Empty));
		Assert.IsFalse(CostPreviewDisplay.IsErrorRow(EnumCostCalcError.None, null));
	}

	[TestMethod]
	public void IsErrorRow_エラーコードが0でもメッセージがあればエラー行() {
		// CostUpdateDbConsumption.NewErrorRow のように、Error=None(0)のままErrorMessageだけ
		// 設定される行がある(例:「商品ID=0の明細は消化仕入対象にできません。」)。
		Assert.IsTrue(CostPreviewDisplay.IsErrorRow(EnumCostCalcError.None, "商品ID=0の明細は消化仕入対象にできません。"));
	}

	[TestMethod]
	public void IsErrorRow_エラーコードが非0ならエラー行() {
		Assert.IsTrue(CostPreviewDisplay.IsErrorRow((EnumCostCalcError)5, string.Empty));
	}

	[TestMethod]
	public void FormatRowStatus_エラー行はエラー正常行は正常() {
		Assert.AreEqual("エラー", CostPreviewDisplay.FormatRowStatus((EnumCostCalcError)1, "エラーです"));
		Assert.AreEqual("正常", CostPreviewDisplay.FormatRowStatus(EnumCostCalcError.None, string.Empty));
	}

	[TestMethod]
	public void FormatConsumptionSourceType_0は卸売上_1は店舗売上() {
		Assert.AreEqual("卸売上", CostPreviewDisplay.FormatConsumptionSourceType((EnumConsumptionSourceType)0));
		Assert.AreEqual("店舗売上", CostPreviewDisplay.FormatConsumptionSourceType((EnumConsumptionSourceType)1));
	}

	[TestMethod]
	public void FormatConsumptionCalcType_0は原価代用_1は上代掛率() {
		Assert.AreEqual("原価代用", CostPreviewDisplay.FormatConsumptionCalcType((EnumConsumptionCalcType)0));
		Assert.AreEqual("上代×掛率", CostPreviewDisplay.FormatConsumptionCalcType((EnumConsumptionCalcType)1));
	}

	[TestMethod]
	public void FormatCostProcessStatus_0から3までの状態文言() {
		Assert.AreEqual("未実行", CostPreviewDisplay.FormatCostProcessStatus((EnumCostProcessStatus)0));
		Assert.AreEqual("完了", CostPreviewDisplay.FormatCostProcessStatus((EnumCostProcessStatus)1));
		Assert.AreEqual("再実行要", CostPreviewDisplay.FormatCostProcessStatus((EnumCostProcessStatus)2));
		Assert.AreEqual("エラー", CostPreviewDisplay.FormatCostProcessStatus((EnumCostProcessStatus)3));
	}

	[TestMethod]
	public void FormatYmd8ToSlash_yyyyMMddをスラッシュ区切りへ変換する() {
		Assert.AreEqual("2026/09/06", CostPreviewDisplay.FormatYmd8ToSlash("20260906"));
	}

	[TestMethod]
	public void FormatYmd8ToSlash_不正な値はそのまま返す() {
		Assert.AreEqual("invalid", CostPreviewDisplay.FormatYmd8ToSlash("invalid"));
	}

	[TestMethod]
	public void FormatYm6ToSlash_yyyyMMをスラッシュ区切りへ変換する() {
		Assert.AreEqual("2026/09", CostPreviewDisplay.FormatYm6ToSlash("202609"));
	}

	[TestMethod]
	public void IsCostMethodMismatchOnly_不一致1件だけならtrue() {
		var rows = new List<CostPreviewRow> {
			new() { Error = EnumCostCalcError.CostMethodMismatch, ErrorMessage = "現在の原価方式(固定原価)では最終仕入原価更新を実行できません。" },
		};
		Assert.IsTrue(CostPreviewDisplay.IsCostMethodMismatchOnly(rows));
	}

	[TestMethod]
	public void IsCostMethodMismatchOnly_0件ならfalse() {
		Assert.IsFalse(CostPreviewDisplay.IsCostMethodMismatchOnly([]));
	}

	[TestMethod]
	public void IsCostMethodMismatchOnly_不一致行に加えて通常行があればfalse() {
		var rows = new List<CostPreviewRow> {
			new() { Error = EnumCostCalcError.CostMethodMismatch, ErrorMessage = "現在の原価方式では実行できません。" },
			new() { Id_Shohin = 1, Error = EnumCostCalcError.None },
		};
		Assert.IsFalse(CostPreviewDisplay.IsCostMethodMismatchOnly(rows));
	}

	[TestMethod]
	public void IsCostMethodMismatchOnly_1件でも不一致以外のエラーならfalse() {
		var rows = new List<CostPreviewRow> {
			new() { Id_Shohin = 1, Error = EnumCostCalcError.NonPositiveAfterCost },
		};
		Assert.IsFalse(CostPreviewDisplay.IsCostMethodMismatchOnly(rows));
	}

	[TestMethod]
	public void FormatSundryCheckSeverity_情報警告エラーの3区分() {
		Assert.AreEqual("情報", CostPreviewDisplay.FormatSundryCheckSeverity(EnumSundryCheckSeverity.Info));
		Assert.AreEqual("警告", CostPreviewDisplay.FormatSundryCheckSeverity(EnumSundryCheckSeverity.Warning));
		Assert.AreEqual("エラー", CostPreviewDisplay.FormatSundryCheckSeverity(EnumSundryCheckSeverity.Error));
	}

	[TestMethod]
	public void FormatSundryCheckSeverity_未定義値は不明表示() {
		Assert.AreEqual("不明(99)", CostPreviewDisplay.FormatSundryCheckSeverity((EnumSundryCheckSeverity)99));
	}

	[TestMethod]
	public void FormatShiireKubun_10仕入20仕入返品30値引99その他() {
		Assert.AreEqual("仕入", CostPreviewDisplay.FormatShiireKubun(10));
		Assert.AreEqual("仕入返品", CostPreviewDisplay.FormatShiireKubun(20));
		Assert.AreEqual("値引", CostPreviewDisplay.FormatShiireKubun(30));
		Assert.AreEqual("その他", CostPreviewDisplay.FormatShiireKubun(99));
	}

	[TestMethod]
	public void FormatShiireKubun_未定義値は不明表示() {
		Assert.AreEqual("不明(0)", CostPreviewDisplay.FormatShiireKubun(0));
	}
}
