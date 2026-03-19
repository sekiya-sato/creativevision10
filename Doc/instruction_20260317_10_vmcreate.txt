# Cvnet10Wpfclient Views/ViewModels フォルダ以下へのViewおよびViewModelの作成、およびMenuData.csの修正

`Cvnet10Wpfclient` の `Views` と `ViewModels` で、これから開発していく初期のViewのxaml およびViewModelのcsファイルを生成する。(View名のリストからViewModel名は自動で推論)

## 作成する初期Viewのxaml
[View名].xaml
```
<helpers:BaseWindow x:Class="Cvnet10Wpfclient.Views._[フォルダ名].[View名]"
	xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
	xmlns:behaviors="http://schemas.microsoft.com/xaml/behaviors" xmlns:d="http://schemas.microsoft.com/expression/blend/2008" xmlns:helpers="clr-namespace:Cvnet10Wpfclient.Helpers"
	xmlns:local="clr-namespace:Cvnet10Wpfclient.Views" xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
	xmlns:vm="clr-namespace:Cvnet10Wpfclient.ViewModels._[フォルダ名]"
	Title="[Viewの日本語名]"
	Width="1244" Height="900"
	Background="{DynamicResource AppCommonBackgroundBrush}"
	Foreground="{DynamicResource MaterialDesignBody}"
	TextOptions.TextFormattingMode="Display"
	mc:Ignorable="d">
	<helpers:BaseWindow.DataContext>
		<vm:[View名]Model />
	</helpers:BaseWindow.DataContext>
	<Grid></Grid>
</helpers:BaseWindow>

```
[View名].xaml..cs
```
namespace Cvnet10Wpfclient.Views._[フォルダ名];

public partial class [View名] : Helpers.BaseWindow {
	public [View名]() {
		InitializeComponent();
	}
}
```


## 作成する初期ViewModelのcs
[ViewModel名].cs
```
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using System.Diagnostics;

namespace Cvnet10Wpfclient.ViewModels._[フォルダ名];

public partial class [ViewModel名] : Helpers.BaseViewModel {
}

```

## 作成対象リスト

- フォルダ `00System`
	- `SysAutoExecHistoryView` 自動実行履歴
- フォルダ `01Master`
	- `MasterJouDaiBulkChangeView` 上代一括変更
	- `ImportTemplateCreateView` 取込レイアウト作成
	- `ExternalCsvImportView` 外部CSVマスタ取込
	- `GenkaChangeEntryView` 原価変更登録
	- `ProductRatingChangeView` 評価替
	- `AutoExecKanriMasterView` 自動実行管理マスタ
	- `AutoExecScheduleSettingView` 自動実行スケジュール設定
- フォルダ `02Yosan`
	- `ShopBrandBudgetMasterView` 店ブランド予算マスタ
	- `SalesStaffBudgetMasterView` 販売員別予算マスタ
	- `SalesRepBudgetMasterView` 営業担当別予算マスタ
	- `ShopBudgetReportView` 店舗予算表
	- `ShopBrandBudgetVsActualView` 店舗ブランド別予算実績対比
	- `DailyShopBudgetReportView` 日別店別予算表
	- `SalesStaffBudgetReportView` 販売員予算表
- フォルダ `03Hatchu`
	- `HachuInputView` 発注入力
	- `HachuHaibunInputView` 発注配分入力
	- `DeliveryScheduleTableView` 納品予定表
	- `SupplierHachuTableView` 仕入先別発注表
	- `ShohinHachuTableView` 商品別発注表
	- `ShohinHachuSummaryTableView` 商品別発注集計表
	- `DeliveryScheduleInquiryView` 納品予定照会
	- `PendingShiireListView` 仕入未受リスト
	- `HachuFormView` 発注書
	- `HachuHaibunListView` 発注配分リスト
	- `HachuZanKanriTableView` 発注残管理表
	- `HachuZanCompletionSettingView` 発注残完了設定
