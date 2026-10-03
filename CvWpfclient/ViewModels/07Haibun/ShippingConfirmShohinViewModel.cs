using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分確定(商品)。配分を商品基準で並べ、確定数を入れて確定し伝票を作る（旧CV.netの「出荷指示確定」＋「出荷処理」）。
/// 商品×色サイズを主軸にまとめて確定する。処理内容は
/// <see cref="Helpers.BaseShippingConfirmViewModel"/> と共通で、並び順だけが異なる。
/// </summary>
public sealed class ShippingConfirmShohinViewModel : Helpers.BaseShippingConfirmViewModel {
	protected override string QueryTitle => "配分確定(商品)";
	protected override string SortOrderSql =>
		"h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id_Tenpo, h.Id";
}
