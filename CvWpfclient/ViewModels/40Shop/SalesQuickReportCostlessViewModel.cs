namespace CvWpfclient.ViewModels._40Shop;

/// <summary>
/// 売上速報（原価無）。店舗へ配布する版で、粗利額と粗利率を出さない。
/// 売上・予算・予算比・前年比は店舗でも必要なのでそのまま出す。
///
/// 抽出条件・SQL・集計は売上速報(20UriageAnalysis)と同一。ShowCost を false にして粗利列を落とす。
/// </summary>
public partial class SalesQuickReportCostlessViewModel : _20UriageAnalysis.SalesQuickReportViewModel {
	protected override string ReportTitle => "売上速報(原価無)";
	protected override string FormFileName => "SalesQuickReportCostless.qfm";
	protected override bool ShowCost => false;
}