- フォルダ `04Juchu`
	- `JuchuInputView` 展示会受注入力
	- `NouhinYoteiTableView` 納品予定表
	- `TokuiSakiJuchuTableView` 得意先別受注表
	- `ShouhinJuchuTableView` 商品別受注表
	- `ShouhinJuchuSummaryTableView` 商品別受注集計表
	- `JuchuZanKanriTableView` 受注残管理表
	- `JuchuZanCompletionSettingView` 受注残完了設定
	- `TenjiSwatchView` 展示会スワッチ
	- `SwatchDataCreateView` スワッチデータ作成
	- `SwatchDataBulkCreateView` スワッチデータ一括作成
	- `DesignListTableView` 絵型一覧表
	- `TokuiSakiUriageYoteiTableView` 得意先別売上予定表
	- `TantoTenjiJuchuGoukeiTableView` 担当別展示会受注合計表
	- `JuchuBestTableView` 受注ベスト表
	- `HaibunShukkaListView` 配分出荷リスト
- フォルダ `05Shiire`
	- `ShiireInputView` 商品仕入入力
	- `HenpinInputView` 仕入返品入力
	- `HinbanShiireCheckListView` 品番別仕入チェックリスト
	- `BrandShiireKingakuTableView` ブランド別仕入金額表
	- `ShiireSlipPrintView` 仕入伝票印刷
	- `ShiharaiInputView` 支払入力
	- `ShiharaiMatchingView` 支払消込
	- `ShiireLedgerView` 仕入先元帳
	- `KaikakeBalanceReportView` 買掛金管理表
	- `ShiharaiListReportView` 支払一覧表
	- `MonthlyShiharaiYoteiTableView` 月別支払予定表
	- `ShiharaiBalanceDetailView` 支払残高明細書
	- `ShiireTrendReportView` 仕入先別仕入推移表
- フォルダ `06Uriage`
	- `ShukkaUriageInputView` 出荷・売上入力
	- `ShopUriageInputView` 店舗売上入力
	- `PosDailySeisanInputView` POS日別精算入力
	- `UriageCashTypeReportView` 売上金種Viewer
	- `HinbanUriageCheckListView` 品番別売上チェックリスト
	- `UriageCheckListView` 売上チェックリスト
	- `NouhinBookPrintView` 納品書印刷
	- `NouhinBookPrintCustomView` 納品書印刷(専用伝票)
	- `NouhinBookPendingCheckListView` 納品書未発行チェックリスト
	- `NyukinInputView` 入金入力
	- `NyukinMatchingView` 入金消込
	- `TokuiLedgerView` 得意先元帳
	- `UrikakeBalanceReportView` 売掛金管理表
	- `SeikyuListReportView` 請求一覧表
	- `MonthlyNyukinYoteiTableView` 月別入金予定表
	- `SeikyuBalanceDetailView` 請求書印刷
	- `TokuiTrendReportView` 得意先別売上推移表
- フォルダ `07Haibun`
	- `ShopHaibunInputView` 店舗配分入力
	- `JuchuHaibunInputView` 受注配分入力
	- `ShopShippingRequestView` 店舗出荷依頼
	- `ZaikoHinHaibunView` 在庫品配分
	- `TokuiHaibunInputView` 得意先別配分入力
	- `ShippingConfirmShohinView` 出荷指示確定(商品)
	- `ShippingConfirmTokuiView` 出荷指示確定(得意先)
	- `ShippingInputView` 出荷処理入力
	- `HaibunDataMenteView` 配分データメンテ
	- `ReservationInputView` 取置入力
	- `IdoInstructionSkuView` 移動指示(SKU)
	- `IdoInstructionShohinView` 移動指示(商品)
	- `ShippingConfirmDetailPrintView` 出荷指示明細書印刷
	- `ShippingListReportView` 納入一覧表
	- `ShippingConfirmListView` 出荷指示一覧印刷
	- `HaibunMenteView` 配分関連メンテナンス
	- `AutoHachuHojunExcludeSettingView` 自動発注・補充対象除外品設定
	- `ZaikoAutoHojunMenteView` 在庫基準自動補充メンテナンス
