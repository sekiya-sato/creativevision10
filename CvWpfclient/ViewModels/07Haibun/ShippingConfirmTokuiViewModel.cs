using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 出荷指示確定(得意先)。旧CV.netの「出荷指示確定」を得意先(出荷先)基準で並べたもの。
/// 出荷先ごとに配分を確認して確定・取消する。処理内容は
/// <see cref="Helpers.BaseShippingConfirmViewModel"/> と共通で、並び順だけが異なる。
/// </summary>
public sealed class ShippingConfirmTokuiViewModel : Helpers.BaseShippingConfirmViewModel {
	protected override string QueryTitle => "出荷指示確定(得意先)";
	protected override string SortOrderSql =>
		"h.Id_Tenpo, h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id";
}
