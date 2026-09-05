using CvBase.Share;

namespace CvBase;

/// <summary>
/// 原価4項目（最終仕入原価・総平均原価・消化仕入・評価替え）の原価計算純ロジック。
/// 正典は `Doc/spec/2026-09-05_原価4項目_詳細設計.md`。
/// <para>
/// 本クラスはDB・NPocoに依存しない純関数のみで構成する。DBアクセスを伴う計算（消化仕入の
/// 計算区分0「原価代用」で `ResolveCostAsOf` を呼ぶ経路など）は Step 4/5 の `CostUpdateDb`（設計書§9.2）の
/// 責務であり、本クラスには含めない。
/// </para>
/// </summary>
public static class CostCalculator {
	/// <summary>1/10/100/1000円のいずれかの端数単位のみ許容する。</summary>
	private static readonly int[] ValidRoundingUnits = [1, 10, 100, 1000];

	/// <summary>
	/// 端数単位で丸める。中間計算は <see cref="decimal"/> で行う（設計書§2.2）。
	/// <para>
	/// <paramref name="unit"/> は消化仕入の `ConsumptionRoundingUnit`（1/10/100/1000円、設計書§2.5.8）と
	/// 評価替えの `RoundingUnit`（1/10/100円、設計書§16.4）のいずれの呼び出しにも対応するため、
	/// 1/10/100/1000のみ許容する。それ以外は <see cref="ArgumentOutOfRangeException"/>。
	/// </para>
	/// <para>
	/// 負値でも「切上＝より大きい方へ」「切捨＝より小さい方へ」という数学的な定義のまま実装し、
	/// 絶対値基準（切上＝絶対値が大きい方へ）にはしない。ただし本機能で扱う金額・単価は
	/// 負値では来ない想定であり、この規約は主に対称性のためのものである。
	/// </para>
	/// </summary>
	public static long RoundToUnit(decimal raw, int unit, EnumRounding rounding) {
		if (Array.IndexOf(ValidRoundingUnits, unit) < 0) {
			throw new ArgumentOutOfRangeException(nameof(unit), unit, "端数単位は1、10、100、1000のいずれかで指定してください。");
		}

		var scaled = raw / unit;
		var roundedScaled = rounding switch {
			EnumRounding.Round => Math.Round(scaled, 0, MidpointRounding.AwayFromZero),
			EnumRounding.Ceiling => Math.Ceiling(scaled),
			EnumRounding.Floor => Math.Floor(scaled),
			_ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "未定義の端数処理です。"),
		};
		return (long)(roundedScaled * unit);
	}

	/// <summary>最終仕入原価・消化仕入（計算区分1）・評価替えの計算結果。</summary>
	public readonly record struct CostCalcResult(long AfterCost, EnumCostCalcError Error) {
		/// <summary>エラーが発生しているか。</summary>
		public bool IsError => Error != EnumCostCalcError.None;

		/// <summary>エラー結果を作る。</summary>
		public static CostCalcResult Fail(EnumCostCalcError error) => new(0, error);
	}

	/// <summary>
	/// 最終仕入原価（設計書§5.3）。<c>AfterCost = round_away_from_zero(明細.Kingaku / 明細.Su)</c>。
	/// <para>
	/// 丸め方向を最終仕入だけ「最も近い整数へ丸める（0.5は0から遠い方、
	/// <see cref="MidpointRounding.AwayFromZero"/>）」にする理由（設計書§5.3）: 最終仕入原価は
	/// 「対象期間で最後の1明細の単価」を割り戻す計算であり、`floor` にすると諸掛が無い場合でも
	/// 明細単価より1円低い原価になりうる（例 `11,490/30=383.0` は一致するが `11,489/30=382.97` は
	/// `floor` だと382になり、明細単価383と食い違う）。「対象期間内で最後の通常仕入を原価にする」
	/// という維持仕様（設計書§1.3）を満たすため、最も近い整数へ丸める。
	/// 総平均原価（<see cref="CalcTotalAverageCost"/>）が `floor` なのは、平均の総額が
	/// 原資を超えないことを優先するためであり、2処理で丸め方向が異なるのは意図的である（設計書§5.3）。
	/// </para>
	/// <para><b>諸掛は加算しない</b>（設計書§3.6、§13 U-24）。</para>
	/// </summary>
	public static CostCalcResult CalcLastPurchaseCost(long kingaku, long su) {
		if (su <= 0) {
			return CostCalcResult.Fail(EnumCostCalcError.NonPositiveDenominator);
		}

		var raw = (decimal)kingaku / su;
		var afterCost = (long)Math.Round(raw, 0, MidpointRounding.AwayFromZero);
		if (afterCost <= 0) {
			return CostCalcResult.Fail(EnumCostCalcError.NonPositiveAfterCost);
		}

		return new CostCalcResult(afterCost, EnumCostCalcError.None);
	}

	/// <summary>総平均原価計算の入力（設計書§6.3・§6.4）。</summary>
	/// <param name="OpeningQty">前月在庫数。</param>
	/// <param name="OpeningAmount">前月在庫金額（<c>OpeningQty × BeforeCost</c>、呼び出し側で算出）。</param>
	/// <param name="PurchaseQty">対象期間の在庫加算仕入数（仕入返品は負）。</param>
	/// <param name="PurchaseAmount">
	/// 対象期間の在庫加算仕入金額（仕入返品は負）。<b>諸掛を含まない</b>仕入金額のみを渡すこと。
	/// 設計書§6.3の式は `PurchaseAmount` に諸掛を含めて書いているが、本関数の引数としては
	/// 仕入金額と諸掛金額を分離し、<see cref="SundryAmount"/> は本関数側で分子へ加算する。
	/// </param>
	/// <param name="SundryAmount">対象計上月に算入する諸掛額（設計書§3.5の集計値）。</param>
	public readonly record struct TotalAverageInput(long OpeningQty, long OpeningAmount, long PurchaseQty, long PurchaseAmount, long SundryAmount);

	/// <summary>総平均原価計算の結果。</summary>
	public readonly record struct TotalAverageResult(long AfterCost, long Denominator, long Numerator, EnumCostCalcError Error) {
		/// <summary>エラーが発生しているか。</summary>
		public bool IsError => Error != EnumCostCalcError.None;
	}

	/// <summary>
	/// 総平均原価（設計書§6.3・§6.4・§6.5）。
	/// <c>Denominator = OpeningQty + PurchaseQty</c>、
	/// <c>Numerator = OpeningAmount + PurchaseAmount + SundryAmount</c>、
	/// <c>AfterCost = floor(Numerator / Denominator)</c>。
	/// <para>
	/// 除算はDBの整数除算に委ねず、<see cref="decimal"/> で除算してから <see cref="Math.Floor(decimal)"/> する
	/// （設計書§6.4、§11.2）。`TQ` は加算しない（設計書§6.4、チェックリストD-06）。
	/// </para>
	/// <para>
	/// 「当月仕入なし → 対象外。前原価を維持」（設計書§6.5）は本関数の対象抽出条件（§6.1）の話であり、
	/// 呼び出し側が対象商品を抽出する段階で行う判断である。本関数はすでに抽出された商品1件分の
	/// 入力を受け取って計算するだけであり、対象外判定そのものは責務外とする。
	/// </para>
	/// <para>
	/// 判定順は設計書§6.5の表のとおり。
	/// </para>
	/// </summary>
	public static TotalAverageResult CalcTotalAverageCost(TotalAverageInput input, long beforeCost) {
		if (input.OpeningQty < 0) {
			return new TotalAverageResult(0, 0, 0, EnumCostCalcError.NegativeOpeningQty);
		}

		if (input.OpeningQty > 0 && beforeCost <= 0) {
			return new TotalAverageResult(0, 0, 0, EnumCostCalcError.NonPositiveBeforeCost);
		}

		if (input.PurchaseQty == 0 && input.PurchaseAmount == 0 && input.SundryAmount != 0) {
			// 当月仕入が無く諸掛だけがある(設計書§6.5「諸掛の対象商品に当月仕入も前月在庫も無い」等)。
			return new TotalAverageResult(0, 0, 0, EnumCostCalcError.SundryOnlyWithoutBase);
		}

		if (input.PurchaseQty == 0 && input.PurchaseAmount != 0) {
			// 当月仕入額はあるが数量0(設計書§6.5「当月仕入額はあるが数量0」)。
			return new TotalAverageResult(0, 0, 0, EnumCostCalcError.PurchaseAmountWithoutQty);
		}

		var denominator = input.OpeningQty + input.PurchaseQty;
		if (denominator <= 0) {
			return new TotalAverageResult(0, denominator, 0, EnumCostCalcError.NonPositiveDenominator);
		}

		var numerator = input.OpeningAmount + input.PurchaseAmount + input.SundryAmount;
		if (numerator <= 0) {
			return new TotalAverageResult(0, denominator, numerator, EnumCostCalcError.NonPositiveNumerator);
		}

		var afterCost = (long)Math.Floor((decimal)numerator / denominator);
		if (afterCost <= 0) {
			return new TotalAverageResult(afterCost, denominator, numerator, EnumCostCalcError.NonPositiveAfterCost);
		}

		return new TotalAverageResult(afterCost, denominator, numerator, EnumCostCalcError.None);
	}

	/// <summary>
	/// 消化仕入の生成単価（計算区分1「上代×掛率」、設計書§4.4）。
	/// <c>raw = 売上明細.Tanka × ConsumptionRateBasisPoints / 10000</c> を <see cref="RoundToUnit"/> で丸める。
	/// <para>
	/// 計算区分0「原価代用」（<c>TankaShiire &gt; 0 ? TankaShiire : ResolveCostAsOf(...)</c>）は
	/// `ResolveCostAsOf` がDB参照を伴うため、この純ロジックには含めない。Step 4/5の `CostUpdateDb` の責務とする。
	/// </para>
	/// </summary>
	public static CostCalcResult CalcConsumptionUnitCostByRate(long uriageTanka, int rateBasisPoints, int roundingUnit, EnumRounding rounding) {
		if (rateBasisPoints is < 1 or > 10000) {
			return CostCalcResult.Fail(EnumCostCalcError.InvalidRate);
		}

		var raw = uriageTanka * rateBasisPoints / 10000m;
		var afterCost = RoundToUnit(raw, roundingUnit, rounding);
		if (afterCost <= 0) {
			return CostCalcResult.Fail(EnumCostCalcError.InvalidUnitPrice);
		}

		return new CostCalcResult(afterCost, EnumCostCalcError.None);
	}

	/// <summary>
	/// 評価替え・方式1「率一括」（設計書§16.5）。<c>raw = beforeCost × ratePercent / 100</c>。
	/// <paramref name="ratePercent"/> は<b>掛率</b>（引下げ率(OFF率)ではない、§13 U-18）で1～100の範囲。
	/// </summary>
	public static CostCalcResult CalcRevalCostByRate(long beforeCost, int ratePercent, int roundingUnit, EnumRounding rounding) {
		if (ratePercent is < 1 or > 100) {
			return CostCalcResult.Fail(EnumCostCalcError.InvalidRate);
		}

		var raw = beforeCost * ratePercent / 100m;
		var afterCost = RoundToUnit(raw, roundingUnit, rounding);
		if (afterCost <= 0) {
			return CostCalcResult.Fail(EnumCostCalcError.NonPositiveAfterCost);
		}

		return new CostCalcResult(afterCost, EnumCostCalcError.None);
	}

	/// <summary>
	/// 評価替え・方式2「金額一括」（設計書§16.5）。<c>raw = fixedCost</c>（単価そのものの指定）を
	/// <see cref="RoundToUnit"/> で丸める。
	/// </summary>
	public static CostCalcResult CalcRevalCostByFixed(long beforeCost, long fixedCost, int roundingUnit, EnumRounding rounding) {
		var afterCost = RoundToUnit(fixedCost, roundingUnit, rounding);
		if (afterCost <= 0) {
			return CostCalcResult.Fail(EnumCostCalcError.NonPositiveAfterCost);
		}

		return new CostCalcResult(afterCost, EnumCostCalcError.None);
	}

	/// <summary>
	/// 評価替えの対象判定（設計書§16.5）。<c>AfterCost &gt;= BeforeCost</c> は計算エラーではなく
	/// 「対象外」であり、この違いを本メソッドで明示する。エラー（<see cref="EnumCostCalcError"/>）は
	/// 計算そのものが成立しない場合に限り、対象外は計算が成立したうえで評価減にならなかった場合を指す。
	/// </summary>
	public static bool IsRevalTarget(long beforeCost, long afterCost) =>
		beforeCost > 0 && afterCost > 0 && afterCost < beforeCost;

	/// <summary>
	/// 最終仕入明細を決定するための比較キー（設計書§5.2）。
	/// <c>DenDay</c>（yyyyMMdd文字列の序数比較）→ <c>ShiireId</c> → <c>MeisaiNo</c> の昇順で比較し、
	/// 最大のキーを持つ明細が「対象期間で最後の1明細」になる。同日複数仕入でも一意に決定できる。
	/// </summary>
	public readonly record struct LastPurchaseKey(string DenDay, long ShiireId, int MeisaiNo) : IComparable<LastPurchaseKey> {
		public int CompareTo(LastPurchaseKey other) {
			var dayCompare = string.CompareOrdinal(DenDay, other.DenDay);
			if (dayCompare != 0) {
				return dayCompare;
			}

			var shiireCompare = ShiireId.CompareTo(other.ShiireId);
			if (shiireCompare != 0) {
				return shiireCompare;
			}

			return MeisaiNo.CompareTo(other.MeisaiNo);
		}
	}
}

