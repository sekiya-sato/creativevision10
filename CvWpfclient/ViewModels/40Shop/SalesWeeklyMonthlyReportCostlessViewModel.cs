namespace CvWpfclient.ViewModels._40Shop;

/// <summary>
/// 売上週報･月報（原価無）。店舗へ配布する版で、粗利額と粗利率を出さない。
///
/// 抽出条件・SQL・集計は売上週報･月報(20UriageAnalysis)と同一。ShowCost を false にして粗利列を落とす。
/// </summary>
public partial class SalesWeeklyMonthlyReportCostlessViewModel : _20UriageAnalysis.UriageShuhouGeppouViewModel {
	protected override string ReportTitle => "売上週報･月報(原価無)";
	protected override string FormFileName => "SalesWeeklyMonthlyReportCostless.qfm";
	protected override bool ShowCost => false;
}
