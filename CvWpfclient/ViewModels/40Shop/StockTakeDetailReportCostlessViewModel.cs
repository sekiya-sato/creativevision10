namespace CvWpfclient.ViewModels._40Shop;

/// <summary>
/// 棚卸明細表（原価無）。店舗へ配布する版で、原価単価と差異金額を出さない。
///
/// 抽出条件・SQL・集計は棚卸明細表(08Zaiko)と同一。ShowCost を false にすることで
/// 原価に関わる列が SELECT から外れる。SQLを二重に持つと片方だけ直して食い違うため継承にしている。
/// 列数が変わるので qfm は専用のものを使う。
/// </summary>
public partial class StockTakeDetailReportCostlessViewModel : _08Zaiko.StockMeisaiTableViewModel {
	protected override string ReportTitle => "棚卸明細表(原価無)";
	protected override string FormFileName => "StockTakeDetailReportCostless.qfm";
	protected override bool ShowCost => false;
}
