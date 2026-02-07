using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Wpfclient.Views;
using System;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.Models;

public partial class MenuData : ObservableObject {
    [ObservableProperty]
    private string? header;

	[ObservableProperty]
	private bool isExpand;

	[ObservableProperty]
	private bool isSelected;

	[ObservableProperty]
	private ObservableCollection<MenuData>? subItems;

	[ObservableProperty]
	private string? addInfo;

	[ObservableProperty]
	private string? icon;

	[ObservableProperty]
	private bool isMainMenu;

	[ObservableProperty]
	private string? color;
    
    public Type? ViewType { get; set; }

    public bool IsDialog { get; set; } = true;

    public int InitParam { get; set; }

    public static ObservableCollection<MenuData> CreateDefault() => new([
        new MenuData {
            Header = "■ 開始処理 / 終了処理",
            Color = "MediumPurple",
            IsMainMenu = true,
            IsExpand = true,
            SubItems = new ObservableCollection<MenuData> {
                new MenuData {
                    Header = "開始処理：接続テスト",
                    AddInfo = "gRPC",
                    ViewType = typeof(Test20260203View),
                    IsDialog = false
                },
                new MenuData {
                    Header = "開始処理：ログイン",
                    AddInfo = "gRPC",
					ViewType = typeof(LoginView),
					IsDialog = true
				},
			new MenuData {
				Header = "システム設定",
				AddInfo = "構成",
				ViewType = typeof(SettingSystemView),
				IsDialog = true
			},
                new MenuData {
                    Header = "終了処理：データアップロード",
                    AddInfo = "準備中"
                }
            }
        },
		new MenuData {
			Header = "■ Master",
			Color = "CornflowerBlue",
			IsMainMenu = true,
			SubItems = new ObservableCollection<MenuData> {
				new MenuData {
					Header = "名称マスタメンテ",
					AddInfo = "Generic name setup",
					ViewType = typeof(MasterMeishoMenteView),
					IsDialog = false
				},
				new MenuData { Header = "得意先マスタメンテ", AddInfo = "準備中" },
				new MenuData { Header = "社員LOGINマスタ", AddInfo = "準備中" }
			}
		},
        new MenuData {
            Header = "■ 管理メニュー",
            Color = "SteelBlue",
            IsMainMenu = true,
            SubItems = new ObservableCollection<MenuData> {
                new MenuData { Header = "システム管理マスタ", AddInfo = "準備中" },
                new MenuData { Header = "処理履歴情報", AddInfo = "準備中" },
                new MenuData { Header = "LOGIN履歴情報", AddInfo = "準備中" }
            }
        },
        /* ================================ */
		new MenuData
				{
					Header = "開始処理/終了処理",
					Icon = "/Images/menu_icon/download_icon.png",
					IsExpand = true,
					IsMainMenu = true,
					IsDialog = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="開始処理：ログイン",
							ViewType = typeof(Views.LoginView),
							IsDialog = true,
						},
						new MenuData{
							Header="開始処理：マスタダウンロード(全件)",
							// ViewType = typeof(Views.MasterDownloadView),
							IsDialog = true,
						},
						new MenuData{
							Header="開始処理：マスタダウンロード(差分)",
							// ViewType = typeof(Views.MasterDownloadView),
							InitParam = 1,
							IsDialog = true,
						},
						new MenuData{
							Header="終了処理：データアップロード",
							IsDialog = true,
						},
					}
				},
				new MenuData
				{
					Header = "マスタ",
					Icon = "/Images/menu_icon/master_icon.png",
					Color = "Blue",
					IsExpand = true,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="名称マスタメンテ",
							ViewType = typeof(Views.MasterMeishoMenteView),
							IsDialog = false,
						},
						new MenuData{
							Header="得意先マスタメンテVer1",
							// ViewType = typeof(Views.MasterShopMente2View),
							IsDialog = false,
						},
						new MenuData{
							Header="得意先マスタメンテVer2",
							// ViewType = typeof(Views.MasterShopMente3View),
							IsDialog = false,
						},
						new MenuData{
							Header="得意先マスタメンテVer3",
							// ViewType = typeof(Views.MasterShopMente4View),
							IsDialog = false,
						},
						new MenuData{
							Header="仕入先マスタメンテ",
                            //ViewType = typeof(Views.MasterShiireMenteView),
                            // ViewType = typeof(Views.MasterShiireMente2View),
							IsDialog = false,
						},
							new MenuData{
							Header="商品マスタ(新規登録) / (照会・修正）",
							// ViewType = typeof(Views.MasterShohinMenteView),
						},
							new MenuData{
							Header="商品マスタ絵型更新",
						},
							new MenuData{
							Header="生地付属マスタ",
							// ViewType = typeof(Views.MasterShKijiMenteView),
							IsDialog = false,
						},
							new MenuData{
							Header="上代一括変更",
						},
							new MenuData{
							Header="上代一括変更取込",
						},
							new MenuData{
							Header="売価一覧印刷",
						},
							new MenuData{
							Header="原価変更登録",
						},
							new MenuData{
							Header="評価替",
						},
							new MenuData{
							Header="評価替（原価基準)",
						},
							new MenuData{
							Header="コンバートマスタ",
						},
							new MenuData{
							Header="社員マスタ",
							// ViewType = typeof(Views.MasterShainMenteView),
							IsDialog = false,
						},
							new MenuData{
							Header="社員証印刷(Felica用)",
						},
					}
				},
				new MenuData
				{
					Header = "マスタ補助",
					Icon = "/Images/menu_icon/master_support.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="名称マスタコピー作成",
						},
						new MenuData{
							Header="タックシール印刷",
						},
						new MenuData{
							Header="取込レイアウト作成",
						},
						new MenuData{
							Header="外部CSVデータ取込",
						},
						new MenuData{
							Header="下札発行用CSVデータ作成",
						},
						new MenuData{
							Header="JAN一括設定(49JAN含む)",
						},
						new MenuData{
							Header="マスタ復帰処理",
						},
						new MenuData{
							Header="商品色サイズメンテ",
						},
						new MenuData{
							Header="社員使用メニュー一覧",
						},
						new MenuData{
							Header="端末UUID設定(Felica用)",
						},
						new MenuData{
							Header="週マスタ",
						},
						new MenuData{
							Header="バーコードブック",
						},
						new MenuData{
							Header="各種マスタ印刷",
						},
						new MenuData{
							Header="各種伝票印刷",
						},
					}
				},
				new MenuData
				{
					Header = "管理メニュー",
					Icon = "/Images/menu_icon/manage_icon.png",
					Color = "Blue",
					IsExpand = true,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="社員LOGINマスタ(管理者用)",
							// ViewType = typeof(Views.SysLoginView),
							IsDialog = false,
						},
						new MenuData{
							Header="HHT用管理マスタ",
						},
						new MenuData{
							Header="システム管理マスタ(管理者用)",
							// ViewType = typeof(Views.MasterSysKanriMenteView),
							IsDialog = false,
						},
						new MenuData{
							Header="社員LOGINマスタ一覧",
						},
						new MenuData{
							Header="名称マスタ(管理者用)",
						},
						new MenuData{
							Header="処理履歴情報(管理者用)",
						},
						new MenuData{
							Header="自動実行履歴(管理者用)",
						},
						new MenuData{
							Header="LOGIN履歴情報(管理者用)",
						},
						new MenuData{
							Header="汎用ファイルメンテ(管理者用)",
						},
						new MenuData{
							Header="フラグメンテナンス（DTP専用)",
						},
						new MenuData{
							Header="システムメンテナンス処理",
						},
						new MenuData{
							Header="汎用SQL問い合わせ(管理者用)",
						},
						new MenuData{
							Header="DB定義書出力",
						},
						new MenuData{
							Header="帳票管理マスタ",
						},
						new MenuData{
							Header="ラベル名称マスタ",
						},
						new MenuData{
							Header="手動更新履歴(管理者用)",
						},
						new MenuData{
							Header="自動実行管理マスタ(管理者用)",
						},
						new MenuData{
							Header="自動実行スケジュール設定",
						},
					},
				},
				new MenuData
				{
					Header = "予算",
					Icon = "/Images/menu_icon/budget_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="店ブランド予算マスタ(月)",
						},
						new MenuData{
							Header="販売員別予算マスタ(月)",
						},
						new MenuData{
							Header="店別ブランド別予算マスタ",
						},
						new MenuData{
							Header="販売員別予算マスタ",
						},
						new MenuData{
							Header="営業担当別予算マスタ",
						},
						new MenuData{
							Header="店舗予算表",
						},
						new MenuData{
							Header="店舗別予算実績対比",
						},
						new MenuData{
							Header="店舗ブランド別予算実績対比",
						},
						new MenuData{
							Header="日別店別予算表",
						},
						new MenuData{
							Header="販売員予算表",
						},
					}
				},
				new MenuData
				{
					Header = "発注",
					Icon = "/Images/menu_icon/order_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="契約発注入力",
						},
						new MenuData{
							Header="発注配分入力",
						},
						new MenuData{
							Header="発注入力",
							// ViewType = typeof(Views.HatchuInputView),
							IsDialog = false,
						},
						new MenuData{
							Header="発注配分入力(簡易版)",
						},
						new MenuData{
							Header="納品予定表",
						},
						new MenuData{
							Header="仕入先別発注表",
						},
						new MenuData{
							Header="商品別発注表",
						},
						new MenuData{
							Header="商品別発注集計表",
						},
						new MenuData{
							Header="週間納品予定表",
						},
						new MenuData{
							Header="納品予定照会",
						},
						new MenuData{
							Header="仕入未受リスト",
						},
						new MenuData{
							Header="発注書",
						},
						new MenuData{
							Header="発注配分リスト",
						},
						new MenuData{
							Header="受注発注連携更新",
						},
						new MenuData{
							Header="契約残管理表",
						},
						new MenuData{
							Header="契約残完了設定",
						},
						new MenuData{
							Header="発注残管理表",
						},
						new MenuData{
							Header="発注残完了設定",
						},
					}
				},
				new MenuData
				{
					Header = "受注 / 展示会",
					Icon = "/Images/menu_icon/order_manage_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="展示会受注入力",
						},
						new MenuData{
							Header="納品予定表",
						},
						new MenuData{
							Header="得意先別受注表",
						},
						new MenuData{
							Header="商品別受注表",
						},
						new MenuData{
							Header="商品別受注集計表",
						},
						new MenuData{
							Header="受注残管理表",
						},
						new MenuData{
							Header="受注残完了設定",
						},
						new MenuData{
							Header="展示会スワッチ",
						},
						new MenuData{
							Header="スワッチデータ作成",
						},
						new MenuData{
							Header="スワッチデータ一括作成",
						},
						new MenuData{
							Header="絵型一覧表",
						},
						new MenuData{
							Header="得意先別売上予定表",
						},
						new MenuData{
							Header="担当別展示会受注合計表",
						},
						new MenuData{
							Header="受注ベスト表",
						},
						new MenuData{
							Header="配分出荷リスト",
						},
						new MenuData{
							Header="バーコードブック発行",
						},
					}
				},
				new MenuData
				{
					Header = "仕入",
					Icon = "/Images/menu_icon/pay_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="生地付属仕入",
						},
						new MenuData{
							Header="商品仕入入力",
							// ViewType = typeof(Views.ShohinShiireInputView),
							IsDialog = false,
						},
						new MenuData{
							Header="仕入返品入力",
						},
						new MenuData{
							Header="品番別仕入チェックリスト",
						},
						new MenuData{
							Header="ブランド別仕入金額表",
						},
						new MenuData{
							Header="消化仕入リスト",
						},
						new MenuData{
							Header="仕入伝票印刷",
						},
						new MenuData{
							Header="仕入先別仕入推移表",
						},
						new MenuData{
							Header="支払入力",
						},
						new MenuData{
							Header="支払消込",
						},
						new MenuData{
							Header="仕入先元帳",
						},
						new MenuData{
							Header="買掛金管理表",
						},
						new MenuData{
							Header="支払一覧表",
						},
						new MenuData{
							Header="月別支払予定表",
						},
						new MenuData{
							Header="支払残高明細書",
						},
					}
				},
				new MenuData
				{
					Header = "売上",
					Icon = "/Images/menu_icon/sales_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="出荷・売上入力",
							// ViewType = typeof(Views.ShukkaUriageInputView),
							IsDialog = false,
						},
						new MenuData{
							Header="店舗売上入力",
							// ViewType = typeof(Views.TenpoUriageInputView),
							IsDialog = false,
						},
						new MenuData{
							Header="客数入力",
						},
						new MenuData{
							Header="クレジット商品券データ入力",
						},
						new MenuData{
							Header="売上金種Viewer",
						},
						new MenuData{
							Header="品番別売上チェックリスト",
						},
						new MenuData{
							Header="売上チェックリスト",
						},
						new MenuData{
							Header="納品書印刷",
						},
						new MenuData{
							Header="納品書印刷(専用伝票)",
						},
						new MenuData{
							Header="納品書未発行チェックリスト",
						},
						new MenuData{
							Header="入金入力",
							// ViewType = typeof(Views.NyukinInputView),
							IsDialog = false,
						},
						new MenuData{
							Header="入金消込",
						},
						new MenuData{
							Header="社員別購入履歴",
						},
						new MenuData{
							Header="催事売上入力",
						},
						new MenuData{
							Header="得意先元帳",
						},
						new MenuData{
							Header="売掛金管理表",
						},
						new MenuData{
							Header="月別入金予定表",
						},
						new MenuData{
							Header="請求一覧表",
						},
						new MenuData{
							Header="請求書印刷",
						},
						new MenuData{
							Header="得意先別売上推移表",
						},
					}
				},
				new MenuData
				{
					Header = "配分",
					Icon = "/Images/menu_icon/distribute_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="店舗配分入力",
						},
						new MenuData{
							Header="受注配分入力",
						},
						new MenuData{
							Header="店舗出荷依頼",
						},
						new MenuData{
							Header="在庫品配分",
						},
						new MenuData{
							Header="得意先別配分入力",
						},
						new MenuData{
							Header="出荷指示確定(商品)",
						},
						new MenuData{
							Header="出荷指示確定(得意先)",
						},
						new MenuData{
							Header="出荷処理入力",
						},
						new MenuData{
							Header="配分データメンテ",
						},
						new MenuData{
							Header="取置入力",
						},
						new MenuData{
							Header="移動指示(SKU)",
						},
						new MenuData{
							Header="移動指示(商品)",
						},
						new MenuData{
							Header="出荷指示明細書印刷",
						},
						new MenuData{
							Header="納入一覧表",
						},
						new MenuData{
							Header="出荷指示一覧印刷",
						},
						new MenuData{
							Header="配分関連メンテナンス",
						},
						new MenuData{
							Header="自動発注・補充対象除外品設定",
						},
						new MenuData{
							Header="在庫基準自動補充メンテナンス",
						},
					}
				},
				new MenuData
				{
					Header = "在庫管理",
					Icon = "/Images/menu_icon/stock_manage_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="棚卸入力",
						},
						new MenuData{
							Header="移動入力(即時)",
						},
						new MenuData{
							Header="移動入力(積送)",
						},
						new MenuData{
							Header="移動受入力",
						},
						new MenuData{
							Header="棚卸差異問合せ",
						},
						new MenuData{
							Header="在庫問合せ",
						},
						new MenuData{
							Header="商品履歴問合せ",
						},
						new MenuData{
							Header="棚卸入力(一覧方式)",
						},
						new MenuData{
							Header="在庫強制調整入力",
						},
						new MenuData{
							Header="在庫移動入力",
						},
						new MenuData{
							Header="倉庫分類別棚卸表",
						},
						new MenuData{
							Header="倉庫別受払表",
						},
						new MenuData{
							Header="商品別受払表",
						},
						new MenuData{
							Header="倉庫別在庫集計表",
						},
						new MenuData{
							Header="汎用在庫表",
						},
						new MenuData{
							Header="棚卸明細表",
						},
						new MenuData{
							Header="棚卸日一括メンテナンス",
						},
						new MenuData{
							Header="棚卸チェックリスト",
						},
						new MenuData{
							Header="品番別移動チェックリスト",
						},
						new MenuData{
							Header="移動未受リスト",
						},
					}
				},
				new MenuData
				{
					Header = "売上分析",
					Icon = "/Images/menu_icon/sales_manage_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="販売動向表",
						},
						new MenuData{
							Header="品番別販売動向表",
						},
						new MenuData{
							Header="投入売上在庫表",
						},
						new MenuData{
							Header="ベスト表",
						},
						new MenuData{
							Header="商品消化率表",
						},
						new MenuData{
							Header="セット売上分析表",
						},
						new MenuData{
							Header="店別売上日報",
						},
						new MenuData{
							Header="店舗別売上日計表",
						},
						new MenuData{
							Header="売上速報",
						},
						new MenuData{
							Header="売上週報･月報",
						},
						new MenuData{
							Header="売上予算構成比",
						},
						new MenuData{
							Header="分類別売上消化率表",
						},
						new MenuData{
							Header="分類別店別売上報告",
						},
						new MenuData{
							Header="店舗売上ランキング表",
						},
					}
				},
				new MenuData
				{
					Header = "卸・販売員・経営分析",
					Icon = "/Images/menu_icon/salesperson_manage_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="得意先別売上日報",
						},
						new MenuData{
							Header="得意先別売上月報",
						},
						new MenuData{
							Header="担当別売上実績半期報",
						},
						new MenuData{
							Header="担当得意先別予算実績対比表",
						},
						new MenuData{
							Header="個人売上ランキング表",
						},
						new MenuData{
							Header="販売員別予算実績対比表",
						},
						new MenuData{
							Header="半期報",
						},
						new MenuData{
							Header="全社受払表",
						},
						new MenuData{
							Header="卸・店舗売上実績表",
						},
					}
				},
				new MenuData
				{
					Header = "C.P.A",
					Icon = "/Images/menu_icon/cpa_analysia_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="★T.L - アナライザー★",
						},
						new MenuData{
							Header="★C.G - アナライザー★",
						},
						new MenuData{
							Header="ナンでも？CSV",
						},
						new MenuData{
							Header="ABC分析(全社)",
						},
						new MenuData{
							Header="ABC分析(店舗)",
						},
						new MenuData{
							Header="在庫データ出力",
						},
						new MenuData{
							Header="在庫受け払い照会",
						},
						new MenuData{
							Header="販売動向ビュー",
						},
						new MenuData{
							Header="店舗稼動ビュー",
						},
						new MenuData{
							Header="売消台帳ビュー",
						},
						new MenuData{
							Header="商品分析",
						},
						new MenuData{
							Header="オンラインモニタ",
						},
						new MenuData{
							Header="売上・在庫問合せ",
						},
						new MenuData{
							Header="ベストレポート",
						},
					}
				},
				new MenuData
				{
					Header = "HHT / POS連携",
					Icon = "/Images/menu_icon/scanner_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="HHT用マスタデータ作成(VulcanCOM)",
						},
						new MenuData{
							Header="HHT用マスタデータ作成(cvnetcom)",
						},
						new MenuData{
							Header="HHT手動データ受信",
						},
						new MenuData{
							Header="HHTエラーデータ修正入力",
						},
						new MenuData{
							Header="HHTデータ更新",
						},
						new MenuData{
							Header="HHT未更新データ印刷",
						},
						new MenuData{
							Header="HHT未更新データ一括削除",
						},
						new MenuData{
							Header="HHT用PATH設定",
						},
						new MenuData{
							Header="出荷指示明細書印刷",
						},
						new MenuData{
							Header="移動明細書印刷",
						},
						new MenuData{
							Header="即時移動明細書",
						},
						new MenuData{
							Header="HHT手動データ受信(ﾃﾞｰﾀ送信後)",
						},
						new MenuData{
							Header="HHT手動データ受信(店舗固定)",
						},
						new MenuData{
							Header="POSデータ取込",
						},
						new MenuData{
							Header="HHT用マスタバーコード印刷",
						},
					}
				},
				new MenuData
				{
					Header = "月次 / 更新処理",
					Icon = "/Images/menu_icon/update_process_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="請求計算",
						},
						new MenuData{
							Header="支払計算",
						},
						new MenuData{
							Header="棚卸開始処理",
						},
						new MenuData{
							Header="棚卸確定",
						},
						new MenuData{
							Header="在庫・掛再更新",
						},
						new MenuData{
							Header="在庫累計更新",
						},
						new MenuData{
							Header="締日更新",
						},
						new MenuData{
							Header="諸掛更新",
						},
						new MenuData{
							Header="一時処理用(管理者用)",
						},
						new MenuData{
							Header="残高登録処理",
						},
						new MenuData{
							Header="データ整理更新",
						},
						new MenuData{
							Header="消費税再計算",
						},
						new MenuData{
							Header="最終仕入原価更新",
						},
						new MenuData{
							Header="総平均原価更新",
						},
						new MenuData{
							Header="消化仕入更新",
						},
						new MenuData{
							Header="積送中クリア",
						},
						new MenuData{
							Header="月間データ集計",
						},
						new MenuData{
							Header="自動発注・補充の実行",
						},
					}
				},
				new MenuData
				{
					Header = "Loyal Customer",
					Icon = "/Images/menu_icon/loyal_customer_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="顧客マスタ",
							// ViewType = typeof(Views.MasterCustomerMenteView),
							IsDialog = false,
						},
						new MenuData{
							Header="顧客ランク設定（管理者用)",
						},
						new MenuData{
							Header="ポイントマスタ（ベース）（管理者用)",
						},
						new MenuData{
							Header="ポイントマスタ（キャンペーン）",
						},
						new MenuData{
							Header="ポイントマスタ（ボーナス）",
						},
						new MenuData{
							Header="店舗別キャンペーン設定",
						},
						new MenuData{
							Header="商品店舗別ポイント設定",
						},
						new MenuData{
							Header="ポイント集計",
						},
						new MenuData{
							Header="顧客カルテ",
						},
						new MenuData{
							Header="RFMクロス分析表",
						},
						new MenuData{
							Header="配信用簡易抽出",
						},
						new MenuData{
							Header="配信ファイル変換",
						},
						new MenuData{
							Header="顧客カルテ",
						},
						new MenuData{
							Header="顧客ランク更新（管理者用）",
						},
						new MenuData{
							Header="RFMクロス分析表",
						},
						new MenuData{
							Header="配信用簡易抽出",
						},
						new MenuData{
							Header="配信ファイル変換",
						},
						new MenuData{
							Header="ポイント集計",
						},
					}
				},
				new MenuData
				{
					Header = "店舗",
					Icon = "/Images/menu_icon/store_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="店舗売上入力",
						},
						new MenuData{
							Header="棚卸明細表(原価無)",
						},
						new MenuData{
							Header="汎用在庫表(原価無)",
						},
						new MenuData{
							Header="売上速報(原価無)",
						},
						new MenuData{
							Header="売上週報･月報(原価無)",
						},
						new MenuData{
							Header="分類別店別売上報告(原価無)",
						},
					}
				},
				new MenuData
				{
					Header = "物流",
					Icon = "/Images/menu_icon/warehouse_icon.png",
					Color = "Blue",
					IsExpand = false,
					IsMainMenu = true,
					SubItems = new ObservableCollection<MenuData>
					{
						new MenuData{
							Header="マスタデータ作成",
						},
						new MenuData{
							Header="連携データ手動送信",
						},
						new MenuData{
							Header="連携データ手動受信",
						},
						new MenuData{
							Header="連携エラーデータ照会",
						},
					}
		}

    ]);
}
