using CvBase;
using CvBase.Share;

namespace CvWpfpos.Services;

/// <summary>
/// 端末の税計算。サーバ（PointOfSaleService）と同じ <see cref="TaxCalculator.Apply"/> を、
/// 同じ税率定義（MasterSysman）・伝票単位・店舗の端数処理で呼び、表示・会計の税額を保存値と一致させる。
/// </summary>
public sealed class PosTaxCalculator(MasterSysman sysman, EnumRounding rounding) {
	/// <summary>
	/// 税率定義と店舗の端数処理をサーバから読む。
	/// </summary>
	public static async Task<PosTaxCalculator> LoadAsync(PosGrpcClient client, long storeId, CancellationToken cancellationToken) {
		var sysman = (await client.QueryListAsync<MasterSysman>("Id=@0", null, ["1"], 1, cancellationToken)).FirstOrDefault() ?? new MasterSysman();
		var store = (await client.QueryListAsync<MasterTokui>("Id=@0", null, [storeId.ToString()], 1, cancellationToken)).FirstOrDefault()
			?? throw new InvalidOperationException($"店舗が見つかりません: Id={storeId}");
		return new PosTaxCalculator(sysman, (EnumRounding)store.TaxRounding);
	}

	/// <summary>明細の税額を計算する（明細の TaxRate・Tax を書き換える）。</summary>
	/// <param name="lines">Id_Tax・Kingaku を設定済みの明細</param>
	/// <param name="denDay">伝票日付（yyyyMMdd）。税率の切替判定に使う</param>
	public TaxTotals Calc(IReadOnlyList<Tran99Meisai> lines, string denDay) =>
		TaxCalculator.Apply(lines, TaxRateResolver.CreateRateResolver(sysman, denDay), EnumTaxCalcUnit.Slip, rounding);
}
