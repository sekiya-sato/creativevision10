using CvBase.Share;

namespace CvBase;

/// <summary>
/// 消費税計算の対象となる明細1行。<see cref="Tran99Meisai"/> / <see cref="Tran99MaterialMeisai"/> が実装する。
/// <para>
/// 両クラスは <c>Kingaku</c>/<c>Tax</c> の型が int / long で異なるため（<see cref="Tran99Meisai"/> は int、
/// <see cref="Tran99MaterialMeisai"/> は long）、この interface で long へ吸収する。実装は明示的インターフェース実装で行う。
/// </para>
/// </summary>
public interface ITaxMeisaiLine {
	/// <summary>明細金額。丸めは絶対値で行う</summary>
	long TaxKingaku { get; }
	/// <summary>消費税区分(MasterSysTax.Id 1-3、0=非課税)</summary>
	long TaxId { get; set; }
	/// <summary>適用消費税率(%)</summary>
	int TaxRatePercent { get; set; }
	/// <summary>明細消費税額。常に正値</summary>
	long TaxAmount { get; set; }
}

/// <summary>
/// <see cref="Tran99Meisai"/> の <see cref="ITaxMeisaiLine"/> 実装。
/// <c>Kingaku</c>/<c>Tax</c> ともに long のため、そのまま委譲する。
/// </summary>
public sealed partial class Tran99Meisai : ITaxMeisaiLine {
	long ITaxMeisaiLine.TaxKingaku => Kingaku;
	long ITaxMeisaiLine.TaxId {
		get => Id_Tax;
		set => Id_Tax = value;
	}
	int ITaxMeisaiLine.TaxRatePercent {
		get => TaxRate;
		set => TaxRate = value;
	}
	long ITaxMeisaiLine.TaxAmount {
		get => Tax;
		set => Tax = value;
	}
}

/// <summary>
/// <see cref="Tran99MaterialMeisai"/> の <see cref="ITaxMeisaiLine"/> 実装。
/// <c>Kingaku</c>/<c>Tax</c> が既に long のため、そのまま委譲する。
/// </summary>
public sealed partial class Tran99MaterialMeisai : ITaxMeisaiLine {
	long ITaxMeisaiLine.TaxKingaku => Kingaku;
	long ITaxMeisaiLine.TaxId {
		get => Id_Tax;
		set => Id_Tax = value;
	}
	int ITaxMeisaiLine.TaxRatePercent {
		get => TaxRate;
		set => TaxRate = value;
	}
	long ITaxMeisaiLine.TaxAmount {
		get => Tax;
		set => Tax = value;
	}
}

/// <summary>
/// 消費税区分(1-3)ごとの集計結果。ヘッダ <c>TaxableAmount1/2/3</c>・<c>Tax1/2/3</c> へそのまま代入できる。
/// </summary>
/// <param name="TaxableAmount1">課税対象額1(Id_Tax=1)</param>
/// <param name="TaxableAmount2">課税対象額2(Id_Tax=2)</param>
/// <param name="TaxableAmount3">課税対象額3(Id_Tax=3)</param>
/// <param name="Tax1">消費税1(Id_Tax=1)</param>
/// <param name="Tax2">消費税2(Id_Tax=2)</param>
/// <param name="Tax3">消費税3(Id_Tax=3)</param>
public readonly record struct TaxTotals(
	long TaxableAmount1, long TaxableAmount2, long TaxableAmount3,
	long Tax1, long Tax2, long Tax3) {
	/// <summary>Tax1+Tax2+Tax3</summary>
	public long TaxTotal => Tax1 + Tax2 + Tax3;
	/// <summary>全項目0のゼロ値</summary>
	public static readonly TaxTotals Zero = new();
}

/// <summary>
/// 明細別消費税の共通計算処理。
/// <para>
/// 仕様は `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` の 3.1〜3.7 を参照する。
/// 伝票単位（<see cref="EnumTaxCalcUnit.Slip"/>）では税区分ごとに1回だけ丸めてヘッダへ確定させ、
/// 請求単位（<see cref="EnumTaxCalcUnit.Billing"/>）では課税対象額の集計のみ行い、税額は請求計算側で確定する。
/// </para>
/// </summary>
public static class TaxCalculator {
	/// <summary>商品マスタが引けない明細に使う既定の消費税区分</summary>
	public const long StandardTaxId = 1;