/// <summary>
/// 原価4項目の計算純ロジックが返すエラー種別。DB保存を伴わないため
/// <c>CvBase.Share.BaseEnumClass</c> ではなく <see cref="CostCalculator"/> と同じファイルに定義する。
/// 各値の根拠は原価4項目 詳細設計書の該当節を参照する。
/// </summary>
public enum EnumCostCalcError : int {
	/// <summary>エラーなし。</summary>
	None = 0,
	/// <summary>前月在庫数が負(設計書§6.5「OpeningQty &lt; 0」)。負在庫を総平均の母数にしない。</summary>
	NegativeOpeningQty,
	/// <summary>前月在庫があるのに計算前原価が0以下(設計書§6.5「OpeningQty &gt; 0、BeforeCost &lt;= 0」)。</summary>
	NonPositiveBeforeCost,
	/// <summary>分母(数量の合計)が0以下(設計書§5.3の`su&lt;=0`、§6.5「Denominator &lt;= 0」)。</summary>
	NonPositiveDenominator,
	/// <summary>分子(金額の合計)が0以下(設計書§6.5「Numerator &lt;= 0」)。</summary>
	NonPositiveNumerator,
	/// <summary>計算後原価が0以下(設計書§2.2、§5.3、§6.5「AfterCost &lt;= 0」、§16.5)。</summary>
	NonPositiveAfterCost,
	/// <summary>対象期間内に対象となる仕入が無い。</summary>
	NoPurchaseInPeriod,
	/// <summary>当月仕入額はあるが数量0(設計書§6.5「当月仕入額はあるが数量0」)。</summary>
	PurchaseAmountWithoutQty,
	/// <summary>当月仕入が無く諸掛だけがある(設計書§6.5「当月仕入が無く諸掛だけがある」、§3.8)。</summary>
	SundryOnlyWithoutBase,
	/// <summary>消化仕入の計算単価が0以下または上限超過(設計書§4.8「計算単価が0以下」)。</summary>
	InvalidUnitPrice,
	/// <summary>端数単位が1/10/100/1000のいずれでもない。</summary>
	InvalidRoundingUnit,
	/// <summary>掛率が範囲外(消化仕入は設計書§2.5.8の1～10000、評価替えは§13 U-18・§16.5の1～100)。</summary>
	InvalidRate,
}