- フォルダ `08Zaiko`
	- `StockInputView` 棚卸入力
	- `IdoInputSokuView` 移動入力(即時)
	- `IdoInputOutView` 移動入力(積送)
	- `IdoInputUkeView` 移動受入力
	- `StockDifferenceQueryView` 棚卸差異問合せ
	- `ZaikoQueryView` 在庫問合せ
	- `ShohinHistoryQueryView` 商品履歴問合せ
	- `StockInputListView` 棚卸入力(一覧方式)
	- `StockForceInputView` 在庫強制調整入力
	- `StockIdoInputView` 在庫移動入力
	- `SokoCategoryStockListView` 倉庫分類別棚卸表
	- `SokoInOutReportView` 倉庫別受払表
	- `ShohinInOutReportView` 商品別受払表
	- `SokoSummaryReportView` 倉庫別在庫集計表
	- `GeneralStockTableView` 汎用在庫表
	- `StockMeisaiTableView` 棚卸明細表
	- `StockDateBulkMenteView` 棚卸日一括メンテナンス
	- `StockCheckListView` 棚卸チェックリスト
	- `HinbanIdoCheckListView` 品番別移動チェックリスト
	- `IdoUnreceivedListView` 移動未受リスト
- フォルダ `20UriageAnalysis`
	- `SalesTrendReportView` 販売動向表
	- `HinbanSalesTrendReportView` 品番別販売動向表
	- `InputSalesStockReportView` 投入売上在庫表
	- `BestSalesReportView` ベスト表
	- `ShohinTurnoverRateReportView` 商品消化率表
	- `SetSalesAnalysisReportView` セット売上分析表
	- `ShopSalesDailyView` 店別売上日報
	- `ShopSalesDailySummaryView` 店舗別売上日計表
	- `SalesQuickReportView` 売上速報
	- `UriageShuhouGeppouView` 売上週報･月報
	- `SalesBudgetRatioReportView` 売上予算構成比
	- `CategorySalesConsumptionRateView` 分類別売上消化率表
	- `CategoryShopSalesReportView` 分類別店別売上報告
	- `ShopSalesRankingReportView` 店舗売上ランキング表
- フォルダ `21OroshiAnalysis`
	- `TokuiSalesDailyReportView` 得意先別売上日報
	- `TokuiSalesMonthlyReportView` 得意先別売上月報
	- `TantoSalesHalfYearReportView` 担当別売上実績半期報
	- `TantoTokuiBudgetActualReportView` 担当得意先別予算実績対比表
	- `PersonalSalesRankingReportView` 個人売上ランキング表
	- `SalesStaffBudgetVsActualReportView` 販売員別予算実績対比表
	- `HalfYearReportView` 半期報
	- `CorporateInOutReportView` 全社受払表
	- `OroshiShopSalesActualReportView` 卸・店舗売上実績表
- フォルダ `22CPA`
	- `TimelineAnalyzerView` ★T.L - アナライザー★
	- `CategoryGroupAnalyzerView` ★C.G - アナライザー★
	- `AnyCsvView` ナンでも？CSV
	- `AbcAnalysisCorporateView` ABC分析(全社)
	- `AbcAnalysisShopView` ABC分析(店舗)
	- `StockDataOutputView` 在庫データ出力
	- `StockInOutInquiryView` 在庫受け払い照会
	- `SalesTrendView` 販売動向ビュー
	- `ShopActivityView` 店舗稼動ビュー
	- `SalesConsumptionLedgerView` 売消台帳ビュー
	- `ShohinAnalysisView` 商品分析
	- `RealTimeMonitorView` オンラインモニタ
	- `SalesStockQueryView` 売上・在庫問合せ
	- `BestSalesReportView` ベストレポート