	/// <summary>
	/// 明細の適用税率・税額を確定し、ヘッダの TaxableAmount1/2/3・Tax1/2/3 へ代入できる合計を返す。
	/// </summary>
	/// <param name="meisai">対象明細。Id_Tax は呼び出し側で設定済みであること(内容を書き換える)</param>
	/// <param name="rateOf">消費税区分 → 適用税率(%) を返す。0以下の区分は呼ばれず税率0として扱う</param>
	/// <param name="calcUnit">税計算単位。Billing なら Tax は 0 のまま(請求計算で計算する)</param>
	/// <param name="rounding">端数処理</param>
	public static TaxTotals Apply(
		IEnumerable<ITaxMeisaiLine> meisai,
		Func<long, int> rateOf,
		EnumTaxCalcUnit calcUnit,
		EnumRounding rounding) {

		var lines = meisai as IList<ITaxMeisaiLine> ?? meisai.ToList();

		// 1. 明細ごとの適用税率を確定する
		foreach (var m in lines) {
			m.TaxRatePercent = m.TaxId <= 0 ? 0 : rateOf(m.TaxId);
		}

		// 2. 税区分(1-3)ごとの課税対象額。0または1-3以外は集計対象外
		long taxable1 = 0, taxable2 = 0, taxable3 = 0;
		foreach (var m in lines) {
			var amount = Math.Abs(m.TaxKingaku);
			switch (m.TaxId) {
				case 1: taxable1 += amount; break;
				case 2: taxable2 += amount; break;
				case 3: taxable3 += amount; break;
			}
		}

		if (calcUnit == EnumTaxCalcUnit.Billing) {
			// 請求単位: ヘッダTaxは0のまま。請求計算がTaxableAmountをSUMしてから1回だけ丸める
			foreach (var m in lines) {
				m.TaxAmount = 0;
			}
			return new TaxTotals(taxable1, taxable2, taxable3, 0, 0, 0);
		}

		// 3. 伝票単位: 税区分ごとに1回だけ丸める。課税対象額0の区分はrateOfを呼ばずに0とする
		long tax1 = taxable1 == 0 ? 0 : TranCalcBase.RoundTax(taxable1, rateOf(1), rounding);
		long tax2 = taxable2 == 0 ? 0 : TranCalcBase.RoundTax(taxable2, rateOf(2), rounding);
		long tax3 = taxable3 == 0 ? 0 : TranCalcBase.RoundTax(taxable3, rateOf(3), rounding);

		foreach (var m in lines) {
			m.TaxAmount = 0;
		}
		Apportion(lines, 1, taxable1, tax1);
		Apportion(lines, 2, taxable2, tax2);
		Apportion(lines, 3, taxable3, tax3);

		return new TaxTotals(taxable1, taxable2, taxable3, tax1, tax2, tax3);
	}

	/// <summary>
	/// 税区分ごとに1回だけ丸めた税額(<paramref name="taxTotal"/>)を、同じ税区分の明細へ課税対象額の比で按分する。
	/// 単純な比例配分は合計がずれるため、端数は課税対象額(絶対値)が最大の行へ寄せて必ず合計を一致させる。
	/// </summary>
	static void Apportion(IList<ITaxMeisaiLine> lines, long taxId, long taxableTotal, long taxTotal) {
		if (taxTotal == 0 || taxableTotal == 0) {
			return;
		}
		long allocated = 0;
		ITaxMeisaiLine? maxLine = null;
		long maxAbs = -1;
		foreach (var m in lines) {
			if (m.TaxId != taxId) {
				continue;
			}
			var abs = Math.Abs(m.TaxKingaku);
			var share = taxTotal * abs / taxableTotal;
			m.TaxAmount = share;
			allocated += share;
			if (abs > maxAbs) {
				maxAbs = abs;
				maxLine = m;
			}
		}
		var remainder = taxTotal - allocated;
		if (remainder != 0 && maxLine != null) {
			maxLine.TaxAmount += remainder;
		}
	}
}
