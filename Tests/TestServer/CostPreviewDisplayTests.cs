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

	// ------------------------------------------------------------------
	// 評価替え（原価4項目 詳細設計 §16、§8.6）
	// ------------------------------------------------------------------

	[TestMethod]
	public void FormatCostRevaluationMethod_1は率一括2は金額一括() {
		Assert.AreEqual("率一括指定", CostPreviewDisplay.FormatCostRevaluationMethod(EnumCostRevaluationMethod.ByRate));
		Assert.AreEqual("金額一括指定", CostPreviewDisplay.FormatCostRevaluationMethod(EnumCostRevaluationMethod.ByFixed));
	}

	[TestMethod]
	public void FormatCostRevalApplyPoint_0は月末1は期末() {
		Assert.AreEqual("月末", CostPreviewDisplay.FormatCostRevalApplyPoint(EnumCostRevalApplyPoint.MonthEnd));
		Assert.AreEqual("期末", CostPreviewDisplay.FormatCostRevalApplyPoint(EnumCostRevalApplyPoint.FiscalEnd));
	}

	[TestMethod]
	public void FormatCostRevalGroupKey_5区分の表示文言() {
		Assert.AreEqual("ブランド", CostPreviewDisplay.FormatCostRevalGroupKey(EnumCostRevalGroupKey.Brand));
		Assert.AreEqual("アイテム", CostPreviewDisplay.FormatCostRevalGroupKey(EnumCostRevalGroupKey.Item));
		Assert.AreEqual("シーズン", CostPreviewDisplay.FormatCostRevalGroupKey(EnumCostRevalGroupKey.Season));
		Assert.AreEqual("メーカー", CostPreviewDisplay.FormatCostRevalGroupKey(EnumCostRevalGroupKey.Maker));
		Assert.AreEqual("展示会", CostPreviewDisplay.FormatCostRevalGroupKey(EnumCostRevalGroupKey.Tenji));
	}

	[TestMethod]
	public void FormatCostRevalCondField_年度を含まない9項目の表示文言() {
		Assert.AreEqual("商品CD", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.ShohinCode));
		Assert.AreEqual("メーカー品番", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.MakerCode));
		Assert.AreEqual("ブランド", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Brand));
		Assert.AreEqual("アイテム", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Item));
		Assert.AreEqual("メーカー", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Maker));
		Assert.AreEqual("シーズン", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Season));
		Assert.AreEqual("展示会", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Tenji));
		Assert.AreEqual("素材", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Material));
		Assert.AreEqual("原産国", CostPreviewDisplay.FormatCostRevalCondField(EnumCostRevalCondField.Country));
	}

	[TestMethod]
	public void FormatRounding_0は四捨五入1は切上2は切捨() {
		Assert.AreEqual("四捨五入", CostPreviewDisplay.FormatRounding(EnumRounding.Round));
		Assert.AreEqual("切上", CostPreviewDisplay.FormatRounding(EnumRounding.Ceiling));
		Assert.AreEqual("切捨", CostPreviewDisplay.FormatRounding(EnumRounding.Floor));
	}

	[TestMethod]
	public void FormatCostRevalStatus_0は有効1は取消() {
		Assert.AreEqual("有効", CostPreviewDisplay.FormatCostRevalStatus(EnumCostRevalStatus.Active));
		Assert.AreEqual("取消", CostPreviewDisplay.FormatCostRevalStatus(EnumCostRevalStatus.Canceled));
	}

	[TestMethod]
	public void FormatRevaluationRowStatus_エラー行は対象外より優先してエラー表示() {
		Assert.AreEqual("エラー", CostPreviewDisplay.FormatRevaluationRowStatus(true, (EnumCostCalcError)1, "後原価が0以下です。"));
		Assert.AreEqual("エラー", CostPreviewDisplay.FormatRevaluationRowStatus(false, (EnumCostCalcError)1, "後原価が0以下です。"));
	}

	[TestMethod]
	public void FormatRevaluationRowStatus_エラーでなければ対象対象外を区別する() {
		Assert.AreEqual("対象", CostPreviewDisplay.FormatRevaluationRowStatus(true, EnumCostCalcError.None, string.Empty));
		Assert.AreEqual("対象外", CostPreviewDisplay.FormatRevaluationRowStatus(false, EnumCostCalcError.None, string.Empty));
	}

	[TestMethod]
	public void FormatRevaluationRowReason_エラー行はエラーメッセージを表示する() {
		Assert.AreEqual("後原価が0以下です。",
			CostPreviewDisplay.FormatRevaluationRowReason(true, (EnumCostCalcError)1, "後原価が0以下です。", string.Empty));
	}

	[TestMethod]
	public void FormatRevaluationRowReason_対象外行は除外理由を表示する() {
		Assert.AreEqual("在庫0",
			CostPreviewDisplay.FormatRevaluationRowReason(false, EnumCostCalcError.None, string.Empty, "在庫0"));
	}

	[TestMethod]
	public void FormatRevaluationRowReason_対象行は空文字() {
		Assert.AreEqual(string.Empty,
			CostPreviewDisplay.FormatRevaluationRowReason(true, EnumCostCalcError.None, string.Empty, string.Empty));
	}

	[TestMethod]
	public void BuildRevaluationRateFormulaText_掛率を式へ埋め込む() {
		Assert.AreEqual("新原価 = 元原価 × 掛率70%（四捨五入等は指定した端数処理）",
			CostPreviewDisplay.BuildRevaluationRateFormulaText(70));
	}

	[TestMethod]
	public void ValidateRevaluationRatePercent_1から100は成功() {
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRatePercent(1));
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRatePercent(100));
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRatePercent(70));
	}

	[TestMethod]
	public void ValidateRevaluationRatePercent_範囲外はエラーメッセージ() {
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRatePercent(0));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRatePercent(101));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRatePercent(-1));
	}

	[TestMethod]
	public void ValidateRevaluationFixedCost_1以上は成功0以下はエラー() {
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationFixedCost(1));
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationFixedCost(1000));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationFixedCost(0));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationFixedCost(-1));
	}

	[TestMethod]
	public void ValidateRevaluationRoundingUnit_1_10_100は成功それ以外はエラー() {
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(1));
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(10));
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(100));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(0));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(5));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationRoundingUnit(1000));
	}

	[TestMethod]
	public void ValidateRevaluationMethodValue_率一括は率だけを検査する() {
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationMethodValue(EnumCostRevaluationMethod.ByRate, 70, 0));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationMethodValue(EnumCostRevaluationMethod.ByRate, 0, 1000));
	}

	[TestMethod]
	public void ValidateRevaluationMethodValue_金額一括は金額だけを検査する() {
		Assert.IsNull(CostPreviewDisplay.ValidateRevaluationMethodValue(EnumCostRevaluationMethod.ByFixed, 0, 1000));
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationMethodValue(EnumCostRevaluationMethod.ByFixed, 70, 0));
	}

	[TestMethod]
	public void ValidateRevaluationMethodValue_未定義方式はエラー() {
		Assert.IsNotNull(CostPreviewDisplay.ValidateRevaluationMethodValue((EnumCostRevaluationMethod)0, 70, 1000));
	}
}
