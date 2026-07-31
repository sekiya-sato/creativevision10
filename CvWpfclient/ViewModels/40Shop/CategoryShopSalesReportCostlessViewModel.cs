namespace CvWpfclient.ViewModels._40Shop;

/// <summary>
/// 分類別店別売上報告（原価無）。店舗へ配布する版で、値入率を出さない。
/// 上代金額は売価なので残す。
///
/// 抽出条件・SQL・集計は分類別店別売上報告(20UriageAnalysis)と同一。ShowCost を false にして値入率を落とす。
/// </summary>
public partial class CategoryShopSalesReportCostlessViewModel : _20UriageAnalysis.CategoryShopSalesReportViewModel {
	protected override string ReportTitle => "分類別店別売上報告(原価無)";
	protected override string FormFileName => "CategoryShopSalesReportCostless.qfm";
	protected override bool ShowCost => false;
}