- フォルダ `30HHT`
	- `HhtMasterDataCreateView` HHT用マスタデータ作成(cvnetcom)
	- `HhtManualDataReceiveView` HHT手動データ受信
	- `HhtErrorDataInputView` HHTエラーデータ修正入力
	- `HhtDataUpdateView` HHTデータ更新
	- `HhtUnupdatedDataPrintView` HHT未更新データ印刷
	- `HhtUnupdatedDataDeleteView` HHT未更新データ一括削除
	- `ShippingConfirmDetailPrintView` 出荷指示明細書印刷
	- `IdoDetailBookPrintView` 移動明細書印刷
	- `IdoSokuDetailBookPrintView` 即時移動明細書
	- `HhtManualDataReceive2View` HHT手動データ受信(ﾃﾞｰﾀ送信後)
	- `HhtMasterBarcodePrintView` HHT用マスタバーコード印刷
- フォルダ `31Monthly`
	- `BillingCalculationView` 請求計算
	- `PaymentCalculationView` 支払計算
	- `StockTakeInitiationView` 棚卸開始処理
	- `StockTakeFinalizationView` 棚卸確定
	- `StockKakeUpdateView` 在庫・掛再更新
	- `StockRuikeiUpdateView` 在庫累計更新
	- `ShimebiUpdateView` 締日更新
	- `SundryChargesUpdateView` 諸掛更新
	- `TemporaryProcessingView` 一時処理用(管理者用)
	- `BalanceRegistrationView` 残高登録処理
	- `DataCleanupUpdateView` データ整理更新
	- `TaxRecalculationView` 消費税再計算
	- `LastPurchaseCostRefreshView` 最終仕入原価更新
	- `TotalAverageCostUpdateView` 総平均原価更新
	- `ConsumptionPurchaseUpdateView` 消化仕入更新
	- `InTransitClearView` 積送中クリア
	- `MonthlyDataSummaryView` 月間データ集計
	- `AutoOrderReplenishExecuteVie` 自動発注・補充の実行
- フォルダ `32LoyalCustomer`
	- `CustomerMasterView` 顧客マスタ
	- `PointMasterBaseAdminView` ポイントマスタ（ベース）（管理者用)
	- `PointMasterCampaignView` ポイントマスタ（キャンペーン）
	- `PointMasterBonusView` ポイントマスタ（ボーナス）
	- `ShopCampaignSettingView` 店舗別キャンペーン設定
	- `ShohinShopPointSettingView` 商品店舗別ポイント設定
	- `PointSummaryView` ポイント集計
	- `EndCustomerProfileView` 顧客カルテ
	- `RfmCrossAnalysisTableView` RFMクロス分析表
- フォルダ `40Shop`
	- `ShopUriageInputView` 店舗売上入力
	- `StockTakeDetailReportCostlessView` 棚卸明細表(原価無)
	- `GeneralInventoryTableCostlessView` 汎用在庫表(原価無)
	- `SalesQuickReportCostlessView` 売上速報(原価無)
	- `SalesWeeklyMonthlyReportCostlessView` 売上週報･月報(原価無)
	- `CategoryStoreSalesReportCostlessView` 分類別店別売上報告(原価無)
- フォルダ `41Logistics`
	- `LogisticsMasterDataCreateView` マスタデータ作成
	- `IntegrationDataManualTransmitView` 連携データ手動送信
	- `IntegrationDataManualReceiveView` 連携データ手動受信
	- `IntegrationErrorDataQueryView` 連携エラーデータ照会

## 作成後、指示ファイルと齟齬がないかチェック

## MenuData.cs の修正
- MenuData.cs の CreateDefault()内の項目を修正。`typeof(object)` を作成したViewで置き換え
- 例
元の列 ```
new("自動実行履歴", typeof(object), false, addInfo:"準備中"),
```
修正後 ```
new("自動実行履歴", typeof(SysAutoExecHistoryView), false, addInfo:"準備中"), // SysAutoExecHistoryView
```
`typeof([View名])` と 列の最後のコメントを追加 `// [View名]`


## 最後にBuildしエラーがなければdocument作成し、未commitのものを全てcommitしpushする
