using CvBase;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分確定(得意先)。配分を得意先(出荷先)基準で並べ、確定数を入れて確定し伝票を作る（旧CV.netの「出荷指示確定」＋「出荷処理」）。
/// 出荷先ごとに配分を確認して確定する。処理内容は
/// <see cref="Helpers.BaseShippingConfirmViewModel"/> と共通で、並び順だけが異なる。
/// </summary>
public sealed class ShippingConfirmTokuiViewModel : Helpers.BaseShippingConfirmViewModel {
	protected override string QueryTitle => "配分確定(得意先)";
	protected override string SortOrderSql =>
		"h.Id_Tenpo, h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id";
}
