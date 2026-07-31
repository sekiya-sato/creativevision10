namespace CvWpfclient.ViewModels._40Shop;

/// <summary>
/// 汎用在庫表（原価無）。店舗へ配布する版で、原価単価と原価金額を出さない。
/// 上代金額は売価なので残す（店舗でも参照して差し支えない）。
///
/// 抽出条件・SQL・集計は汎用在庫表(08Zaiko)と同一。ShowCost を false にして原価列を落とす。
/// </summary>
public partial class GeneralInventoryTableCostlessViewModel : _08Zaiko.GeneralStockTableViewModel {
	protected override string ReportTitle => "汎用在庫表(原価無)";
	protected override string FormFileName => "GeneralInventoryTableCostless.qfm";
	protected override bool ShowCost => false;
}
