using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Wpfclient.Views;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.Models;

public partial class MenuData : ObservableObject {
	[ObservableProperty]
	private string header = string.Empty;

	[Obsolete]
	[ObservableProperty]
	private bool isExpand;

	[ObservableProperty]
	private ObservableCollection<MenuData>? subItems;

	[ObservableProperty]
	private string? addInfo;

	[ObservableProperty]
	private string? icon;

	/* --- after this line, only use for ViewModel --- */
	public Type ViewType { get; set; } = typeof(object);
	public bool IsDialog { get; set; } = true;
	public int InitParam { get; set; }

	public MenuData() {
	}
	public MenuData(string header, ObservableCollection<MenuData> subItems) {
		Header = header;
		SubItems = subItems;
	}
	public MenuData(string header, ObservableCollection<MenuData> subItems, string? icon = null) {
		Header = header;
		SubItems = subItems;
		Icon = icon;
	}
	public MenuData(string header, Type viewType, bool isDialog = true, int initParam = 0, string? addInfo = null, string? icon = null) {
		Header = header;
		ViewType = viewType;
		IsDialog = isDialog;
		InitParam = initParam;
		AddInfo = addInfo;
		Icon = icon;
	}

	public static ObservableCollection<MenuData> CreateDefault() {
		return new([
		new("■ 管理メニュー / テスト画面", new([
			/*
			new("ログイン", typeof(Views._00System.LoginView), true, 0, "gPRC"),
			new("リフレッシュ", typeof(Views._00System.LoginView), true, 1, "gRPC"),
			new("環境設定", typeof(Views._00System.SysSetConfigView), true),
			new("", new([
				new("", typeof(object), false, addInfo:"準備中"),
				new("", typeof(object), false, addInfo:"準備中"),
			])),
			*/
			new("システム管理マスタ", typeof(Views._01Master.MasterSysKanriMenteView), false, addInfo:"gRPC"),
			new("ログイン管理マスタ", typeof(Views._00System.SysLoginView), false, addInfo:"gRPC"),
			new("ログイン履歴情報", typeof(Views._00System.SysLoginHistoryView), false, addInfo:"gRPC"),
			new("自動実行履歴", typeof(Views._00System.SysAutoExecHistoryView), false, addInfo:"準備中"), // SysAutoExecHistoryView
	        /* ================================ */
			new("サンプル画面 ---", typeof(SampleView), false, addInfo:"gRPC"),
			new("Test画面--", typeof(Views.Sub.RangeInputParamView), false, addInfo:"一時的なテストで使用"),
		])),
		new("■ マスター", new([
			new("名称マスタメンテ", typeof(Views._01Master.MasterMeishoMenteView), false, addInfo:"Generic name setup"),
			new("社員マスタ", typeof(Views._01Master.MasterShainMenteView), false),
			new("商品マスタ", typeof(Views._01Master.MasterShohinMenteView), false),
			new("仕入先マスタメンテ", typeof(Views._01Master.MasterShiireMenteView), false),
			new("得意先マスタメンテ", typeof(Views._01Master.MasterTokuiMenteView), false),
			new("顧客マスタメンテ", typeof(Views._01Master.MasterEndCustomerMenteView), false),
			new("上代一括変更", typeof(Views._01Master.MasterJouDaiBulkChangeView), false, addInfo:"準備中"), // MasterJouDaiBulkChangeView
			new("取込レイアウト作成", typeof(Views._01Master.ImportTemplateCreateView), false, addInfo:"準備中"), // ImportTemplateCreateView
			new("外部CSVマスタ取込", typeof(Views._01Master.ExternalCsvImportView), false, addInfo:"準備中"), // ExternalCsvImportView
			new("原価変更登録", typeof(Views._01Master.GenkaChangeEntryView), false, addInfo:"準備中"), // GenkaChangeEntryView
			new("評価替", typeof(Views._01Master.ProductRatingChangeView), false, addInfo:"準備中"), // ProductRatingChangeView
			new("自動実行管理マスタ", typeof(Views._01Master.AutoExecKanriMasterView), false, addInfo:"準備中"), // AutoExecKanriMasterView
			new("自動実行スケジュール設定", typeof(Views._01Master.AutoExecScheduleSettingView), false, addInfo:"準備中"), // AutoExecScheduleSettingView
		])),
        /* ================================ */
		new("■ 予算", new([
			new("店ブランド予算マスタ", typeof(Views._02Yosan.ShopBrandBudgetMasterView), false, addInfo:"準備中"), // ShopBrandBudgetMasterView
			new("販売員別予算マスタ", typeof(Views._02Yosan.SalesStaffBudgetMasterView), false, addInfo:"準備中"), // SalesStaffBudgetMasterView
			new("営業担当別予算マスタ", typeof(Views._02Yosan.SalesRepBudgetMasterView), false, addInfo:"準備中"), // SalesRepBudgetMasterView
			new("店舗予算表", typeof(Views._02Yosan.ShopBudgetReportView), false, addInfo:"準備中"), // ShopBudgetReportView
			new("店舗ブランド別予算実績対比", typeof(Views._02Yosan.ShopBrandBudgetVsActualView), false, addInfo:"準備中"), // ShopBrandBudgetVsActualView
			new("日別店別予算表", typeof(Views._02Yosan.DailyShopBudgetReportView), false, addInfo:"準備中"), // DailyShopBudgetReportView
			new("販売員予算表", typeof(Views._02Yosan.SalesStaffBudgetReportView), false, addInfo:"準備中"), // SalesStaffBudgetReportView
		])),
		new("■ 発注", new([
			new("発注入力", typeof(Views._03Hatchu.HachuInputView), false, addInfo:"準備中"), // HachuInputView
			new("発注配分入力", typeof(Views._03Hatchu.HachuHaibunInputView), false, addInfo:"準備中"), // HachuHaibunInputView
			new("納品予定表", typeof(Views._03Hatchu.DeliveryScheduleTableView), false, addInfo:"準備中"), // DeliveryScheduleTableView
			new("仕入先別発注表", typeof(Views._03Hatchu.SupplierHachuTableView), false, addInfo:"準備中"), // SupplierHachuTableView
			new("商品別発注表", typeof(Views._03Hatchu.ShohinHachuTableView), false, addInfo:"準備中"), // ShohinHachuTableView
			new("商品別発注集計表", typeof(Views._03Hatchu.ShohinHachuSummaryTableView), false, addInfo:"準備中"), // ShohinHachuSummaryTableView
			new("納品予定照会", typeof(Views._03Hatchu.DeliveryScheduleInquiryView), false, addInfo:"準備中"), // DeliveryScheduleInquiryView
			new("仕入未受リスト", typeof(Views._03Hatchu.PendingShiireListView), false, addInfo:"準備中"), // PendingShiireListView
			new("発注書", typeof(Views._03Hatchu.HachuFormView), false, addInfo:"準備中"), // HachuFormView
			new("発注配分リスト", typeof(Views._03Hatchu.HachuHaibunListView), false, addInfo:"準備中"), // HachuHaibunListView
			new("発注残管理表", typeof(Views._03Hatchu.HachuZanKanriTableView), false, addInfo:"準備中"), // HachuZanKanriTableView
			new("発注残完了設定", typeof(Views._03Hatchu.HachuZanCompletionSettingView), false, addInfo:"準備中"), // HachuZanCompletionSettingView
		])),
		new("■ 受注 / 展示会", new([
			new("展示会受注入力", typeof(Views._04Juchu.JuchuInputView), false, addInfo:"準備中"), // JuchuInputView
			new("納品予定表", typeof(Views._04Juchu.NouhinYoteiTableView), false, addInfo:"準備中"), // NouhinYoteiTableView
			new("得意先別受注表", typeof(Views._04Juchu.TokuiSakiJuchuTableView), false, addInfo:"準備中"), // TokuiSakiJuchuTableView
			new("商品別受注表", typeof(Views._04Juchu.ShouhinJuchuTableView), false, addInfo:"準備中"), // ShouhinJuchuTableView
			new("商品別受注集計表", typeof(Views._04Juchu.ShouhinJuchuSummaryTableView), false, addInfo:"準備中"), // ShouhinJuchuSummaryTableView
			new("受注残管理表", typeof(Views._04Juchu.JuchuZanKanriTableView), false, addInfo:"準備中"), // JuchuZanKanriTableView
			new("受注残完了設定", typeof(Views._04Juchu.JuchuZanCompletionSettingView), false, addInfo:"準備中"), // JuchuZanCompletionSettingView
			new("展示会スワッチ", typeof(Views._04Juchu.TenjiSwatchView), false, addInfo:"準備中"), // TenjiSwatchView
			new("スワッチデータ作成", typeof(Views._04Juchu.SwatchDataCreateView), false, addInfo:"準備中"), // SwatchDataCreateView
			new("スワッチデータ一括作成", typeof(Views._04Juchu.SwatchDataBulkCreateView), false, addInfo:"準備中"), // SwatchDataBulkCreateView
			new("絵型一覧表", typeof(Views._04Juchu.DesignListTableView), false, addInfo:"準備中"), // DesignListTableView
			new("得意先別売上予定表", typeof(Views._04Juchu.TokuiSakiUriageYoteiTableView), false, addInfo:"準備中"), // TokuiSakiUriageYoteiTableView
			new("担当別展示会受注合計表", typeof(Views._04Juchu.TantoTenjiJuchuGoukeiTableView), false, addInfo:"準備中"), // TantoTenjiJuchuGoukeiTableView
			new("受注ベスト表", typeof(Views._04Juchu.JuchuBestTableView), false, addInfo:"準備中"), // JuchuBestTableView
			new("配分出荷リスト", typeof(Views._04Juchu.HaibunShukkaListView), false, addInfo:"準備中"), // HaibunShukkaListView
		])),
		new("■ 仕入", new([
			new("商品仕入入力", typeof(Views._05Shiire.ShiireInputView), false, addInfo:"準備中"), // ShiireInputView
			new("仕入返品入力", typeof(Views._05Shiire.HenpinInputView), false, addInfo:"準備中"), // HenpinInputView
			new("品番別仕入チェックリスト", typeof(Views._05Shiire.HinbanShiireCheckListView), false, addInfo:"準備中"), // HinbanShiireCheckListView
			new("ブランド別仕入金額表", typeof(Views._05Shiire.BrandShiireKingakuTableView), false, addInfo:"準備中"), // BrandShiireKingakuTableView
			new("仕入伝票印刷", typeof(Views._05Shiire.ShiireSlipPrintView), false, addInfo:"準備中"), // ShiireSlipPrintView
			new("仕入先別仕入推移表", typeof(Views._05Shiire.ShiireTrendReportView), false, addInfo:"準備中"), // ShiireTrendReportView
			new("支払入力", typeof(Views._05Shiire.ShiharaiInputView), false, addInfo:"準備中"), // ShiharaiInputView
			new("支払消込", typeof(Views._05Shiire.ShiharaiMatchingView), false, addInfo:"準備中"), // ShiharaiMatchingView
			new("仕入先元帳", typeof(Views._05Shiire.ShiireLedgerView), false, addInfo:"準備中"), // ShiireLedgerView
			new("買掛金管理表", typeof(Views._05Shiire.KaikakeBalanceReportView), false, addInfo:"準備中"), // KaikakeBalanceReportView
			new("支払一覧表", typeof(Views._05Shiire.ShiharaiListReportView), false, addInfo:"準備中"), // ShiharaiListReportView
			new("月別支払予定表", typeof(Views._05Shiire.MonthlyShiharaiYoteiTableView), false, addInfo:"準備中"), // MonthlyShiharaiYoteiTableView
			new("支払残高明細書", typeof(Views._05Shiire.ShiharaiBalanceDetailView), false, addInfo:"準備中"), // ShiharaiBalanceDetailView
		])),
		new("■ 売上", new([
			new("出荷・売上入力", typeof(Views._06Uriage.ShukkaUriageInputView), false, addInfo:"準備中"), // ShukkaUriageInputView
			new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), false), // ShopUriageInputView
			new("POS日別精算入力", typeof(Views._06Uriage.PosDailySeisanInputView), false, addInfo:"準備中"), // PosDailySeisanInputView
			new("売上金種Viewer", typeof(Views._06Uriage.UriageCashTypeReportView), false, addInfo:"準備中"), // UriageCashTypeReportView
			new("品番別売上チェックリスト", typeof(Views._06Uriage.HinbanUriageCheckListView), false, addInfo:"準備中"), // HinbanUriageCheckListView
			new("売上チェックリスト", typeof(Views._06Uriage.UriageCheckListView), false, addInfo:"準備中"), // UriageCheckListView
			new("納品書印刷", typeof(Views._06Uriage.NouhinBookPrintView), false, addInfo:"準備中"), // NouhinBookPrintView
			new("納品書印刷(専用伝票)", typeof(Views._06Uriage.NouhinBookPrintCustomView), false, addInfo:"準備中"), // NouhinBookPrintCustomView
			new("納品書未発行チェックリスト", typeof(Views._06Uriage.NouhinBookPendingCheckListView), false, addInfo:"準備中"), // NouhinBookPendingCheckListView
			new("入金入力", typeof(Views._06Uriage.NyukinInputView), false, addInfo:"準備中"), // NyukinInputView
			new("入金消込", typeof(Views._06Uriage.NyukinMatchingView), false, addInfo:"準備中"), // NyukinMatchingView
			new("得意先元帳", typeof(Views._06Uriage.TokuiLedgerView), false, addInfo:"準備中"), // TokuiLedgerView
			new("売掛金管理表", typeof(Views._06Uriage.UrikakeBalanceReportView), false, addInfo:"準備中"), // UrikakeBalanceReportView
			new("月別入金予定表", typeof(Views._06Uriage.MonthlyNyukinYoteiTableView), false, addInfo:"準備中"), // MonthlyNyukinYoteiTableView
			new("請求一覧表", typeof(Views._06Uriage.SeikyuListReportView), false, addInfo:"準備中"), // SeikyuListReportView
			new("請求書印刷", typeof(Views._06Uriage.SeikyuBalanceDetailView), false, addInfo:"準備中"), // SeikyuBalanceDetailView
			new("得意先別売上推移表", typeof(Views._06Uriage.TokuiTrendReportView), false, addInfo:"準備中"), // TokuiTrendReportView
		])),
		new("■ 配分", new([
			new("店舗配分入力", typeof(Views._07Haibun.ShopHaibunInputView), false, addInfo:"準備中"), // ShopHaibunInputView
			new("受注配分入力", typeof(Views._07Haibun.JuchuHaibunInputView), false, addInfo:"準備中"), // JuchuHaibunInputView
			new("店舗出荷依頼", typeof(Views._07Haibun.ShopShippingRequestView), false, addInfo:"準備中"), // ShopShippingRequestView
			new("在庫品配分", typeof(Views._07Haibun.ZaikoHinHaibunView), false, addInfo:"準備中"), // ZaikoHinHaibunView
			new("得意先別配分入力", typeof(Views._07Haibun.TokuiHaibunInputView), false, addInfo:"準備中"), // TokuiHaibunInputView
			new("出荷指示確定(商品)", typeof(Views._07Haibun.ShippingConfirmShohinView), false, addInfo:"準備中"), // ShippingConfirmShohinView
			new("出荷指示確定(得意先)", typeof(Views._07Haibun.ShippingConfirmTokuiView), false, addInfo:"準備中"), // ShippingConfirmTokuiView
			new("出荷処理入力", typeof(Views._07Haibun.ShippingInputView), false, addInfo:"準備中"), // ShippingInputView
			new("配分データメンテ", typeof(Views._07Haibun.HaibunDataMenteView), false, addInfo:"準備中"), // HaibunDataMenteView
			new("取置入力", typeof(Views._07Haibun.ReservationInputView), false, addInfo:"準備中"), // ReservationInputView
			new("移動指示(SKU)", typeof(Views._07Haibun.IdoInstructionSkuView), false, addInfo:"準備中"), // IdoInstructionSkuView
			new("移動指示(商品)", typeof(Views._07Haibun.IdoInstructionShohinView), false, addInfo:"準備中"), // IdoInstructionShohinView
			new("出荷指示明細書印刷", typeof(Views._07Haibun.ShippingConfirmDetailPrintView), false, addInfo:"準備中"), // ShippingConfirmDetailPrintView
			new("納入一覧表", typeof(Views._07Haibun.ShippingListReportView), false, addInfo:"準備中"), // ShippingListReportView
			new("出荷指示一覧印刷", typeof(Views._07Haibun.ShippingConfirmListView), false, addInfo:"準備中"), // ShippingConfirmListView
			new("配分関連メンテナンス", typeof(Views._07Haibun.HaibunMenteView), false, addInfo:"準備中"), // HaibunMenteView
			new("自動発注・補充対象除外品設定", typeof(Views._07Haibun.AutoHachuHojunExcludeSettingView), false, addInfo:"準備中"), // AutoHachuHojunExcludeSettingView
			new("在庫基準自動補充メンテナンス", typeof(Views._07Haibun.ZaikoAutoHojunMenteView), false, addInfo:"準備中"), // ZaikoAutoHojunMenteView
		])),
		new("在庫管理", new([
			new("棚卸入力", typeof(Views._08Zaiko.StockInputView), false, addInfo:"準備中"), // StockInputView
			new("移動入力(即時)", typeof(Views._08Zaiko.IdoInputSokuView), false, addInfo:"準備中"), // IdoInputSokuView
			new("移動入力(積送)", typeof(Views._08Zaiko.IdoInputOutView), false, addInfo:"準備中"), // IdoInputOutView
			new("移動受入力", typeof(Views._08Zaiko.IdoInputUkeView), false, addInfo:"準備中"), // IdoInputUkeView
			new("棚卸差異問合せ", typeof(Views._08Zaiko.StockDifferenceQueryView), false, addInfo:"準備中"), // StockDifferenceQueryView
			new("在庫問合せ", typeof(Views._08Zaiko.ZaikoQueryView), false, addInfo:"準備中"), // ZaikoQueryView
			new("商品履歴問合せ", typeof(Views._08Zaiko.ShohinHistoryQueryView), false, addInfo:"準備中"), // ShohinHistoryQueryView
			new("棚卸入力(一覧方式)", typeof(Views._08Zaiko.StockInputListView), false, addInfo:"準備中"), // StockInputListView
			new("在庫強制調整入力", typeof(Views._08Zaiko.StockForceInputView), false, addInfo:"準備中"), // StockForceInputView
			new("在庫移動入力", typeof(Views._08Zaiko.StockIdoInputView), false, addInfo:"準備中"), // StockIdoInputView
			new("倉庫分類別棚卸表", typeof(Views._08Zaiko.SokoCategoryStockListView), false, addInfo:"準備中"), // SokoCategoryStockListView
			new("倉庫別受払表", typeof(Views._08Zaiko.SokoInOutReportView), false, addInfo:"準備中"), // SokoInOutReportView
			new("商品別受払表", typeof(Views._08Zaiko.ShohinInOutReportView), false, addInfo:"準備中"), // ShohinInOutReportView
			new("倉庫別在庫集計表", typeof(Views._08Zaiko.SokoSummaryReportView), false, addInfo:"準備中"), // SokoSummaryReportView
			new("汎用在庫表", typeof(Views._08Zaiko.GeneralStockTableView), false, addInfo:"準備中"), // GeneralStockTableView
			new("棚卸明細表", typeof(Views._08Zaiko.StockMeisaiTableView), false, addInfo:"準備中"), // StockMeisaiTableView
			new("棚卸日一括メンテナンス", typeof(Views._08Zaiko.StockDateBulkMenteView), false, addInfo:"準備中"), // StockDateBulkMenteView
			new("棚卸チェックリスト", typeof(Views._08Zaiko.StockCheckListView), false, addInfo:"準備中"), // StockCheckListView
			new("品番別移動チェックリスト", typeof(Views._08Zaiko.HinbanIdoCheckListView), false, addInfo:"準備中"), // HinbanIdoCheckListView
			new("移動未受リスト", typeof(Views._08Zaiko.IdoUnreceivedListView), false, addInfo:"準備中"), // IdoUnreceivedListView
		])),
		new("売上分析", new([
			new("販売動向表", typeof(Views._20UriageAnalysis.SalesTrendReportView), false, addInfo:"準備中"), // SalesTrendReportView
			new("品番別販売動向表", typeof(Views._20UriageAnalysis.HinbanSalesTrendReportView), false, addInfo:"準備中"), // HinbanSalesTrendReportView
			new("投入売上在庫表", typeof(Views._20UriageAnalysis.InputSalesStockReportView), false, addInfo:"準備中"), // InputSalesStockReportView
			new("ベスト表", typeof(Views._20UriageAnalysis.BestSalesReportView), false, addInfo:"準備中"), // BestSalesReportView
			new("商品消化率表", typeof(Views._20UriageAnalysis.ShohinTurnoverRateReportView), false, addInfo:"準備中"), // ShohinTurnoverRateReportView
			new("セット売上分析表", typeof(Views._20UriageAnalysis.SetSalesAnalysisReportView), false, addInfo:"準備中"), // SetSalesAnalysisReportView
			new("店別売上日報", typeof(Views._20UriageAnalysis.ShopSalesDailyView), false, addInfo:"準備中"), // ShopSalesDailyView
			new("店舗別売上日計表", typeof(Views._20UriageAnalysis.ShopSalesDailySummaryView), false, addInfo:"準備中"), // ShopSalesDailySummaryView
			new("売上速報", typeof(Views._20UriageAnalysis.SalesQuickReportView), false, addInfo:"準備中"), // SalesQuickReportView
			new("売上週報･月報", typeof(Views._20UriageAnalysis.UriageShuhouGeppouView), false, addInfo:"準備中"), // UriageShuhouGeppouView
			new("売上予算構成比", typeof(Views._20UriageAnalysis.SalesBudgetRatioReportView), false, addInfo:"準備中"), // SalesBudgetRatioReportView
			new("分類別売上消化率表", typeof(Views._20UriageAnalysis.CategorySalesConsumptionRateView), false, addInfo:"準備中"), // CategorySalesConsumptionRateView
			new("分類別店別売上報告", typeof(Views._20UriageAnalysis.CategoryShopSalesReportView), false, addInfo:"準備中"), // CategoryShopSalesReportView
			new("店舗売上ランキング表", typeof(Views._20UriageAnalysis.ShopSalesRankingReportView), false, addInfo:"準備中"), // ShopSalesRankingReportView
		])),
		new("卸・販売員・経営分析", new([
			new("得意先別売上日報", typeof(Views._21OroshiAnalysis.TokuiSalesDailyReportView), false, addInfo:"準備中"), // TokuiSalesDailyReportView
			new("得意先別売上月報", typeof(Views._21OroshiAnalysis.TokuiSalesMonthlyReportView), false, addInfo:"準備中"), // TokuiSalesMonthlyReportView
			new("担当別売上実績半期報", typeof(Views._21OroshiAnalysis.TantoSalesHalfYearReportView), false, addInfo:"準備中"), // TantoSalesHalfYearReportView
			new("担当得意先別予算実績対比表", typeof(Views._21OroshiAnalysis.TantoTokuiBudgetActualReportView), false, addInfo:"準備中"), // TantoTokuiBudgetActualReportView
			new("個人売上ランキング表", typeof(Views._21OroshiAnalysis.PersonalSalesRankingReportView), false, addInfo:"準備中"), // PersonalSalesRankingReportView
			new("販売員別予算実績対比表", typeof(Views._21OroshiAnalysis.SalesStaffBudgetVsActualReportView), false, addInfo:"準備中"), // SalesStaffBudgetVsActualReportView
			new("半期報", typeof(Views._21OroshiAnalysis.HalfYearReportView), false, addInfo:"準備中"), // HalfYearReportView
			new("全社受払表", typeof(Views._21OroshiAnalysis.CorporateInOutReportView), false, addInfo:"準備中"), // CorporateInOutReportView
			new("卸・店舗売上実績表", typeof(Views._21OroshiAnalysis.OroshiShopSalesActualReportView), false, addInfo:"準備中"), // OroshiShopSalesActualReportView
		])),
		new("C.P.A", new([
			new("★T.L - アナライザー★", typeof(Views._22CPA.TimelineAnalyzerView), false, addInfo:"準備中"), // TimelineAnalyzerView
			new("★C.G - アナライザー★", typeof(Views._22CPA.CategoryGroupAnalyzerView), false, addInfo:"準備中"), // CategoryGroupAnalyzerView
			new("ナンでも？CSV", typeof(Views._22CPA.AnyCsvView), false, addInfo:"準備中"), // AnyCsvView
			new("ABC分析(全社)", typeof(Views._22CPA.AbcAnalysisCorporateView), false, addInfo:"準備中"), // AbcAnalysisCorporateView
			new("ABC分析(店舗)", typeof(Views._22CPA.AbcAnalysisShopView), false, addInfo:"準備中"), // AbcAnalysisShopView
			new("在庫データ出力", typeof(Views._22CPA.StockDataOutputView), false, addInfo:"準備中"), // StockDataOutputView
			new("在庫受け払い照会", typeof(Views._22CPA.StockInOutInquiryView), false, addInfo:"準備中"), // StockInOutInquiryView
			new("販売動向ビュー", typeof(Views._22CPA.SalesTrendView), false, addInfo:"準備中"), // SalesTrendView
			new("店舗稼動ビュー", typeof(Views._22CPA.ShopActivityView), false, addInfo:"準備中"), // ShopActivityView
			new("売消台帳ビュー", typeof(Views._22CPA.SalesConsumptionLedgerView), false, addInfo:"準備中"), // SalesConsumptionLedgerView
			new("商品分析", typeof(Views._22CPA.ShohinAnalysisView), false, addInfo:"準備中"), // ShohinAnalysisView
			new("オンラインモニタ", typeof(Views._22CPA.RealTimeMonitorView), false, addInfo:"準備中"), // RealTimeMonitorView
			new("売上・在庫問合せ", typeof(Views._22CPA.SalesStockQueryView), false, addInfo:"準備中"), // SalesStockQueryView
			new("ベストレポート", typeof(Views._22CPA.BestSalesReportView), false, addInfo:"準備中"), // BestSalesReportView
		])),
		new("HHT / POS連携", new([
			new("HHT用マスタデータ作成(cvnetcom)", typeof(Views._30HHT.HhtMasterDataCreateView), false, addInfo:"準備中"), // HhtMasterDataCreateView
			new("HHT手動データ受信", typeof(Views._30HHT.HhtManualDataReceiveView), false, addInfo:"準備中"), // HhtManualDataReceiveView
			new("HHTエラーデータ修正入力", typeof(Views._30HHT.HhtErrorDataInputView), false, addInfo:"準備中"), // HhtErrorDataInputView
			new("HHTデータ更新", typeof(Views._30HHT.HhtDataUpdateView), false, addInfo:"準備中"), // HhtDataUpdateView
			new("HHT未更新データ印刷", typeof(Views._30HHT.HhtUnupdatedDataPrintView), false, addInfo:"準備中"), // HhtUnupdatedDataPrintView
			new("HHT未更新データ一括削除", typeof(Views._30HHT.HhtUnupdatedDataDeleteView), false, addInfo:"準備中"), // HhtUnupdatedDataDeleteView
			new("出荷指示明細書印刷", typeof(Views._30HHT.ShippingConfirmDetailPrintView), false, addInfo:"準備中"), // ShippingConfirmDetailPrintView
			new("移動明細書印刷", typeof(Views._30HHT.IdoDetailBookPrintView), false, addInfo:"準備中"), // IdoDetailBookPrintView
			new("即時移動明細書", typeof(Views._30HHT.IdoSokuDetailBookPrintView), false, addInfo:"準備中"), // IdoSokuDetailBookPrintView
			new("HHT手動データ受信(ﾃﾞｰﾀ送信後)", typeof(Views._30HHT.HhtManualDataReceive2View), false, addInfo:"準備中"), // HhtManualDataReceive2View
			new("HHT用マスタバーコード印刷", typeof(Views._30HHT.HhtMasterBarcodePrintView), false, addInfo:"準備中"), // HhtMasterBarcodePrintView
		])),
		new("月次 / 更新処理", new([
			new("請求計算", typeof(Views._31Monthly.BillingCalculationView), false, addInfo:"準備中"), // BillingCalculationView
			new("支払計算", typeof(Views._31Monthly.PaymentCalculationView), false, addInfo:"準備中"), // PaymentCalculationView
			new("棚卸開始処理", typeof(Views._31Monthly.StockTakeInitiationView), false, addInfo:"準備中"), // StockTakeInitiationView
			new("棚卸確定", typeof(Views._31Monthly.StockTakeFinalizationView), false, addInfo:"準備中"), // StockTakeFinalizationView
			new("在庫・掛再更新", typeof(Views._31Monthly.StockKakeUpdateView), false, addInfo:"準備中"), // StockKakeUpdateView
			new("在庫累計更新", typeof(Views._31Monthly.StockRuikeiUpdateView), false, addInfo:"準備中"), // StockRuikeiUpdateView
			new("締日更新", typeof(Views._31Monthly.ShimebiUpdateView), false, addInfo:"準備中"), // ShimebiUpdateView
			new("諸掛更新", typeof(Views._31Monthly.SundryChargesUpdateView), false, addInfo:"準備中"), // SundryChargesUpdateView
			new("一時処理用(管理者用)", typeof(Views._31Monthly.TemporaryProcessingView), false, addInfo:"準備中"), // TemporaryProcessingView
			new("残高登録処理", typeof(Views._31Monthly.BalanceRegistrationView), false, addInfo:"準備中"), // BalanceRegistrationView
			new("データ整理更新", typeof(Views._31Monthly.DataCleanupUpdateView), false, addInfo:"準備中"), // DataCleanupUpdateView
			new("消費税再計算", typeof(Views._31Monthly.TaxRecalculationView), false, addInfo:"準備中"), // TaxRecalculationView
			new("最終仕入原価更新", typeof(Views._31Monthly.LastPurchaseCostRefreshView), false, addInfo:"準備中"), // LastPurchaseCostRefreshView
			new("総平均原価更新", typeof(Views._31Monthly.TotalAverageCostUpdateView), false, addInfo:"準備中"), // TotalAverageCostUpdateView
			new("消化仕入更新", typeof(Views._31Monthly.ConsumptionPurchaseUpdateView), false, addInfo:"準備中"), // ConsumptionPurchaseUpdateView
			new("積送中クリア", typeof(Views._31Monthly.InTransitClearView), false, addInfo:"準備中"), // InTransitClearView
			new("月間データ集計", typeof(Views._31Monthly.MonthlyDataSummaryView), false, addInfo:"準備中"), // MonthlyDataSummaryView
			new("自動発注・補充の実行", typeof(Views._31Monthly.AutoOrderReplenishExecuteVie), false, addInfo:"準備中"), // AutoOrderReplenishExecuteVie
		])),
		new("Loyal Customer", new([
			new("顧客マスタ", typeof(Views._32LoyalCustomer.CustomerMasterView), false, addInfo:"準備中"), // CustomerMasterView
			new("ポイントマスタ（ベース）（管理者用)", typeof(Views._32LoyalCustomer.PointMasterBaseAdminView), false, addInfo:"準備中"), // PointMasterBaseAdminView
			new("ポイントマスタ（キャンペーン）", typeof(Views._32LoyalCustomer.PointMasterCampaignView), false, addInfo:"準備中"), // PointMasterCampaignView
			new("ポイントマスタ（ボーナス）", typeof(Views._32LoyalCustomer.PointMasterBonusView), false, addInfo:"準備中"), // PointMasterBonusView
			new("店舗別キャンペーン設定", typeof(Views._32LoyalCustomer.ShopCampaignSettingView), false, addInfo:"準備中"), // ShopCampaignSettingView
			new("商品店舗別ポイント設定", typeof(Views._32LoyalCustomer.ShohinShopPointSettingView), false, addInfo:"準備中"), // ShohinShopPointSettingView
			new("ポイント集計", typeof(Views._32LoyalCustomer.PointSummaryView), false, addInfo:"準備中"), // PointSummaryView
			new("顧客カルテ", typeof(Views._32LoyalCustomer.EndCustomerProfileView), false, addInfo:"準備中"), // EndCustomerProfileView
			new("RFMクロス分析表", typeof(Views._32LoyalCustomer.RfmCrossAnalysisTableView), false, addInfo:"準備中"), // RfmCrossAnalysisTableView
		])),
		new("店舗", new([
			new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), false), // ShopUriageInputView
			new("棚卸明細表(原価無)", typeof(Views._40Shop.StockTakeDetailReportCostlessView), false, addInfo:"準備中"), // StockTakeDetailReportCostlessView
			new("汎用在庫表(原価無)", typeof(Views._40Shop.GeneralInventoryTableCostlessView), false, addInfo:"準備中"), // GeneralInventoryTableCostlessView
			new("売上速報(原価無)", typeof(Views._40Shop.SalesQuickReportCostlessView), false, addInfo:"準備中"), // SalesQuickReportCostlessView
			new("売上週報･月報(原価無)", typeof(Views._40Shop.SalesWeeklyMonthlyReportCostlessView), false, addInfo:"準備中"), // SalesWeeklyMonthlyReportCostlessView
			new("分類別店別売上報告(原価無)", typeof(Views._40Shop.CategoryStoreSalesReportCostlessView), false, addInfo:"準備中"), // CategoryStoreSalesReportCostlessView
		])),
		new("物流", new([
			new("マスタデータ作成", typeof(Views._41Logistics.LogisticsMasterDataCreateView), false, addInfo:"準備中"), // LogisticsMasterDataCreateView
			new("連携データ手動送信", typeof(Views._41Logistics.IntegrationDataManualTransmitView), false, addInfo:"準備中"), // IntegrationDataManualTransmitView
			new("連携データ手動受信", typeof(Views._41Logistics.IntegrationDataManualReceiveView), false, addInfo:"準備中"), // IntegrationDataManualReceiveView
			new("連携エラーデータ照会", typeof(Views._41Logistics.IntegrationErrorDataQueryView), false, addInfo:"準備中"), // IntegrationErrorDataQueryView
		])),
	]);
	}
}
