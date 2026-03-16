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
		new("■ 管理メニュー / 接続テスト", new([
			new("ログイン", typeof(Views._00System.LoginView), true, 0, "gPRC"),
			new("リフレッシュ", typeof(Views._00System.LoginView), true, 1, "gRPC"),
			new("環境設定", typeof(Views._00System.SysSetConfigView), true),
			new("サンプル画面 ---", typeof(SampleView), false, addInfo:"gRPC"),
			new("システム管理マスタ", typeof(Views._01Master.MasterSysKanriMenteView), false, addInfo:"gRPC"),
			new("ログイン管理マスタ", typeof(Views._00System.SysLoginView), false, addInfo:"gRPC"),
			new("ログイン履歴情報", typeof(Views._00System.SysLoginHistoryView), false, addInfo:"gRPC"),
			new("自動実行履歴", typeof(object), false, addInfo:"準備中"),
			/*
		new("", new([
			new("", typeof(object), false, addInfo:"準備中"),
			new("", typeof(object), false, addInfo:"準備中"),
		])),
			*/
		])),
		new("■ マスター", new([
			new("名称マスタメンテ", typeof(Views._01Master.MasterMeishoMenteView), false, addInfo:"Generic name setup"),
			new("社員マスタ", typeof(Views._01Master.MasterShainMenteView), false),
			new("商品マスタ", typeof(Views._01Master.MasterShohinMenteView), false),
			new("仕入先マスタメンテ", typeof(Views._01Master.MasterShiireMenteView), false),
			new("得意先マスタメンテ", typeof(Views._01Master.MasterTokuiMenteView), false),
			new("顧客マスタメンテ", typeof(Views._01Master.MasterEndCustomerMenteView), false),
			new("上代一括変更", typeof(object), false, addInfo:"準備中"),
			new("取込レイアウト作成", typeof(object), false, addInfo:"準備中"),
			new("外部CSVマスタ取込", typeof(object), false, addInfo:"準備中"),
			new("原価変更登録", typeof(object), false, addInfo:"準備中"),
			new("評価替", typeof(object), false, addInfo:"準備中"),
			new("自動実行管理マスタ", typeof(object), false, addInfo:"準備中"),
			new("自動実行スケジュール設定", typeof(object), false, addInfo:"準備中"),
		])),
        /* ================================ */
		new("■ 予算", new([
			new("店ブランド予算マスタ", typeof(object), false, addInfo:"準備中"),
			new("販売員別予算マスタ", typeof(object), false, addInfo:"準備中"),
			new("営業担当別予算マスタ", typeof(object), false, addInfo:"準備中"),
			new("店舗予算表", typeof(object), false, addInfo:"準備中"),
			new("店舗ブランド別予算実績対比", typeof(object), false, addInfo:"準備中"),
			new("日別店別予算表", typeof(object), false, addInfo:"準備中"),
			new("販売員予算表", typeof(object), false, addInfo:"準備中"),
		])),
		new("■ 発注", new([
			new("発注入力", typeof(object), false, addInfo:"準備中"),
			new("発注配分入力", typeof(object), false, addInfo:"準備中"),
			new("納品予定表", typeof(object), false, addInfo:"準備中"),
			new("仕入先別発注表", typeof(object), false, addInfo:"準備中"),
			new("商品別発注表", typeof(object), false, addInfo:"準備中"),
			new("商品別発注集計表", typeof(object), false, addInfo:"準備中"),
			new("納品予定照会", typeof(object), false, addInfo:"準備中"),
			new("仕入未受リスト", typeof(object), false, addInfo:"準備中"),
			new("発注書", typeof(object), false, addInfo:"準備中"),
			new("発注配分リスト", typeof(object), false, addInfo:"準備中"),
			new("発注残管理表", typeof(object), false, addInfo:"準備中"),
			new("発注残完了設定", typeof(object), false, addInfo:"準備中"),
		])),
		new("■ 受注 / 展示会", new([
			new("展示会受注入力", typeof(object), false, addInfo:"準備中"),
			new("納品予定表", typeof(object), false, addInfo:"準備中"),
			new("得意先別受注表", typeof(object), false, addInfo:"準備中"),
			new("商品別受注表", typeof(object), false, addInfo:"準備中"),
			new("商品別受注集計表", typeof(object), false, addInfo:"準備中"),
			new("受注残管理表", typeof(object), false, addInfo:"準備中"),
			new("受注残完了設定", typeof(object), false, addInfo:"準備中"),
			new("展示会スワッチ", typeof(object), false, addInfo:"準備中"),
			new("スワッチデータ作成", typeof(object), false, addInfo:"準備中"),
			new("スワッチデータ一括作成", typeof(object), false, addInfo:"準備中"),
			new("絵型一覧表", typeof(object), false, addInfo:"準備中"),
			new("得意先別売上予定表", typeof(object), false, addInfo:"準備中"),
			new("担当別展示会受注合計表", typeof(object), false, addInfo:"準備中"),
			new("受注ベスト表", typeof(object), false, addInfo:"準備中"),
			new("配分出荷リスト", typeof(object), false, addInfo:"準備中"),
		])),
		new("■ 仕入", new([
			new("商品仕入入力", typeof(object), false, addInfo:"準備中"),
			new("仕入返品入力", typeof(object), false, addInfo:"準備中"),
			new("品番別仕入チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("ブランド別仕入金額表", typeof(object), false, addInfo:"準備中"),
			new("仕入伝票印刷", typeof(object), false, addInfo:"準備中"),
			new("仕入先別仕入推移表", typeof(object), false, addInfo:"準備中"),
			new("支払入力", typeof(object), false, addInfo:"準備中"),
			new("支払消込", typeof(object), false, addInfo:"準備中"),
			new("仕入先元帳", typeof(object), false, addInfo:"準備中"),
			new("買掛金管理表", typeof(object), false, addInfo:"準備中"),
			new("支払一覧表", typeof(object), false, addInfo:"準備中"),
			new("月別支払予定表", typeof(object), false, addInfo:"準備中"),
			new("支払残高明細書", typeof(object), false, addInfo:"準備中"),
		])),
		new("■ 売上", new([
			new("出荷・売上入力", typeof(object), false, addInfo:"準備中"),
			new("店舗売上入力", typeof(object), false, addInfo:"準備中"),
			new("POS日別精算入力", typeof(object), false, addInfo:"準備中"),
			new("売上金種Viewer", typeof(object), false, addInfo:"準備中"),
			new("品番別売上チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("売上チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("納品書印刷", typeof(object), false, addInfo:"準備中"),
			new("納品書印刷(専用伝票)", typeof(object), false, addInfo:"準備中"),
			new("納品書未発行チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("入金入力", typeof(object), false, addInfo:"準備中"),
			new("入金消込", typeof(object), false, addInfo:"準備中"),
			new("得意先元帳", typeof(object), false, addInfo:"準備中"),
			new("売掛金管理表", typeof(object), false, addInfo:"準備中"),
			new("月別入金予定表", typeof(object), false, addInfo:"準備中"),
			new("請求一覧表", typeof(object), false, addInfo:"準備中"),
			new("請求書印刷", typeof(object), false, addInfo:"準備中"),
			new("得意先別売上推移表", typeof(object), false, addInfo:"準備中"),
		])),
		new("■ 配分", new([
			new("店舗配分入力", typeof(object), false, addInfo:"準備中"),
			new("受注配分入力", typeof(object), false, addInfo:"準備中"),
			new("店舗出荷依頼", typeof(object), false, addInfo:"準備中"),
			new("在庫品配分", typeof(object), false, addInfo:"準備中"),
			new("得意先別配分入力", typeof(object), false, addInfo:"準備中"),
			new("出荷指示確定(商品)", typeof(object), false, addInfo:"準備中"),
			new("出荷指示確定(得意先)", typeof(object), false, addInfo:"準備中"),
			new("出荷処理入力", typeof(object), false, addInfo:"準備中"),
			new("配分データメンテ", typeof(object), false, addInfo:"準備中"),
			new("取置入力", typeof(object), false, addInfo:"準備中"),
			new("移動指示(SKU)", typeof(object), false, addInfo:"準備中"),
			new("移動指示(商品)", typeof(object), false, addInfo:"準備中"),
			new("出荷指示明細書印刷", typeof(object), false, addInfo:"準備中"),
			new("納入一覧表", typeof(object), false, addInfo:"準備中"),
			new("出荷指示一覧印刷", typeof(object), false, addInfo:"準備中"),
			new("配分関連メンテナンス", typeof(object), false, addInfo:"準備中"),
			new("自動発注・補充対象除外品設定", typeof(object), false, addInfo:"準備中"),
			new("在庫基準自動補充メンテナンス", typeof(object), false, addInfo:"準備中"),
		])),
		new("在庫管理", new([
			new("棚卸入力", typeof(object), false, addInfo:"準備中"),
			new("移動入力(即時)", typeof(object), false, addInfo:"準備中"),
			new("移動入力(積送)", typeof(object), false, addInfo:"準備中"),
			new("移動受入力", typeof(object), false, addInfo:"準備中"),
			new("棚卸差異問合せ", typeof(object), false, addInfo:"準備中"),
			new("在庫問合せ", typeof(object), false, addInfo:"準備中"),
			new("商品履歴問合せ", typeof(object), false, addInfo:"準備中"),
			new("棚卸入力(一覧方式)", typeof(object), false, addInfo:"準備中"),
			new("在庫強制調整入力", typeof(object), false, addInfo:"準備中"),
			new("在庫移動入力", typeof(object), false, addInfo:"準備中"),
			new("倉庫分類別棚卸表", typeof(object), false, addInfo:"準備中"),
			new("倉庫別受払表", typeof(object), false, addInfo:"準備中"),
			new("商品別受払表", typeof(object), false, addInfo:"準備中"),
			new("倉庫別在庫集計表", typeof(object), false, addInfo:"準備中"),
			new("汎用在庫表", typeof(object), false, addInfo:"準備中"),
			new("棚卸明細表", typeof(object), false, addInfo:"準備中"),
			new("棚卸日一括メンテナンス", typeof(object), false, addInfo:"準備中"),
			new("棚卸チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("品番別移動チェックリスト", typeof(object), false, addInfo:"準備中"),
			new("移動未受リスト", typeof(object), false, addInfo:"準備中"),
		])),
		new("売上分析", new([
			new("販売動向表", typeof(object), false, addInfo:"準備中"),
			new("品番別販売動向表", typeof(object), false, addInfo:"準備中"),
			new("投入売上在庫表", typeof(object), false, addInfo:"準備中"),
			new("ベスト表", typeof(object), false, addInfo:"準備中"),
			new("商品消化率表", typeof(object), false, addInfo:"準備中"),
			new("セット売上分析表", typeof(object), false, addInfo:"準備中"),
			new("店別売上日報", typeof(object), false, addInfo:"準備中"),
			new("店舗別売上日計表", typeof(object), false, addInfo:"準備中"),
			new("売上速報", typeof(object), false, addInfo:"準備中"),
			new("売上週報･月報", typeof(object), false, addInfo:"準備中"),
			new("売上予算構成比", typeof(object), false, addInfo:"準備中"),
			new("分類別売上消化率表", typeof(object), false, addInfo:"準備中"),
			new("分類別店別売上報告", typeof(object), false, addInfo:"準備中"),
			new("店舗売上ランキング表", typeof(object), false, addInfo:"準備中"),
		])),
		new("卸・販売員・経営分析", new([
			new("得意先別売上日報", typeof(object), false, addInfo:"準備中"),
			new("得意先別売上月報", typeof(object), false, addInfo:"準備中"),
			new("担当別売上実績半期報", typeof(object), false, addInfo:"準備中"),
			new("担当得意先別予算実績対比表", typeof(object), false, addInfo:"準備中"),
			new("個人売上ランキング表", typeof(object), false, addInfo:"準備中"),
			new("販売員別予算実績対比表", typeof(object), false, addInfo:"準備中"),
			new("半期報", typeof(object), false, addInfo:"準備中"),
			new("全社受払表", typeof(object), false, addInfo:"準備中"),
			new("卸・店舗売上実績表", typeof(object), false, addInfo:"準備中"),
		])),
		new("C.P.A", new([
			new("★T.L - アナライザー★", typeof(object), false, addInfo:"準備中"),
			new("★C.G - アナライザー★", typeof(object), false, addInfo:"準備中"),
			new("ナンでも？CSV", typeof(object), false, addInfo:"準備中"),
			new("ABC分析(全社)", typeof(object), false, addInfo:"準備中"),
			new("ABC分析(店舗)", typeof(object), false, addInfo:"準備中"),
			new("在庫データ出力", typeof(object), false, addInfo:"準備中"),
			new("在庫受け払い照会", typeof(object), false, addInfo:"準備中"),
			new("販売動向ビュー", typeof(object), false, addInfo:"準備中"),
			new("店舗稼動ビュー", typeof(object), false, addInfo:"準備中"),
			new("売消台帳ビュー", typeof(object), false, addInfo:"準備中"),
			new("商品分析", typeof(object), false, addInfo:"準備中"),
			new("オンラインモニタ", typeof(object), false, addInfo:"準備中"),
			new("売上・在庫問合せ", typeof(object), false, addInfo:"準備中"),
			new("ベストレポート", typeof(object), false, addInfo:"準備中"),
		])),
		new("HHT / POS連携", new([
			new("HHT用マスタデータ作成(cvnetcom)", typeof(object), false, addInfo:"準備中"),
			new("HHT手動データ受信", typeof(object), false, addInfo:"準備中"),
			new("HHTエラーデータ修正入力", typeof(object), false, addInfo:"準備中"),
			new("HHTデータ更新", typeof(object), false, addInfo:"準備中"),
			new("HHT未更新データ印刷", typeof(object), false, addInfo:"準備中"),
			new("HHT未更新データ一括削除", typeof(object), false, addInfo:"準備中"),
			new("出荷指示明細書印刷", typeof(object), false, addInfo:"準備中"),
			new("移動明細書印刷", typeof(object), false, addInfo:"準備中"),
			new("即時移動明細書", typeof(object), false, addInfo:"準備中"),
			new("HHT手動データ受信(ﾃﾞｰﾀ送信後)", typeof(object), false, addInfo:"準備中"),
			new("HHT用マスタバーコード印刷", typeof(object), false, addInfo:"準備中"),
		])),
		new("月次 / 更新処理", new([
			new("請求計算", typeof(object), false, addInfo:"準備中"),
			new("支払計算", typeof(object), false, addInfo:"準備中"),
			new("棚卸開始処理", typeof(object), false, addInfo:"準備中"),
			new("棚卸確定", typeof(object), false, addInfo:"準備中"),
			new("在庫・掛再更新", typeof(object), false, addInfo:"準備中"),
			new("在庫累計更新", typeof(object), false, addInfo:"準備中"),
			new("締日更新", typeof(object), false, addInfo:"準備中"),
			new("諸掛更新", typeof(object), false, addInfo:"準備中"),
			new("一時処理用(管理者用)", typeof(object), false, addInfo:"準備中"),
			new("残高登録処理", typeof(object), false, addInfo:"準備中"),
			new("データ整理更新", typeof(object), false, addInfo:"準備中"),
			new("消費税再計算", typeof(object), false, addInfo:"準備中"),
			new("最終仕入原価更新", typeof(object), false, addInfo:"準備中"),
			new("総平均原価更新", typeof(object), false, addInfo:"準備中"),
			new("消化仕入更新", typeof(object), false, addInfo:"準備中"),
			new("積送中クリア", typeof(object), false, addInfo:"準備中"),
			new("月間データ集計", typeof(object), false, addInfo:"準備中"),
			new("自動発注・補充の実行", typeof(object), false, addInfo:"準備中"),
		])),
		new("Loyal Customer", new([
			new("顧客マスタ", typeof(object), false, addInfo:"準備中"),
			new("ポイントマスタ（ベース）（管理者用)", typeof(object), false, addInfo:"準備中"),
			new("ポイントマスタ（キャンペーン）", typeof(object), false, addInfo:"準備中"),
			new("ポイントマスタ（ボーナス）", typeof(object), false, addInfo:"準備中"),
			new("店舗別キャンペーン設定", typeof(object), false, addInfo:"準備中"),
			new("商品店舗別ポイント設定", typeof(object), false, addInfo:"準備中"),
			new("ポイント集計", typeof(object), false, addInfo:"準備中"),
			new("顧客カルテ", typeof(object), false, addInfo:"準備中"),
			new("RFMクロス分析表", typeof(object), false, addInfo:"準備中"),
		])),
		new("店舗", new([
			new("店舗売上入力", typeof(object), false, addInfo:"準備中"),
			new("棚卸明細表(原価無)", typeof(object), false, addInfo:"準備中"),
			new("汎用在庫表(原価無)", typeof(object), false, addInfo:"準備中"),
			new("売上速報(原価無)", typeof(object), false, addInfo:"準備中"),
			new("売上週報･月報(原価無)", typeof(object), false, addInfo:"準備中"),
			new("分類別店別売上報告(原価無)", typeof(object), false, addInfo:"準備中"),
		])),
		new("物流", new([
			new("マスタデータ作成", typeof(object), false, addInfo:"準備中"),
			new("連携データ手動送信", typeof(object), false, addInfo:"準備中"),
			new("連携データ手動受信", typeof(object), false, addInfo:"準備中"),
			new("連携エラーデータ照会", typeof(object), false, addInfo:"準備中"),
		])),
	]);
	}
}
