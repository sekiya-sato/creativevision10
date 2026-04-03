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
	public MenuData(string header, Type viewType, bool isDialog = false, int initParam = 0, string? addInfo = null, string? icon = null) {
		Header = header;
		ViewType = viewType;
		IsDialog = isDialog;
		InitParam = initParam;
		AddInfo = addInfo;
		Icon = icon;
	}

	public static ObservableCollection<MenuData> CreateDefault() {
		return new([
			// ToDo: MenuDataの"準備中"メッセ終わったところ書き換え
			// `addInfo:"準備中"` のものは、基本的に空のViewおよびViewModel
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
			new("システム管理マスタ", typeof(Views._01Master.MasterSysKanriMenteView),  addInfo:"会社情報、締日、税率などを設定"),
			new("ログイン管理マスタ", typeof(Views._00System.SysLoginView),  addInfo:"ログインIDの管理とユーザ割当、有効期限の設定"),
			new("ログイン履歴情報", typeof(Views._00System.SysLoginHistoryView),  addInfo:"ログイン履歴の確認"),
			new("自動実行履歴", typeof(Views._00System.SysAutoExecHistoryView), addInfo:"準備中"),
			new("汎用マスタメンテ", typeof(Views._00System.SysGeneralMenteView), addInfo:"MasterMeisho を汎用編集UIで表示・更新"),
	        /* ================================ */
			new("サンプル画面 ---", typeof(SampleView), addInfo:"MaterialDesignサンプルとテストPG"),
			new("Test画面--", typeof(Views._06Uriage.ShopUriageInputView), addInfo:"一時的なテストで使用"),
		])),
		new("■ マスター", new([
			new("名称マスタメンテ", typeof(Views._01Master.MasterMeishoMenteView), addInfo:"区分別の名称マスタメンテ画面"),
			new("社員マスタ", typeof(Views._01Master.MasterShainMenteView), addInfo:"社員マスタメンテ画面"),
			new("商品マスタ", typeof(Views._01Master.MasterShohinMenteView), addInfo:"商品マスタメンテ画面"),
			new("仕入先マスタメンテ", typeof(Views._01Master.MasterShiireMenteView), addInfo:"仕入先マスタメンテ画面"),
			new("得意先マスタメンテ", typeof(Views._01Master.MasterTokuiMenteView), addInfo:"得意先マスタメンテ画面"),
			new("顧客マスタメンテ", typeof(Views._01Master.MasterEndCustomerMenteView), addInfo:"顧客マスタメンテ画面"),
			new("上代一括変更", typeof(Views._01Master.MasterJouDaiBulkChangeView), addInfo:"準備中"),
			new("取込レイアウト作成", typeof(Views._01Master.ImportTemplateCreateView), addInfo:"準備中"),
			new("外部CSVマスタ取込", typeof(Views._01Master.ExternalCsvImportView), addInfo:"準備中"),
			new("原価変更登録", typeof(Views._01Master.GenkaChangeEntryView), addInfo:"準備中"),
			new("評価替", typeof(Views._01Master.ProductRatingChangeView), addInfo:"準備中"),
			new("自動実行管理マスタ", typeof(Views._01Master.AutoExecKanriMasterView), addInfo:"準備中"),
			new("自動実行スケジュール設定", typeof(Views._01Master.AutoExecScheduleSettingView), addInfo:"準備中"),
		])),
        /* ================================ */
		new("■ 予算", new([
			new("店ブランド予算マスタ", typeof(Views._02Yosan.ShopBrandBudgetMasterView), addInfo:"準備中"),
			new("販売員別予算マスタ", typeof(Views._02Yosan.SalesStaffBudgetMasterView), addInfo:"準備中"),
			new("営業担当別予算マスタ", typeof(Views._02Yosan.SalesRepBudgetMasterView), addInfo:"準備中"),
			new("店舗予算表", typeof(Views._02Yosan.ShopBudgetReportView), addInfo:"準備中"),
			new("店舗ブランド別予算実績対比", typeof(Views._02Yosan.ShopBrandBudgetVsActualView), addInfo:"準備中"),
			new("日別店別予算表", typeof(Views._02Yosan.DailyShopBudgetReportView), addInfo:"準備中"),
			new("販売員予算表", typeof(Views._02Yosan.SalesStaffBudgetReportView), addInfo:"準備中"),
		])),
		new("■ 発注", new([
			new("発注入力", typeof(Views._03Hatchu.HachuInputView), addInfo:"準備中"),
			new("発注配分入力", typeof(Views._03Hatchu.HachuHaibunInputView), addInfo:"準備中"),
			new("納品予定表", typeof(Views._03Hatchu.DeliveryScheduleTableView), addInfo:"準備中"),
			new("仕入先別発注表", typeof(Views._03Hatchu.SupplierHachuTableView), addInfo:"準備中"),
			new("商品別発注表", typeof(Views._03Hatchu.ShohinHachuTableView), addInfo:"準備中"),
			new("商品別発注集計表", typeof(Views._03Hatchu.ShohinHachuSummaryTableView), addInfo:"準備中"),
			new("納品予定照会", typeof(Views._03Hatchu.DeliveryScheduleInquiryView), addInfo:"準備中"),
			new("仕入未受リスト", typeof(Views._03Hatchu.PendingShiireListView), addInfo:"準備中"),
			new("発注書", typeof(Views._03Hatchu.HachuFormView), addInfo:"準備中"),
			new("発注配分リスト", typeof(Views._03Hatchu.HachuHaibunListView), addInfo:"準備中"),
			new("発注残管理表", typeof(Views._03Hatchu.HachuZanKanriTableView), addInfo:"準備中"),
			new("発注残完了設定", typeof(Views._03Hatchu.HachuZanCompletionSettingView), addInfo:"準備中"),
		])),
		new("■ 受注 / 展示会", new([
			new("展示会受注入力", typeof(Views._04Juchu.JuchuInputView), addInfo:"準備中"),
			new("納品予定表", typeof(Views._04Juchu.NouhinYoteiTableView), addInfo:"準備中"),
			new("得意先別受注表", typeof(Views._04Juchu.TokuiSakiJuchuTableView), addInfo:"準備中"),
			new("商品別受注表", typeof(Views._04Juchu.ShouhinJuchuTableView), addInfo:"準備中"),
			new("商品別受注集計表", typeof(Views._04Juchu.ShouhinJuchuSummaryTableView), addInfo:"準備中"),
			new("受注残管理表", typeof(Views._04Juchu.JuchuZanKanriTableView), addInfo:"準備中"),
			new("受注残完了設定", typeof(Views._04Juchu.JuchuZanCompletionSettingView), addInfo:"準備中"),
			new("展示会スワッチ", typeof(Views._04Juchu.TenjiSwatchView), addInfo:"準備中"),
			new("スワッチデータ作成", typeof(Views._04Juchu.SwatchDataCreateView), addInfo:"準備中"),
			new("スワッチデータ一括作成", typeof(Views._04Juchu.SwatchDataBulkCreateView), addInfo:"準備中"),
			new("絵型一覧表", typeof(Views._04Juchu.DesignListTableView), addInfo:"準備中"),
			new("得意先別売上予定表", typeof(Views._04Juchu.TokuiSakiUriageYoteiTableView), addInfo:"準備中"),
			new("担当別展示会受注合計表", typeof(Views._04Juchu.TantoTenjiJuchuGoukeiTableView), addInfo:"準備中"),
			new("受注ベスト表", typeof(Views._04Juchu.JuchuBestTableView), addInfo:"準備中"),
			new("配分出荷リスト", typeof(Views._04Juchu.HaibunShukkaListView), addInfo:"準備中"),
		])),
		new("■ 仕入", new([
			new("商品仕入入力", typeof(Views._05Shiire.ShiireInputView), addInfo:"準備中"),
			new("仕入返品入力", typeof(Views._05Shiire.HenpinInputView), addInfo:"準備中"),
			new("品番別仕入チェックリスト", typeof(Views._05Shiire.HinbanShiireCheckListView), addInfo:"準備中"),
			new("ブランド別仕入金額表", typeof(Views._05Shiire.BrandShiireKingakuTableView), addInfo:"準備中"),
			new("仕入伝票印刷", typeof(Views._05Shiire.ShiireSlipPrintView), addInfo:"準備中"),
			new("仕入先別仕入推移表", typeof(Views._05Shiire.ShiireTrendReportView), addInfo:"準備中"),
			new("支払入力", typeof(Views._05Shiire.ShiharaiInputView), addInfo:"準備中"),
			new("支払消込", typeof(Views._05Shiire.ShiharaiMatchingView), addInfo:"準備中"),
			new("仕入先元帳", typeof(Views._05Shiire.ShiireLedgerView), addInfo:"準備中"),
			new("買掛金管理表", typeof(Views._05Shiire.KaikakeBalanceReportView), addInfo:"準備中"),
			new("支払一覧表", typeof(Views._05Shiire.ShiharaiListReportView), addInfo:"準備中"),
			new("月別支払予定表", typeof(Views._05Shiire.MonthlyShiharaiYoteiTableView), addInfo:"準備中"),
			new("支払残高明細書", typeof(Views._05Shiire.ShiharaiBalanceDetailView), addInfo:"準備中"),
		])),
		new("■ 売上", new([
			new("出荷・売上入力", typeof(Views._06Uriage.ShukkaUriageInputView), addInfo:"準備中"),
			new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), addInfo:"店舗売上入力"),
			new("POS日別精算入力", typeof(Views._06Uriage.PosDailySeisanInputView), addInfo:"準備中"),
			new("売上金種Viewer", typeof(Views._06Uriage.UriageCashTypeReportView), addInfo:"準備中"),
			new("品番別売上チェックリスト", typeof(Views._06Uriage.HinbanUriageCheckListView), addInfo:"準備中"),
			new("売上チェックリスト", typeof(Views._06Uriage.UriageCheckListView), addInfo:"準備中"),
			new("納品書印刷", typeof(Views._06Uriage.NouhinBookPrintView), addInfo:"準備中"),
			new("納品書印刷(専用伝票)", typeof(Views._06Uriage.NouhinBookPrintCustomView), addInfo:"準備中"),
			new("納品書未発行チェックリスト", typeof(Views._06Uriage.NouhinBookPendingCheckListView), addInfo:"準備中"),
			new("入金入力", typeof(Views._06Uriage.NyukinInputView), addInfo:"準備中"),
			new("入金消込", typeof(Views._06Uriage.NyukinMatchingView), addInfo:"準備中"),
			new("得意先元帳", typeof(Views._06Uriage.TokuiLedgerView), addInfo:"準備中"),
			new("売掛金管理表", typeof(Views._06Uriage.UrikakeBalanceReportView), addInfo:"準備中"),
			new("月別入金予定表", typeof(Views._06Uriage.MonthlyNyukinYoteiTableView), addInfo:"準備中"),
			new("請求一覧表", typeof(Views._06Uriage.SeikyuListReportView), addInfo:"準備中"),
			new("請求書印刷", typeof(Views._06Uriage.SeikyuBalanceDetailView), addInfo:"準備中"),
			new("得意先別売上推移表", typeof(Views._06Uriage.TokuiTrendReportView), addInfo:"準備中"),
		])),
		new("■ 配分", new([
			new("店舗配分入力", typeof(Views._07Haibun.ShopHaibunInputView), addInfo:"準備中"),
			new("受注配分入力", typeof(Views._07Haibun.JuchuHaibunInputView), addInfo:"準備中"),
			new("店舗出荷依頼", typeof(Views._07Haibun.ShopShippingRequestView), addInfo:"準備中"),
			new("在庫品配分", typeof(Views._07Haibun.ZaikoHinHaibunView), addInfo:"準備中"),
			new("得意先別配分入力", typeof(Views._07Haibun.TokuiHaibunInputView), addInfo:"準備中"),
			new("出荷指示確定(商品)", typeof(Views._07Haibun.ShippingConfirmShohinView), addInfo:"準備中"),
			new("出荷指示確定(得意先)", typeof(Views._07Haibun.ShippingConfirmTokuiView), addInfo:"準備中"),
			new("出荷処理入力", typeof(Views._07Haibun.ShippingInputView), addInfo:"準備中"),
			new("配分データメンテ", typeof(Views._07Haibun.HaibunDataMenteView), addInfo:"準備中"),
			new("取置入力", typeof(Views._07Haibun.ReservationInputView), addInfo:"準備中"),
			new("移動指示(SKU)", typeof(Views._07Haibun.IdoInstructionSkuView), addInfo:"準備中"),
			new("移動指示(商品)", typeof(Views._07Haibun.IdoInstructionShohinView), addInfo:"準備中"),
			new("出荷指示明細書印刷", typeof(Views._07Haibun.ShippingConfirmDetailPrintView), addInfo:"準備中"),
			new("納入一覧表", typeof(Views._07Haibun.ShippingListReportView), addInfo:"準備中"),
			new("出荷指示一覧印刷", typeof(Views._07Haibun.ShippingConfirmListView), addInfo:"準備中"),
			new("配分関連メンテナンス", typeof(Views._07Haibun.HaibunMenteView), addInfo:"準備中"),
			new("自動発注・補充対象除外品設定", typeof(Views._07Haibun.AutoHachuHojunExcludeSettingView), addInfo:"準備中"),
			new("在庫基準自動補充メンテナンス", typeof(Views._07Haibun.ZaikoAutoHojunMenteView), addInfo:"準備中"),
		])),
		new("在庫管理", new([
			new("棚卸入力", typeof(Views._08Zaiko.StockInputView), addInfo:"準備中"),
			new("移動入力(即時)", typeof(Views._08Zaiko.IdoInputSokuView), addInfo:"準備中"),
			new("移動入力(積送)", typeof(Views._08Zaiko.IdoInputOutView), addInfo:"準備中"),
			new("移動受入力", typeof(Views._08Zaiko.IdoInputUkeView), addInfo:"準備中"),
			new("棚卸差異問合せ", typeof(Views._08Zaiko.StockDifferenceQueryView), addInfo:"準備中"),
			new("在庫問合せ", typeof(Views._08Zaiko.ZaikoQueryView), addInfo:"準備中"),
			new("商品履歴問合せ", typeof(Views._08Zaiko.ShohinHistoryQueryView), addInfo:"準備中"),
			new("棚卸入力(一覧方式)", typeof(Views._08Zaiko.StockInputListView), addInfo:"準備中"),
			new("在庫強制調整入力", typeof(Views._08Zaiko.StockForceInputView), addInfo:"準備中"),
			new("在庫移動入力", typeof(Views._08Zaiko.StockIdoInputView), addInfo:"準備中"),
			new("倉庫分類別棚卸表", typeof(Views._08Zaiko.SokoCategoryStockListView), addInfo:"準備中"),
			new("倉庫別受払表", typeof(Views._08Zaiko.SokoInOutReportView), addInfo:"準備中"),
			new("商品別受払表", typeof(Views._08Zaiko.ShohinInOutReportView), addInfo:"準備中"),
			new("倉庫別在庫集計表", typeof(Views._08Zaiko.SokoSummaryReportView), addInfo:"準備中"),
			new("汎用在庫表", typeof(Views._08Zaiko.GeneralStockTableView), addInfo:"準備中"),
			new("棚卸明細表", typeof(Views._08Zaiko.StockMeisaiTableView), addInfo:"準備中"),
			new("棚卸日一括メンテナンス", typeof(Views._08Zaiko.StockDateBulkMenteView), addInfo:"準備中"),
			new("棚卸チェックリスト", typeof(Views._08Zaiko.StockCheckListView), addInfo:"準備中"),
			new("品番別移動チェックリスト", typeof(Views._08Zaiko.HinbanIdoCheckListView), addInfo:"準備中"),
			new("移動未受リスト", typeof(Views._08Zaiko.IdoUnreceivedListView), addInfo:"準備中"),
		])),
		new("売上分析", new([
			new("販売動向表", typeof(Views._20UriageAnalysis.SalesTrendReportView), addInfo:"準備中"),
			new("品番別販売動向表", typeof(Views._20UriageAnalysis.HinbanSalesTrendReportView), addInfo:"準備中"),
			new("投入売上在庫表", typeof(Views._20UriageAnalysis.InputSalesStockReportView), addInfo:"準備中"),
			new("ベスト表", typeof(Views._20UriageAnalysis.BestSalesReportView), addInfo:"準備中"),
			new("商品消化率表", typeof(Views._20UriageAnalysis.ShohinTurnoverRateReportView), addInfo:"準備中"),
			new("セット売上分析表", typeof(Views._20UriageAnalysis.SetSalesAnalysisReportView), addInfo:"準備中"),
			new("店別売上日報", typeof(Views._20UriageAnalysis.ShopSalesDailyView), addInfo:"準備中"),
			new("店舗別売上日計表", typeof(Views._20UriageAnalysis.ShopSalesDailySummaryView), addInfo:"準備中"),
			new("売上速報", typeof(Views._20UriageAnalysis.SalesQuickReportView), addInfo:"準備中"),
			new("売上週報･月報", typeof(Views._20UriageAnalysis.UriageShuhouGeppouView), addInfo:"準備中"),
			new("売上予算構成比", typeof(Views._20UriageAnalysis.SalesBudgetRatioReportView), addInfo:"準備中"),
			new("分類別売上消化率表", typeof(Views._20UriageAnalysis.CategorySalesConsumptionRateView), addInfo:"準備中"),
			new("分類別店別売上報告", typeof(Views._20UriageAnalysis.CategoryShopSalesReportView), addInfo:"準備中"),
			new("店舗売上ランキング表", typeof(Views._20UriageAnalysis.ShopSalesRankingReportView), addInfo:"準備中"),
		])),
		new("卸・販売員・経営分析", new([
			new("得意先別売上日報", typeof(Views._21OroshiAnalysis.TokuiSalesDailyReportView), addInfo:"準備中"),
			new("得意先別売上月報", typeof(Views._21OroshiAnalysis.TokuiSalesMonthlyReportView), addInfo:"準備中"),
			new("担当別売上実績半期報", typeof(Views._21OroshiAnalysis.TantoSalesHalfYearReportView), addInfo:"準備中"),
			new("担当得意先別予算実績対比表", typeof(Views._21OroshiAnalysis.TantoTokuiBudgetActualReportView), addInfo:"準備中"),
			new("個人売上ランキング表", typeof(Views._21OroshiAnalysis.PersonalSalesRankingReportView), addInfo:"準備中"),
			new("販売員別予算実績対比表", typeof(Views._21OroshiAnalysis.SalesStaffBudgetVsActualReportView), addInfo:"準備中"),
			new("半期報", typeof(Views._21OroshiAnalysis.HalfYearReportView), addInfo:"準備中"),
			new("全社受払表", typeof(Views._21OroshiAnalysis.CorporateInOutReportView), addInfo:"準備中"),
			new("卸・店舗売上実績表", typeof(Views._21OroshiAnalysis.OroshiShopSalesActualReportView), addInfo:"準備中"),
		])),
		new("C.P.A", new([
			new("★T.L - アナライザー★", typeof(Views._22CPA.TimelineAnalyzerView), addInfo:"準備中"),
			new("★C.G - アナライザー★", typeof(Views._22CPA.CategoryGroupAnalyzerView), addInfo:"準備中"),
			new("ナンでも？CSV", typeof(Views._22CPA.AnyCsvView), addInfo:"準備中"),
			new("ABC分析(全社)", typeof(Views._22CPA.AbcAnalysisCorporateView), addInfo:"準備中"),
			new("ABC分析(店舗)", typeof(Views._22CPA.AbcAnalysisShopView), addInfo:"準備中"),
			new("在庫データ出力", typeof(Views._22CPA.StockDataOutputView), addInfo:"準備中"),
			new("在庫受け払い照会", typeof(Views._22CPA.StockInOutInquiryView), addInfo:"準備中"),
			new("販売動向ビュー", typeof(Views._22CPA.SalesTrendView), addInfo:"準備中"),
			new("店舗稼動ビュー", typeof(Views._22CPA.ShopActivityView), addInfo:"準備中"),
			new("売消台帳ビュー", typeof(Views._22CPA.SalesConsumptionLedgerView), addInfo:"準備中"),
			new("商品分析", typeof(Views._22CPA.ShohinAnalysisView), addInfo:"準備中"),
			new("オンラインモニタ", typeof(Views._22CPA.RealTimeMonitorView), addInfo:"準備中"),
			new("売上・在庫問合せ", typeof(Views._22CPA.SalesStockQueryView), addInfo:"準備中"),
			new("ベストレポート", typeof(Views._22CPA.BestSalesReportView), addInfo:"準備中"),
		])),
		new("HHT / POS連携", new([
			new("HHT用マスタデータ作成", typeof(Views._30HHT.HhtMasterDataCreateView), addInfo:"CSV または固定長で HHT マスタを出力"),
			new("HHT手動データ受信", typeof(Views._30HHT.HhtManualDataReceiveView), addInfo:"受信フォルダ内の HHT データを手動取込"),
			new("HHTエラーデータ修正入力", typeof(Views._30HHT.HhtErrorDataInputView), addInfo:"準備中"),
			new("HHTデータ更新", typeof(Views._30HHT.HhtDataUpdateView), addInfo:"準備中"),
			new("HHT未更新データ印刷", typeof(Views._30HHT.HhtUnupdatedDataPrintView), addInfo:"準備中"),
			new("HHT未更新データ一括削除", typeof(Views._30HHT.HhtUnupdatedDataDeleteView), addInfo:"準備中"),
			new("出荷指示明細書印刷", typeof(Views._30HHT.ShippingConfirmDetailPrintView), addInfo:"準備中"),
			new("移動明細書印刷", typeof(Views._30HHT.IdoDetailBookPrintView), addInfo:"準備中"),
			new("即時移動明細書", typeof(Views._30HHT.IdoSokuDetailBookPrintView), addInfo:"準備中"),
			new("HHT手動データ受信(ﾃﾞｰﾀ送信後)", typeof(Views._30HHT.HhtManualDataReceive2View), addInfo:"準備中"),
			new("HHT用マスタバーコード印刷", typeof(Views._30HHT.HhtMasterBarcodePrintView), addInfo:"準備中"),
		])),
		new("月次 / 更新処理", new([
			new("請求計算", typeof(Views._31Monthly.BillingCalculationView), addInfo:"準備中"),
			new("支払計算", typeof(Views._31Monthly.PaymentCalculationView), addInfo:"準備中"),
			new("棚卸開始処理", typeof(Views._31Monthly.StockTakeInitiationView), addInfo:"準備中"),
			new("棚卸確定", typeof(Views._31Monthly.StockTakeFinalizationView), addInfo:"準備中"),
			new("在庫・掛再更新", typeof(Views._31Monthly.StockKakeUpdateView), addInfo:"準備中"),
			new("在庫累計更新", typeof(Views._31Monthly.StockRuikeiUpdateView), addInfo:"準備中"),
			new("締日更新", typeof(Views._31Monthly.ShimebiUpdateView), addInfo:"準備中"),
			new("諸掛更新", typeof(Views._31Monthly.SundryChargesUpdateView), addInfo:"準備中"),
			new("一時処理用(管理者用)", typeof(Views._31Monthly.TemporaryProcessingView), addInfo:"準備中"),
			new("残高登録処理", typeof(Views._31Monthly.BalanceRegistrationView), addInfo:"準備中"),
			new("データ整理更新", typeof(Views._31Monthly.DataCleanupUpdateView), addInfo:"準備中"),
			new("消費税再計算", typeof(Views._31Monthly.TaxRecalculationView), addInfo:"準備中"),
			new("最終仕入原価更新", typeof(Views._31Monthly.LastPurchaseCostRefreshView), addInfo:"準備中"),
			new("総平均原価更新", typeof(Views._31Monthly.TotalAverageCostUpdateView), addInfo:"準備中"),
			new("消化仕入更新", typeof(Views._31Monthly.ConsumptionPurchaseUpdateView), addInfo:"準備中"),
			new("積送中クリア", typeof(Views._31Monthly.InTransitClearView), addInfo:"準備中"),
			new("月間データ集計", typeof(Views._31Monthly.MonthlyDataSummaryView), addInfo:"準備中"),
			new("自動発注・補充の実行", typeof(Views._31Monthly.AutoOrderReplenishExecuteVie), addInfo:"準備中"),
		])),
		new("Loyal Customer", new([
			new("顧客マスタ", typeof(Views._32LoyalCustomer.CustomerMasterView), addInfo:"準備中"),
			new("ポイントマスタ（ベース）（管理者用)", typeof(Views._32LoyalCustomer.PointMasterBaseAdminView), addInfo:"準備中"),
			new("ポイントマスタ（キャンペーン）", typeof(Views._32LoyalCustomer.PointMasterCampaignView), addInfo:"準備中"),
			new("ポイントマスタ（ボーナス）", typeof(Views._32LoyalCustomer.PointMasterBonusView), addInfo:"準備中"),
			new("店舗別キャンペーン設定", typeof(Views._32LoyalCustomer.ShopCampaignSettingView), addInfo:"準備中"),
			new("商品店舗別ポイント設定", typeof(Views._32LoyalCustomer.ShohinShopPointSettingView), addInfo:"準備中"),
			new("ポイント集計", typeof(Views._32LoyalCustomer.PointSummaryView), addInfo:"準備中"),
			new("顧客カルテ", typeof(Views._32LoyalCustomer.EndCustomerProfileView), addInfo:"準備中"),
			new("RFMクロス分析表", typeof(Views._32LoyalCustomer.RfmCrossAnalysisTableView), addInfo:"準備中"),
		])),
		new("店舗", new([
			new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), false),
			new("棚卸明細表(原価無)", typeof(Views._40Shop.StockTakeDetailReportCostlessView), addInfo:"準備中"),
			new("汎用在庫表(原価無)", typeof(Views._40Shop.GeneralInventoryTableCostlessView), addInfo:"準備中"),
			new("売上速報(原価無)", typeof(Views._40Shop.SalesQuickReportCostlessView), addInfo:"準備中"),
			new("売上週報･月報(原価無)", typeof(Views._40Shop.SalesWeeklyMonthlyReportCostlessView), addInfo:"準備中"),
			new("分類別店別売上報告(原価無)", typeof(Views._40Shop.CategoryStoreSalesReportCostlessView), addInfo:"準備中"),
		])),
		new("物流", new([
			new("マスタデータ作成", typeof(Views._41Logistics.LogisticsMasterDataCreateView), addInfo:"準備中"),
			new("連携データ手動送信", typeof(Views._41Logistics.IntegrationDataManualTransmitView), addInfo:"準備中"),
			new("連携データ手動受信", typeof(Views._41Logistics.IntegrationDataManualReceiveView), addInfo:"準備中"),
			new("連携エラーデータ照会", typeof(Views._41Logistics.IntegrationErrorDataQueryView), addInfo:"準備中"),
		])),
	]);
	}
}
