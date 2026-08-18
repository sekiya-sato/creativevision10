using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 出荷指示確定(商品)。旧CV.netの「出荷指示確定」を商品基準で並べたもの。
/// 商品×色サイズを主軸にまとめて確定・取消する。処理内容は
/// <see cref="Helpers.BaseShippingConfirmViewModel"/> と共通で、並び順だけが異なる。
/// </summary>
public sealed class ShippingConfirmShohinViewModel : Helpers.BaseShippingConfirmViewModel {
	protected override string QueryTitle => "出荷指示確定(商品)";
	protected override string SortOrderSql =>
		"h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id_Tenpo, h.Id";
}
