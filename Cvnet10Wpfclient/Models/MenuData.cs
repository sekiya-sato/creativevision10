using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Wpfclient.Views;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.Models;

public partial class MenuData : ObservableObject {
	[ObservableProperty]
	private string? header;

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
	public Type? ViewType { get; set; }
	public bool IsDialog { get; set; } = true;
	public int InitParam { get; set; }

	public static ObservableCollection<MenuData> CreateDefault() {
		return new([
		new () {
			Header = "■ 管理メニュー / 接続テスト",
			SubItems = new () {
				new () {
					Header = "ログイン",
					AddInfo = "gRPC",
					ViewType = typeof(Views._00System.LoginView),
					IsDialog = true,
					InitParam = 0
				},
				new () {
					Header = "リフレッシュ",
					AddInfo = "gRPC",
					ViewType = typeof(Views._00System.LoginView),
					IsDialog = true,
					InitParam = 1
				},
				new () {
					Header = "環境設定",
					AddInfo = "構成",
					ViewType = typeof(Views._00System.SysSetConfigView),
					IsDialog = true
				},
				new () {
					Header = "サンプル画面 ---",
					AddInfo = "gRPC",
					ViewType = typeof(SampleView),
					IsDialog = false
				},
				new () {
					Header = "システム管理マスタ",
					AddInfo = "gRPC",
					ViewType = typeof(Views._01Master.MasterSysKanriMenteView),
					IsDialog = false
				},
				new () {
					Header = "ログイン管理マスタ",
					AddInfo = "gRPC",
					ViewType = typeof(Views._00System.SysLoginView),
					IsDialog = false
				},
				new () {
					Header = "LOGIN履歴情報",
					AddInfo = "gRPC",
					ViewType = typeof(Views._00System.SysLoginHistoryView),
					IsDialog = false
				},
			}
		},
		new () {
			Header = "■ Master",
			SubItems = new () {
				new() {
					Header = "名称マスタメンテ",
					AddInfo = "Generic name setup",
					ViewType = typeof(Views._01Master.MasterMeishoMenteView),
					IsDialog = false
				},
			new() {
				Header="社員マスタ",
				ViewType = typeof(Views._01Master.MasterShainMenteView),
				IsDialog = false,
			},
			new() {
				Header="得意先マスタメンテ",
				ViewType = typeof(Views._01Master.MasterTokuiMenteView),
				IsDialog = false,
			},
			new() {
				Header = "顧客マスタメンテ",
					AddInfo = "準備中",
					ViewType = typeof(Views._01Master.MasterEndCustomerMenteView),
					IsDialog = false
				},
				new() {
					Header="商品マスタ",
					ViewType = typeof(Views._01Master.MasterShohinMenteView),
					IsDialog = false,
				},
			}
		},
        /* ================================ */
		new() {
			Header = "ここから下はバックアップのメニューデータ----- backupdata",
					Icon = "/Images/menu_icon/download_icon.png",
					IsExpand = true,
					// IsMainMenu = true,
					IsDialog = true,
					SubItems = new ()
					{
						new (){
							Header="開始処理：ログイン",
							ViewType = typeof(Views._00System.LoginView),
							IsDialog = true,
						},
						new (){
							Header="開始処理：マスタダウンロード(全件)",
							// ViewType = typeof(Views.MasterDownloadView),
							IsDialog = true,
						},
						new (){
							Header="開始処理：マスタダウンロード(差分)",
							// ViewType = typeof(Views.MasterDownloadView),
							InitParam = 1,
							IsDialog = true,
						},
						new (){
							Header="終了処理：データアップロード",
							IsDialog = true,
						},
					}
				},
				new ()
				{
					Header = "マスタ",
					Icon = "/Images/menu_icon/master_icon.png",
					SubItems = new ()
					{
						new (){
							Header="名称マスタメンテ",
							ViewType = typeof(Views._01Master.MasterMeishoMenteView),
							IsDialog = false,
						},
						new (){
							Header="得意先マスタメンテVer1",
							// ViewType = typeof(Views.MasterShopMente2View),
							IsDialog = false,
						},
						new (){
							Header="得意先マスタメンテVer2",
							// ViewType = typeof(Views.MasterShopMente3View),
							IsDialog = false,
						},
						new (){
							Header="得意先マスタメンテVer3",
							// ViewType = typeof(Views.MasterShopMente4View),
							IsDialog = false,
						},
						new (){
							Header="仕入先マスタメンテ",
                            //ViewType = typeof(Views.MasterShiireMenteView),
                            // ViewType = typeof(Views.MasterShiireMente2View),
							IsDialog = false,
						},
							new (){
							Header="商品マスタ(新規登録) / (照会・修正）",
							// ViewType = typeof(Views.MasterShohinMenteView),
						},
							new (){
							Header="商品マスタ絵型更新",
						},
							new (){
							Header="生地付属マスタ",
							// ViewType = typeof(Views.MasterShKijiMenteView),
							IsDialog = false,
						},
							new (){
							Header="上代一括変更",
						},
							new (){
							Header="上代一括変更取込",
						},
							new (){
							Header="売価一覧印刷",
						},
							new (){
							Header="原価変更登録",
						},
							new (){
							Header="評価替",
						},
							new (){
							Header="評価替（原価基準)",
						},
							new (){
							Header="コンバートマスタ",
						},
							new (){
							Header="社員マスタ",
							// ViewType = typeof(Views.MasterShainMenteView),
							IsDialog = false,
						},
							new (){
							Header="社員証印刷(Felica用)",
						},
					}
				},
				new ()
				{
					Header = "マスタ補助",
					Icon = "/Images/menu_icon/master_support.png",
					SubItems = new ()
					{
						new (){
							Header="名称マスタコピー作成",
						},
						new (){
							Header="タックシール印刷",
						},
						new (){
							Header="取込レイアウト作成",
						},
						new (){
							Header="外部CSVデータ取込",
						},
						new (){
							Header="下札発行用CSVデータ作成",
						},
						new (){
							Header="JAN一括設定(49JAN含む)",
						},
						new (){
							Header="マスタ復帰処理",
						},
						new (){
							Header="商品色サイズメンテ",
						},
						new (){
							Header="社員使用メニュー一覧",
						},
						new (){
							Header="端末UUID設定(Felica用)",
						},
						new (){
							Header="週マスタ",
						},
						new (){
							Header="バーコードブック",
						},
						new (){
							Header="各種マスタ印刷",
						},
						new (){
							Header="各種伝票印刷",
						},
					}
				},
				new ()
				{
					Header = "管理メニュー",
					Icon = "/Images/menu_icon/manage_icon.png",
					SubItems = new ()
					{
						new (){
							Header="社員LOGINマスタ(管理者用)",
							ViewType = typeof(Views._00System.SysLoginView),
							IsDialog = false,
						},
						new (){
							Header="HHT用管理マスタ",
						},
						new (){
							Header="システム管理マスタ(管理者用)",
							 ViewType = typeof(Views._01Master.MasterSysKanriMenteView),
							IsDialog = false,
						},
						new (){
							Header="社員LOGINマスタ一覧",
						},
						new (){
							Header="名称マスタ(管理者用)",
						},
						new (){
							Header="処理履歴情報(管理者用)",
						},
						new (){
							Header="自動実行履歴(管理者用)",
						},
						new (){
							Header="LOGIN履歴情報(管理者用)",
						},
						new (){
							Header="汎用ファイルメンテ(管理者用)",
						},
						new (){
							Header="フラグメンテナンス（DTP専用)",
						},
						new (){
							Header="システムメンテナンス処理",
						},
						new (){
							Header="汎用SQL問い合わせ(管理者用)",
						},
						new (){
							Header="DB定義書出力",
						},
						new (){
							Header="帳票管理マスタ",
						},
						new (){
							Header="ラベル名称マスタ",
						},
						new (){
							Header="手動更新履歴(管理者用)",
						},
						new (){
							Header="自動実行管理マスタ(管理者用)",
						},
						new (){
							Header="自動実行スケジュール設定",
						},
					},
				},
				new ()
				{
					Header = "予算",
					Icon = "/Images/menu_icon/budget_icon.png",
					SubItems = new ()
					{
						new (){
							Header="店ブランド予算マスタ(月)",
						},
						new (){
							Header="販売員別予算マスタ(月)",
						},
						new (){
							Header="店別ブランド別予算マスタ",
						},
						new (){
							Header="販売員別予算マスタ",
						},
						new (){
							Header="営業担当別予算マスタ",
						},
						new (){
							Header="店舗予算表",
						},
						new (){
							Header="店舗別予算実績対比",
						},
						new (){
							Header="店舗ブランド別予算実績対比",
						},
						new (){
							Header="日別店別予算表",
						},
						new (){
							Header="販売員予算表",
						},
					}
				},
				new ()
				{
					Header = "発注",
					Icon = "/Images/menu_icon/order_icon.png",
					SubItems = new ()
					{
						new (){
							Header="契約発注入力",
						},
						new (){
							Header="発注配分入力",
						},
						new (){
							Header="発注入力",
							// ViewType = typeof(Views.HatchuInputView),
							IsDialog = false,
						},
						new (){
							Header="発注配分入力(簡易版)",
						},
						new (){
							Header="納品予定表",
						},
						new (){
							Header="仕入先別発注表",
						},
						new (){
							Header="商品別発注表",
						},
						new (){
							Header="商品別発注集計表",
						},
						new (){
							Header="週間納品予定表",
						},
						new (){
							Header="納品予定照会",
						},
						new (){
							Header="仕入未受リスト",
						},
						new (){
							Header="発注書",
						},
						new (){
							Header="発注配分リスト",
						},
						new (){
							Header="受注発注連携更新",
						},
						new (){
							Header="契約残管理表",
						},
						new (){
							Header="契約残完了設定",
						},
						new (){
							Header="発注残管理表",
						},
						new (){
							Header="発注残完了設定",
						},
					}
				},
				new ()
				{
					Header = "受注 / 展示会",
					Icon = "/Images/menu_icon/order_manage_icon.png",
					SubItems = new ()
					{
						new (){
							Header="展示会受注入力",
						},
						new (){
							Header="納品予定表",
						},
						new (){
							Header="得意先別受注表",
						},
						new (){
							Header="商品別受注表",
						},
						new (){
							Header="商品別受注集計表",
						},
						new (){
							Header="受注残管理表",
						},
						new (){
							Header="受注残完了設定",
						},
						new (){
							Header="展示会スワッチ",
						},
						new (){
							Header="スワッチデータ作成",
						},
						new (){
							Header="スワッチデータ一括作成",
						},
						new (){
							Header="絵型一覧表",
						},
						new (){
							Header="得意先別売上予定表",
						},
						new (){
							Header="担当別展示会受注合計表",
						},
						new (){
							Header="受注ベスト表",
						},
						new (){
							Header="配分出荷リスト",
						},
						new (){
							Header="バーコードブック発行",
						},
					}
				},
				new ()
				{
					Header = "仕入",
					Icon = "/Images/menu_icon/pay_icon.png",
					SubItems = new ()
					{
						new (){
							Header="生地付属仕入",
						},
						new (){
							Header="商品仕入入力",
							// ViewType = typeof(Views.ShohinShiireInputView),
							IsDialog = false,
						},
						new (){
							Header="仕入返品入力",
						},
						new (){
							Header="品番別仕入チェックリスト",
						},
						new (){
							Header="ブランド別仕入金額表",
						},
						new (){
							Header="消化仕入リスト",
						},
						new (){
							Header="仕入伝票印刷",
						},
						new (){
							Header="仕入先別仕入推移表",
						},
						new (){
							Header="支払入力",
						},
						new (){
							Header="支払消込",
						},
						new (){
							Header="仕入先元帳",
						},
						new (){
							Header="買掛金管理表",
						},
						new (){
							Header="支払一覧表",
						},
						new (){
							Header="月別支払予定表",
						},
						new (){
							Header="支払残高明細書",
						},
					}
				},
				new ()
				{
					Header = "売上",
					Icon = "/Images/menu_icon/sales_icon.png",
					SubItems = new ()
					{
						new (){
							Header="出荷・売上入力",
							// ViewType = typeof(Views.ShukkaUriageInputView),
							IsDialog = false,
						},
						new (){
							Header="店舗売上入力",
							// ViewType = typeof(Views.TenpoUriageInputView),
							IsDialog = false,
						},
						new (){
							Header="客数入力",
						},
						new (){
							Header="クレジット商品券データ入力",
						},
						new (){
							Header="売上金種Viewer",
						},
						new (){
							Header="品番別売上チェックリスト",
						},
						new (){
							Header="売上チェックリスト",
						},
						new (){
							Header="納品書印刷",
						},
						new (){
							Header="納品書印刷(専用伝票)",
						},
						new (){
							Header="納品書未発行チェックリスト",
						},
						new (){
							Header="入金入力",
							// ViewType = typeof(Views.NyukinInputView),
							IsDialog = false,
						},
						new (){
							Header="入金消込",
						},
						new (){
							Header="社員別購入履歴",
						},
						new (){
							Header="催事売上入力",
						},
						new (){
							Header="得意先元帳",
						},
						new (){
							Header="売掛金管理表",
						},
						new (){
							Header="月別入金予定表",
						},
						new (){
							Header="請求一覧表",
						},
						new (){
							Header="請求書印刷",
						},
						new (){
							Header="得意先別売上推移表",
						},
					}
				},
				new ()
				{
					Header = "配分",
					Icon = "/Images/menu_icon/distribute_icon.png",
					SubItems = new ()
					{
						new (){
							Header="店舗配分入力",
						},
						new (){
							Header="受注配分入力",
						},
						new (){
							Header="店舗出荷依頼",
						},
						new (){
							Header="在庫品配分",
						},
						new (){
							Header="得意先別配分入力",
						},
						new (){
							Header="出荷指示確定(商品)",
						},
						new (){
							Header="出荷指示確定(得意先)",
						},
						new (){
							Header="出荷処理入力",
						},
						new (){
							Header="配分データメンテ",
						},
						new (){
							Header="取置入力",
						},
						new (){
							Header="移動指示(SKU)",
						},
						new (){
							Header="移動指示(商品)",
						},
						new (){
							Header="出荷指示明細書印刷",
						},
						new (){
							Header="納入一覧表",
						},
						new (){
							Header="出荷指示一覧印刷",
						},
						new (){
							Header="配分関連メンテナンス",
						},
						new (){
							Header="自動発注・補充対象除外品設定",
						},
						new (){
							Header="在庫基準自動補充メンテナンス",
						},
					}
				},
				new ()
				{
					Header = "在庫管理",
					Icon = "/Images/menu_icon/stock_manage_icon.png",
					SubItems = new ()
					{
						new (){
							Header="棚卸入力",
						},
						new (){
							Header="移動入力(即時)",
						},
						new (){
							Header="移動入力(積送)",
						},
						new (){
							Header="移動受入力",
						},
						new (){
							Header="棚卸差異問合せ",
						},
						new (){
							Header="在庫問合せ",
						},
						new (){
							Header="商品履歴問合せ",
						},
						new (){
							Header="棚卸入力(一覧方式)",
						},
						new (){
							Header="在庫強制調整入力",
						},
						new (){
							Header="在庫移動入力",
						},
						new (){
							Header="倉庫分類別棚卸表",
						},
						new (){
							Header="倉庫別受払表",
						},
						new (){
							Header="商品別受払表",
						},
						new (){
							Header="倉庫別在庫集計表",
						},
						new (){
							Header="汎用在庫表",
						},
						new (){
							Header="棚卸明細表",
						},
						new (){
							Header="棚卸日一括メンテナンス",
						},
						new (){
							Header="棚卸チェックリスト",
						},
						new (){
							Header="品番別移動チェックリスト",
						},
						new (){
							Header="移動未受リスト",
						},
					}
				},
				new ()
				{
					Header = "売上分析",
					Icon = "/Images/menu_icon/sales_manage_icon.png",
					SubItems = new ()
					{
						new (){
							Header="販売動向表",
						},
						new (){
							Header="品番別販売動向表",
						},
						new (){
							Header="投入売上在庫表",
						},
						new (){
							Header="ベスト表",
						},
						new (){
							Header="商品消化率表",
						},
						new (){
							Header="セット売上分析表",
						},
						new (){
							Header="店別売上日報",
						},
						new (){
							Header="店舗別売上日計表",
						},
						new (){
							Header="売上速報",
						},
						new (){
							Header="売上週報･月報",
						},
						new (){
							Header="売上予算構成比",
						},
						new (){
							Header="分類別売上消化率表",
						},
						new (){
							Header="分類別店別売上報告",
						},
						new (){
							Header="店舗売上ランキング表",
						},
					}
				},
				new ()
				{
					Header = "卸・販売員・経営分析",
					Icon = "/Images/menu_icon/salesperson_manage_icon.png",
					SubItems = new ()
					{
						new (){
							Header="得意先別売上日報",
						},
						new (){
							Header="得意先別売上月報",
						},
						new (){
							Header="担当別売上実績半期報",
						},
						new (){
							Header="担当得意先別予算実績対比表",
						},
						new (){
							Header="個人売上ランキング表",
						},
						new (){
							Header="販売員別予算実績対比表",
						},
						new (){
							Header="半期報",
						},
						new (){
							Header="全社受払表",
						},
						new (){
							Header="卸・店舗売上実績表",
						},
					}
				},
				new ()
				{
					Header = "C.P.A",
					Icon = "/Images/menu_icon/cpa_analysia_icon.png",
					SubItems = new ()
					{
						new (){
							Header="★T.L - アナライザー★",
						},
						new (){
							Header="★C.G - アナライザー★",
						},
						new (){
							Header="ナンでも？CSV",
						},
						new (){
							Header="ABC分析(全社)",
						},
						new (){
							Header="ABC分析(店舗)",
						},
						new (){
							Header="在庫データ出力",
						},
						new (){
							Header="在庫受け払い照会",
						},
						new (){
							Header="販売動向ビュー",
						},
						new (){
							Header="店舗稼動ビュー",
						},
						new (){
							Header="売消台帳ビュー",
						},
						new (){
							Header="商品分析",
						},
						new (){
							Header="オンラインモニタ",
						},
						new (){
							Header="売上・在庫問合せ",
						},
						new (){
							Header="ベストレポート",
						},
					}
				},
				new ()
				{
					Header = "HHT / POS連携",
					Icon = "/Images/menu_icon/scanner_icon.png",
					SubItems = new ()
					{
						new (){
							Header="HHT用マスタデータ作成(VulcanCOM)",
						},
						new (){
							Header="HHT用マスタデータ作成(cvnetcom)",
						},
						new (){
							Header="HHT手動データ受信",
						},
						new (){
							Header="HHTエラーデータ修正入力",
						},
						new (){
							Header="HHTデータ更新",
						},
						new (){
							Header="HHT未更新データ印刷",
						},
						new (){
							Header="HHT未更新データ一括削除",
						},
						new (){
							Header="HHT用PATH設定",
						},
						new (){
							Header="出荷指示明細書印刷",
						},
						new (){
							Header="移動明細書印刷",
						},
						new (){
							Header="即時移動明細書",
						},
						new (){
							Header="HHT手動データ受信(ﾃﾞｰﾀ送信後)",
						},
						new (){
							Header="HHT手動データ受信(店舗固定)",
						},
						new (){
							Header="POSデータ取込",
						},
						new (){
							Header="HHT用マスタバーコード印刷",
						},
					}
				},
				new ()
				{
					Header = "月次 / 更新処理",
					Icon = "/Images/menu_icon/update_process_icon.png",
					SubItems = new ()
					{
						new (){
							Header="請求計算",
						},
						new (){
							Header="支払計算",
						},
						new (){
							Header="棚卸開始処理",
						},
						new (){
							Header="棚卸確定",
						},
						new (){
							Header="在庫・掛再更新",
						},
						new (){
							Header="在庫累計更新",
						},
						new (){
							Header="締日更新",
						},
						new (){
							Header="諸掛更新",
						},
						new (){
							Header="一時処理用(管理者用)",
						},
						new (){
							Header="残高登録処理",
						},
						new (){
							Header="データ整理更新",
						},
						new (){
							Header="消費税再計算",
						},
						new (){
							Header="最終仕入原価更新",
						},
						new (){
							Header="総平均原価更新",
						},
						new (){
							Header="消化仕入更新",
						},
						new (){
							Header="積送中クリア",
						},
						new (){
							Header="月間データ集計",
						},
						new (){
							Header="自動発注・補充の実行",
						},
					}
				},
				new ()
				{
					Header = "Loyal Customer",
					Icon = "/Images/menu_icon/loyal_customer_icon.png",
					SubItems = new ()
					{
						new (){
							Header="顧客マスタ",
							// ViewType = typeof(Views.MasterCustomerMenteView),
							IsDialog = false,
						},
						new (){
							Header="顧客ランク設定（管理者用)",
						},
						new (){
							Header="ポイントマスタ（ベース）（管理者用)",
						},
						new (){
							Header="ポイントマスタ（キャンペーン）",
						},
						new (){
							Header="ポイントマスタ（ボーナス）",
						},
						new (){
							Header="店舗別キャンペーン設定",
						},
						new (){
							Header="商品店舗別ポイント設定",
						},
						new (){
							Header="ポイント集計",
						},
						new (){
							Header="顧客カルテ",
						},
						new (){
							Header="RFMクロス分析表",
						},
						new (){
							Header="配信用簡易抽出",
						},
						new (){
							Header="配信ファイル変換",
						},
						new (){
							Header="顧客カルテ",
						},
						new (){
							Header="顧客ランク更新（管理者用）",
						},
						new (){
							Header="RFMクロス分析表",
						},
						new (){
							Header="配信用簡易抽出",
						},
						new (){
							Header="配信ファイル変換",
						},
						new (){
							Header="ポイント集計",
						},
					}
				},
				new ()
				{
					Header = "店舗",
					Icon = "/Images/menu_icon/store_icon.png",
					SubItems = new ()
					{
						new (){
							Header="店舗売上入力",
						},
						new (){
							Header="棚卸明細表(原価無)",
						},
						new (){
							Header="汎用在庫表(原価無)",
						},
						new (){
							Header="売上速報(原価無)",
						},
						new (){
							Header="売上週報･月報(原価無)",
						},
						new (){
							Header="分類別店別売上報告(原価無)",
						},
					}
				},
				new ()
				{
					Header = "物流",
					Icon = "/Images/menu_icon/warehouse_icon.png",
					SubItems = new ()
					{
						new (){
							Header="マスタデータ作成",
						},
						new (){
							Header="連携データ手動送信",
						},
						new (){
							Header="連携データ手動受信",
						},
						new (){
							Header="連携エラーデータ照会",
						},
					}
		}

	]);
	}
}
