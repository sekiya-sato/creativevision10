using CvBase;
using CvBase.Share;
using System.Globalization;

namespace CvDomainLogic;

/// <summary>
/// 伝票日付時点の消費税率を解決する。
/// <para>
/// クライアント側の <c>AppGlobal.LogicGetTax</c> と同じ判定をサーバ側で行うための共通処理。
/// 仕様は `Doc/spec/2026-08-25_明細別消費税計算_詳細設計.md` の 4.4 を参照する。
/// </para>
/// </summary>
public static class TaxRateResolver {

	/// <summary>消費税率が取得できなかった場合の既定値(%)</summary>
	public const int DefaultTaxRatePercent = 10;

	/// <summary>
	/// 伝票日付時点の消費税率(%)を返す。<c>Rate</c> は掛率に使うのでここでは触らない。
	/// </summary>
	/// <param name="sysman">システム設定（<see cref="MasterSysman.Jsub"/> に税率定義を持つ）</param>
	/// <param name="taxId">消費税区分（<see cref="MasterSysTax.Id"/> 1-3）。0以下は非課税で 0 を返す</param>
	/// <param name="denDay">伝票日付(yyyyMMdd)</param>
	public static int ResolveTaxRatePercent(MasterSysman? sysman, long taxId, string? denDay) {
		// 非課税。MasterSysTax を引かずにここで確定させる
		// (Id=0 の定義は存在せず、既定値の DateFrom が空のまま CompareYmd に渡ると例外になる)
		if (taxId <= 0) {
			return 0;
		}
		var systax = sysman?.Jsub?.FirstOrDefault(x => x.Id == taxId);
		if (systax == null) {
			return DefaultTaxRatePercent;
		}
		var rate = systax.TaxRate;
		// DateFrom が未設定なら新税率への切替日が無いということなので現行税率を使う。
		// CvAsset.Common.CompareYmd は 8桁以外で例外を投げるため、渡す前に桁数を確認する
		if (IsValidYmd(denDay) && IsValidYmd(systax.DateFrom)
			&& CvAsset.Common.CompareYmd(denDay!, systax.DateFrom) >= 0) {
			rate = systax.TaxNewRate;
		}
		return rate > 0 ? rate : DefaultTaxRatePercent;
	}

	/// <summary>
	/// 明細の税額を計算する。常に正値を返し、返品等の符号はヘッダ Kubun の CalcFlag が決める。
	/// </summary>
	/// <param name="kingaku">明細金額</param>
	/// <param name="taxRatePercent">適用消費税率(%)</param>
	public static int CalcMeisaiTax(int kingaku, int taxRatePercent) =>
		(int)TranCalcBase.RoundTax(Math.Abs(kingaku), taxRatePercent, EnumRounding.Round);

	/// <summary>8桁yyyyMMddとして妥当か</summary>
	public static bool IsValidYmd(string? ymd) =>
		!string.IsNullOrWhiteSpace(ymd)
		&& ymd.Length == 8
		&& DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
