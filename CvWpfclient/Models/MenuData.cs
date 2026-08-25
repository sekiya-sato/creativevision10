using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;
using CvBase.Share;
using System.Collections.ObjectModel;

namespace CvWpfclient.Models;

public partial class MenuData : ObservableObject {
	[ObservableProperty]
	public partial string Header { get; set; } = string.Empty;
	/* Obsolete
	private bool isExpand;
	private string? icon;
	 */
	[ObservableProperty]
	public partial ObservableCollection<MenuData>? SubItems { get; set; }

	[ObservableProperty]
	public partial string? AddInfo { get; set; }

	/* --- after this line, only use for ViewModel --- */
	public Type ViewType { get; set; } = typeof(object);

	public bool IsExecutable => ViewType.IsSubclassOf(typeof(System.Windows.Window));
	public bool IsDialog { get; set; } = true;
	public int InitParam { get; set; }

	/// <summary>
	/// 表示を許可するロール。null または空は全ロール共通として扱う。
	/// SysLogin.Id_Role に対応する。
	/// </summary>
	public IReadOnlyList<EnumLoginRole>? AllowedRoles { get; init; }

	public MenuData() {
	}
	public MenuData(string header, ObservableCollection<MenuData> subItems) {
		Header = header;
		SubItems = subItems;
	}
	public MenuData(string header, Type viewType, bool isDialog = false, int initParam = 0, string? addInfo = null) {
		Header = header;
		ViewType = viewType;
		IsDialog = isDialog;
		InitParam = initParam;
		AddInfo = addInfo;
	}

	/// <summary>
	/// 指定ロールで表示してよいメニューかを返す
	/// </summary>
	public bool IsVisibleFor(EnumLoginRole role) =>
		AllowedRoles == null || AllowedRoles.Count == 0 || AllowedRoles.Contains(role);

	/// <summary>
	/// 標準ロール(ロール未設定)のメニューを作成する
	/// </summary>
	public static ObservableCollection<MenuData> CreateDefault() => CreateDefault(EnumLoginRole.Standard);

	/// <summary>
	/// ログインロールに応じたメニューを作成する。
	/// ロール別メニューは標準業務メニューへのショートカットであり、標準業務メニュー側の機能は削っていない。
	/// </summary>
	public static ObservableCollection<MenuData> CreateDefault(EnumLoginRole role) => FilterByRole(CreateAll(), role);

	/// <summary>
	/// AllowedRoles に合致しないノードを再帰的に取り除く
	/// </summary>
	private static ObservableCollection<MenuData> FilterByRole(ObservableCollection<MenuData> items, EnumLoginRole role) {
		var result = new ObservableCollection<MenuData>();
		foreach (var item in items) {
			if (!item.IsVisibleFor(role)) {
				continue;
			}
			if (item.SubItems != null) {
				item.SubItems = FilterByRole(item.SubItems, role);
			}
			result.Add(item);
		}
		return result;
	}

	/// <summary>
	/// 全ロール分のメニュー定義。構成は .omo/2026-08-新メニュー案.md に準拠する。
	/// `addInfo:"準備中"` のものは、基本的に空のViewおよびViewModel
	/// </summary>
	private static ObservableCollection<MenuData> CreateAll() {
		return new([
		/* ================================================================
		 * ロール別メニュー: 標準業務メニューへのショートカット。
		 * 同一のViewを標準業務メニュー側にも残しているため、ここは表示経路の追加のみ。
		 * ================================================================ */
		new("■ 店舗業務", new([
			new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), addInfo:"店舗売上入力"),
			new("在庫問合せ", typeof(Views._08Zaiko.ZaikoQueryView), addInfo:"商品・色・倉庫条件から現在庫を照会"),
			new("棚卸入力", typeof(Views._08Zaiko.StockInputView), addInfo:"倉庫の棚卸データをTran60Tanaへ登録"),
			new("棚卸明細表(原価無)", typeof(Views._40Shop.StockTakeDetailReportCostlessView), addInfo:"棚卸明細表の店舗配布版。原価単価･差異金額を出さない"),
			new("汎用在庫表(原価無)", typeof(Views._40Shop.GeneralInventoryTableCostlessView), addInfo:"汎用在庫表の店舗配布版。原価単価･原価金額を出さない"),
			new("売上速報(原価無)", typeof(Views._40Shop.SalesQuickReportCostlessView), addInfo:"売上速報の店舗配布版。粗利･粗利率を出さない"),
			new("売上週報･月報(原価無)", typeof(Views._40Shop.SalesWeeklyMonthlyReportCostlessView), addInfo:"売上週報･月報の店舗配布版。粗利･粗利率を出さない"),
			new("分類別店別売上報告(原価無)", typeof(Views._40Shop.CategoryShopSalesReportCostlessView), addInfo:"分類別店別売上報告の店舗配布版。値入率を出さない"),
		])) { AllowedRoles = [EnumLoginRole.Shop] },
		// 倉庫担当向けの一覧は新メニュー案に記載が無いため、在庫・移動・出荷の既存機能から暫定構成している。
		new("■ 倉庫業務", new([
			new("在庫問合せ", typeof(Views._08Zaiko.ZaikoQueryView), addInfo:"商品・色・倉庫条件から現在庫を照会"),
			new("移動入力(即時)", typeof(Views._08Zaiko.IdoInputSokuView), addInfo:"倉庫間即時移動の入力・一覧・明細印刷"),
			new("移動入力(積送)", typeof(Views._08Zaiko.IdoInputOutView), addInfo:"倉庫間積送移動の入力・一覧・明細印刷"),
			new("移動受入力", typeof(Views._08Zaiko.IdoInputUkeView), addInfo:"積送中在庫を移動先へ実入庫。未受の出庫伝票から取込可"),
			new("移動未受リスト", typeof(Views._08Zaiko.IdoUnreceivedListView), addInfo:"出庫済みで入庫未済の移動をSKU別に列挙"),
			new("棚卸入力", typeof(Views._08Zaiko.StockInputView), addInfo:"倉庫の棚卸データをTran60Tanaへ登録"),
			new("店舗出荷依頼", typeof(Views._07Haibun.ShopShippingRequestView), addInfo:"準備中 倉庫の有効在庫を見て店舗から出荷希望数を入力"),
			new("出荷処理入力", typeof(Views._07Haibun.ShippingInputView), addInfo:"準備中 確定済み配分から出荷売上/移動伝票を作成しEndFlagを立てる(引当解除)"),
			new("出荷指示明細書印刷", typeof(Views._07Haibun.ShippingConfirmDetailPrintView), addInfo:"準備中 確定した配分をピッキングリストとして印刷"),
			new("有効在庫問合わせ", typeof(Views._07Haibun.YukoZaikoQueryView), addInfo:"準備中 商品別に有効在庫(実在庫-引当数)･引当･在庫を照会"),
		])) { AllowedRoles = [EnumLoginRole.Warehouse] },
		/* ================================================================
		 * 01 マスター
		 * ================================================================ */
		new("■ マスター", new([
			new("▲ 基本設定", new([
				new("システム管理マスタ", typeof(Views._01Master.MasterSysKanriMenteView), addInfo:"会社情報、締日、税率などを設定"),
				new("設定フラグマスタメンテ", typeof(Views._01Master.MasterConfigMenteView), addInfo:"設定フラグテーブルを編集"),
				new("名称マスタメンテ", typeof(Views._01Master.MasterMeishoMenteView), addInfo:"区分別の名称マスタメンテ画面"),
			])),
			new("▲ 社員・取引先", new([
				new("社員マスタ", typeof(Views._01Master.MasterShainMenteView), addInfo:"社員マスタメンテ画面"),
				new("社員証カード印刷", typeof(Views._01Master.PrintMasterShainCardView), addInfo:"社員証カード型印刷"),
				new("得意先マスタメンテ", typeof(Views._01Master.MasterTokuiMenteView), addInfo:"得意先マスタメンテ画面"),
				new("仕入先マスタメンテ", typeof(Views._01Master.MasterShiireMenteView), addInfo:"仕入先マスタメンテ画面"),
			])),
			new("▲ 商品", new([
				new("商品マスタ", typeof(Views._01Master.MasterShohinMenteView), addInfo:"商品マスタメンテ画面"),
				new("商品バーコードブック", typeof(Views._01Master.MasterPrintBarcodeView), addInfo:"商品バーコードブック印刷"),
				new("上代一括変更", typeof(Views._01Master.MasterJouDaiBulkChangeView), addInfo:"店舗･期間つきの販売価格をTranJodaiで登録し確定でDerivedJodaiへ展開"),
			])),
			new("▲ 顧客・イベント", new([
				new("顧客マスタメンテ", typeof(Views._01Master.MasterEndCustomerMenteView), addInfo:"顧客マスタメンテ画面"),
				new("得意先イベントメンテ", typeof(Views._01Master.TranTokuiPromotionMenteView), addInfo:"得意先、日付別のイベント名と重要度を登録"),
				new("店舗イベントメンテ", typeof(Views._01Master.TranShopPromotionMenteView), addInfo:"店舗、日付別のイベント名と重要度を登録"),
			])),
			new("▲ データ取込", new([
				new("取込レイアウト作成", typeof(Views._01Master.ImportTemplateCreateView), addInfo:"テーブル列定義からUTF-8 CSV取込レイアウトを作成"),
				new("外部CSVマスタ取込", typeof(Views._01Master.ExternalCsvImportView), addInfo:"取込レイアウトCSVを検証してInsertBulkParamで登録"),
			])),
		])),
		/* ================================================================
		 * 02 予算
		 * ================================================================ */
		new("■ 予算", new([
			new("▲ 店舗予算", new([
				new("店ブランド予算マスタ(月)", typeof(Views._02Yosan.ShopBrandBudgetMasterView), addInfo:"店ブランド別の月毎の日予算を作成"),
				new("店ブランド予算マスタメンテ", typeof(Views._02Yosan.MasterYosanBrandMenteView), addInfo:"MasterYosanBrand の日別予算レコードを直接編集"),
				new("店舗予算表", typeof(Views._02Yosan.ShopBudgetReportView)),
				new("日別店別予算表", typeof(Views._02Yosan.DailyShopBudgetReportView), addInfo:"日付→店舗順に予算･売上･差異･累計を印刷"),
				new("店舗ブランド別予算実績対比", typeof(Views._02Yosan.ShopBrandBudgetVsActualView), addInfo:"店舗×ブランドの売上･粗利を月単位で予算実績対比"),
			])),
			new("▲ 販売員予算", new([
				new("販売員別予算マスタ(月)", typeof(Views._02Yosan.SalesStaffBudgetMasterView), addInfo:"販売員別の月毎の日予算を作成"),
				new("販売員予算マスタメンテ", typeof(Views._02Yosan.MasterYosanHanbaiMenteView), addInfo:"MasterYosanHanbai の日別予算レコードを直接編集"),
				new("販売員予算表", typeof(Views._02Yosan.SalesStaffBudgetReportView), addInfo:"販売員別･日別に予算･売上･差異･累計を印刷"),
			])),
		])),
		/* ================================================================
		 * 03 発注
		 * ================================================================ */
		new("■ 発注", new([
			new("▲ 発注入力", new([
				new("発注入力", typeof(Views._03Hatchu.HachuInputView), addInfo:"仕入先に対する発注入力"),
				new("発注配分入力", typeof(Views._03Hatchu.HachuHaibunInputView), addInfo:"発注(入荷予定)を入庫先へ色サイズ別に振り分けて配分データを作成"),
			])),
			new("▲ 発注残・納品予定", new([
				new("納品予定照会", typeof(Views._03Hatchu.DeliveryScheduleInquiryView), addInfo:"発注ヘッダの納品予定日で入荷予定・納期遅れを照会"),
				new("納品予定表", typeof(Views._03Hatchu.DeliveryScheduleTableView), addInfo:"発注を納品予定日順に印刷(仕入先別・入荷数/残数・納期遅れ日数)。納期遅れは納品日とEndFlagで判定"),
				new("仕入未受リスト", typeof(Views._03Hatchu.PendingShiireListView), addInfo:"発注済みで入荷未済の分をSKU別に列挙"),
				new("発注残管理表", typeof(Views._03Hatchu.HachuZanKanriTableView), addInfo:"発注伝票単位に残数･残金額･経過日数･滞留区分を印刷"),
				new("発注残完了設定", typeof(Views._03Hatchu.HachuZanCompletionSettingView), addInfo:"残がある発注を伝票単位で完了にする(解除も可)。完納すると自動完了するのでこれは例外処理"),
			])),
			new("▲ 発注帳票", new([
				new("発注書", typeof(Views._03Hatchu.HachuFormView), addInfo:"仕入先へ渡す発注書を単票印刷"),
				new("仕入先別発注表", typeof(Views._03Hatchu.SupplierHachuTableView), addInfo:"仕入先別に件数･数量･金額･上代･原価率を集計"),
				new("商品別発注表", typeof(Views._03Hatchu.ShohinHachuTableView), addInfo:"発注明細を品番別に集計して数量･金額･上代を印刷"),
				new("商品別発注集計表", typeof(Views._03Hatchu.ShohinHachuSummaryTableView), addInfo:"発注をブランド/アイテム別に集計し分類内構成比を印刷"),
			])),
		])),
		/* ================================================================
		 * 04 受注・展示会
		 * ================================================================ */
		new("■ 受注・展示会", new([
			new("▲ 受注", new([
				new("展示会受注入力", typeof(Views._04Juchu.JuchuInputView), addInfo:"得意先対象の受注入力・一覧・明細印刷"),
				new("納品予定照会(受注)", typeof(Views._04Juchu.NouhinYoteiTableView), addInfo:"受注ヘッダの納品予定日で得意先別の出荷予定・納期遅れを照会"),
				new("得意先別受注表", typeof(Views._04Juchu.TokuiSakiJuchuTableView), addInfo:"得意先別に件数･数量･金額･上代･掛率を集計"),
				new("商品別受注表", typeof(Views._04Juchu.ShouhinJuchuTableView), addInfo:"受注明細を品番別に集計して数量･金額･上代を印刷"),
				new("商品別受注集計表", typeof(Views._04Juchu.ShouhinJuchuSummaryTableView), addInfo:"受注をブランド/アイテム別に集計し分類内構成比を印刷"),
				new("受注残管理表", typeof(Views._04Juchu.JuchuZanKanriTableView), addInfo:"受注残(受注-紐付く出荷)を伝票単位に印刷。完了済みは対象外"),
				new("受注残完了設定", typeof(Views._04Juchu.JuchuZanCompletionSettingView), addInfo:"残がある受注を伝票単位で完了にする(解除も可)。全量出荷すると自動完了するのでこれは例外処理"),
			])),
			new("▲ 受注分析・帳票", new([
				new("得意先別売上予定表", typeof(Views._04Juchu.TokuiSakiUriageYoteiTableView), addInfo:"得意先別に受注残を売上予定額として集計"),
				new("担当別展示会受注合計表", typeof(Views._04Juchu.TantoTenjiJuchuGoukeiTableView), addInfo:"担当社員×展示会で受注を集計し構成比を印刷"),
				new("受注ベスト表", typeof(Views._04Juchu.JuchuBestTableView), addInfo:"受注を品番別に順位付けし構成比･累計構成比を印刷"),
			])),
		])),
		/* ================================================================
		 * 05 仕入 : 支払・買掛関連は「掛管理（請求・支払）」へ移動済み
		 * ================================================================ */
		new("■ 仕入", new([
			new("商品仕入入力", typeof(Views._05Shiire.ShiireInputView), addInfo:"仕入先に対する仕入入力"),
			new("仕入返品入力", typeof(Views._05Shiire.HenpinInputView), addInfo:"仕入先への返品･値引を Tran03Shiire の減算区分で入力"),
			new("▲ 仕入照会・帳票", new([
				new("品番別仕入チェックリスト", typeof(Views._05Shiire.HinbanShiireCheckListView), addInfo:"仕入明細を品番別に集計して数量･金額･上代･平均単価を印刷"),
				new("ブランド別仕入金額表", typeof(Views._05Shiire.BrandShiireKingakuTableView), addInfo:"ﾌﾞﾗﾝﾄﾞ別･年月別の仕入金額･上代･原価率･構成比を印刷"),
				new("仕入伝票印刷", typeof(Views._05Shiire.ShiireSlipPrintView), addInfo:"仕入日・仕入先・倉庫・伝票番号などを指定して仕入伝票をPDF印刷"),
				new("仕入先別仕入推移表", typeof(Views._05Shiire.ShiireTrendReportView), addInfo:"仕入先×年月の数量･金額･累計･前年同月比を印刷"),
			])),
		])),
		/* ================================================================
		 * 06 配分・出荷 : 旧「配分」。出荷指示・出荷処理まで含むため名称を拡張
		 * ================================================================ */
		new("■ 配分・出荷", new([
			new("▲ 配分", new([
				new("店舗配分入力", typeof(Views._07Haibun.ShopHaibunInputView), addInfo:"入荷予定･現在庫をSKU×店舗へ振り分けてTranHaibunを作成"),
				new("受注配分入力", typeof(Views._07Haibun.JuchuHaibunInputView), addInfo:"受注伝票を選び受注残をSKU別に配分。有効在庫は参照表示"),
				new("在庫品配分", typeof(Views._07Haibun.ZaikoHinHaibunView), addInfo:"準備中 倉庫の有効在庫を一括で店舗へ配分する"),
				new("得意先別配分入力", typeof(Views._07Haibun.TokuiHaibunInputView), addInfo:"準備中 得意先を軸に商品を振り分ける"),
				new("配分データメンテ", typeof(Views._07Haibun.HaibunDataMenteView), addInfo:"準備中 管理者用。確定日･欠品数･完了FLGを直接修正する"),
				new("配分関連メンテナンス", typeof(Views._07Haibun.HaibunMenteView), addInfo:"1.1以降 自動補充の対象店舗･優先順位を設定する"),
			])),
			new("▲ 出荷", new([
				new("店舗出荷依頼", typeof(Views._07Haibun.ShopShippingRequestView), addInfo:"準備中 倉庫の有効在庫を見て店舗から出荷希望数を入力"),
				new("出荷指示確定(商品)", typeof(Views._07Haibun.ShippingConfirmShohinView), addInfo:"準備中 商品基準で配分を確定しKakuteiDayを立てる。有効在庫割れはエラー"),
				new("出荷指示確定(得意先)", typeof(Views._07Haibun.ShippingConfirmTokuiView), addInfo:"準備中 得意先基準で配分を確定しKakuteiDayを立てる。有効在庫割れはエラー"),
				new("出荷処理入力", typeof(Views._07Haibun.ShippingInputView), addInfo:"準備中 確定済み配分から出荷売上/移動伝票を作成しEndFlagを立てる(引当解除)"),
				new("出荷指示明細書印刷", typeof(Views._07Haibun.ShippingConfirmDetailPrintView), addInfo:"準備中 確定した配分をピッキングリストとして印刷"),
				new("滞留・欠品例外(出荷指示一覧)", typeof(Views._07Haibun.ShippingConfirmListView), addInfo:"確定済みかつ未出荷の滞留を検出し確定取消/強制完了。欠品実績も照会"),
				new("納入一覧表", typeof(Views._07Haibun.ShippingListReportView), addInfo:"準備中 商品×出荷先で種まき用の数量表を印刷"),
			])),
			new("▲ 配分照会", new([
				new("配分問合わせ", typeof(Views._07Haibun.HaibunQueryView), addInfo:"準備中 出庫側から商品別の配分数を倉庫×色サイズで展開"),
				new("引当問合わせ", typeof(Views._07Haibun.HikiateQueryView), addInfo:"準備中 入庫側から商品別の引当数を倉庫×色サイズで展開"),
				new("有効在庫問合わせ", typeof(Views._07Haibun.YukoZaikoQueryView), addInfo:"準備中 商品別に有効在庫(実在庫-引当数)･引当･在庫を照会"),
			])),
			new("▲ 補充・移動指示", new([
				new("取置入力", typeof(Views._07Haibun.ReservationInputView), addInfo:"準備中 得意先･顧客向けに在庫を確保する(引当対象)"),
				new("移動指示(SKU)", typeof(Views._07Haibun.IdoInstructionSkuView), addInfo:"準備中"),
				new("移動指示(商品)", typeof(Views._07Haibun.IdoInstructionShohinView), addInfo:"準備中"),
				new("自動発注・補充対象除外品設定", typeof(Views._07Haibun.AutoHachuHojunExcludeSettingView), addInfo:"1.1以降 自動補充はRelease後対応"),
				new("在庫基準自動補充メンテナンス", typeof(Views._07Haibun.ZaikoAutoHojunMenteView), addInfo:"1.1以降 自動補充はRelease後対応"),
			])),
		])),
		/* ================================================================
		 * 07 売上 : 入金・売掛・請求関連は「掛管理（請求・支払）」へ移動済み
		 * ================================================================ */
		new("■ 売上", new([
			new("▲ 売上入力", new([
				new("出荷・売上入力", typeof(Views._06Uriage.ShukkaUriageInputView), addInfo:"出荷売上入力"),
				new("店舗売上入力", typeof(Views._06Uriage.ShopUriageInputView), addInfo:"店舗売上入力"),
				new("POS日別精算入力", typeof(Views._06Uriage.PosDailySeisanInputView), addInfo:"未実装 日別精算を保存するテーブルが無く仕様確定待ち"),
			])),
			new("▲ 売上確認", new([
				new("売上金種Viewer", typeof(Views._06Uriage.UriageCashTypeReportView), addInfo:"POS決済内訳を金種別に集計し売上金額との差額を確認"),
				new("品番別売上チェックリスト", typeof(Views._06Uriage.HinbanUriageCheckListView), addInfo:"卸･店舗売上明細を品番別に集計して数量･金額･上代･平均単価を印刷"),
				new("売上チェックリスト", typeof(Views._06Uriage.UriageCheckListView), addInfo:"売上伝票の明細を1行ずつ印刷して入力内容を突合"),
			])),
			new("▲ 納品書", new([
				new("納品書印刷", typeof(Views._06Uriage.NouhinBookPrintView), addInfo:"卸売上伝票から納品書を単票印刷し発行済みフラグを管理"),
				new("納品書印刷(専用伝票)", typeof(Views._06Uriage.NouhinBookPrintCustomView), addInfo:"プレプリント伝票へ位置合わせして納品書を印刷"),
				new("納品書未発行チェックリスト", typeof(Views._06Uriage.NouhinBookPendingCheckListView), addInfo:"納品書が未発行の売上伝票を一覧印刷して発行漏れを検出"),
			])),
		])),
		/* ================================================================
		 * 08 在庫管理
		 * ================================================================ */
		new("■ 在庫管理", new([
			new("▲ 在庫照会", new([
				new("在庫問合せ", typeof(Views._08Zaiko.ZaikoQueryView), addInfo:"商品・色・倉庫条件から現在庫を照会"),
				new("商品履歴問合せ", typeof(Views._08Zaiko.ShohinHistoryQueryView), addInfo:"1商品の在庫を動かした伝票を時系列に表示"),
			])),
			new("▲ 在庫移動", new([
				new("移動入力(即時)", typeof(Views._08Zaiko.IdoInputSokuView), addInfo:"倉庫間即時移動の入力・一覧・明細印刷"),
				new("移動入力(積送)", typeof(Views._08Zaiko.IdoInputOutView), addInfo:"倉庫間積送移動の入力・一覧・明細印刷"),
				new("移動受入力", typeof(Views._08Zaiko.IdoInputUkeView), addInfo:"積送中在庫を移動先へ実入庫。未受の出庫伝票から取込可"),
				new("在庫移動入力", typeof(Views._08Zaiko.StockIdoInputView), addInfo:"出庫元の在庫一覧から移動数を入力し即時移動伝票を作成"),
				new("移動未受リスト", typeof(Views._08Zaiko.IdoUnreceivedListView), addInfo:"出庫済みで入庫未済の移動をSKU別に列挙"),
				new("品番別移動チェックリスト", typeof(Views._08Zaiko.HinbanIdoCheckListView), addInfo:"移動明細を品番別に集計し出庫数･入庫数･差異を印刷"),
				new("在庫強制調整入力", typeof(Views._08Zaiko.StockForceInputView), addInfo:"調整専用伝票Tran61Choseiで在庫を強制的に増減(登録で即時反映)"),
				new("在庫強制調整実績照会", typeof(Views._08Zaiko.StockForceHistoryView), addInfo:"強制調整伝票を照会し取消(削除で在庫を調整前へ戻す)"),
				new("在庫強制調整実績表", typeof(Views._08Zaiko.StockForceReportView), addInfo:"強制調整伝票を倉庫･調整日範囲で伝票単位に一覧印刷(調整理由付き･棚卸確定は除く)"),
			])),
			new("▲ 棚卸", new([
				new("棚卸入力", typeof(Views._08Zaiko.StockInputView), addInfo:"倉庫の棚卸データをTran60Tanaへ登録"),
				new("棚卸入力(一覧方式)", typeof(Views._08Zaiko.StockInputListView), addInfo:"倉庫のSKUを一覧表示し実棚数をまとめて入力"),
				new("棚卸差異問合せ", typeof(Views._08Zaiko.StockDifferenceQueryView), addInfo:"棚卸数と理論在庫の差異を画面で確認"),
				new("棚卸明細表", typeof(Views._08Zaiko.StockMeisaiTableView), addInfo:"棚卸数と理論在庫を突合し差異数･差異金額を印刷"),
				new("棚卸チェックリスト", typeof(Views._08Zaiko.StockCheckListView), addInfo:"棚卸入力伝票の明細を棚番順に印刷して入力内容を突合"),
				new("棚卸日一括メンテナンス", typeof(Views._08Zaiko.StockDateBulkMenteView), addInfo:"店舗別の棚卸日と自動補充曜日を一覧で設定・更新"),
			])),
			new("▲ 在庫帳票", new([
				new("倉庫分類別棚卸表", typeof(Views._08Zaiko.SokoCategoryStockListView), addInfo:"倉庫×分類別にSKUを列挙した実棚記入用リスト"),
				new("倉庫別受払表", typeof(Views._08Zaiko.SokoInOutReportView), addInfo:"倉庫別に前月残･入出庫･調整･当月残･棚卸差異を年月順に印刷"),
				new("商品別受払表", typeof(Views._08Zaiko.ShohinInOutReportView), addInfo:"商品(SKU)別に前月残･入出庫･調整･当月残を年月順に印刷"),
				new("倉庫別在庫集計表", typeof(Views._08Zaiko.SokoSummaryReportView), addInfo:"倉庫×分類別の在庫数･原価金額･上代金額･構成比を印刷"),
				new("汎用在庫表", typeof(Views._08Zaiko.GeneralStockTableView), addInfo:"現在庫をSKU別/商品別/倉庫別に集計して金額付きで印刷"),
			])),
		])),
		/* ================================================================
		 * 09 掛管理（請求・支払）: 「仕入」「売上」「月次」から掛関連を集約した新設メニュー
		 * ================================================================ */
		new("■ 掛管理（請求・支払）", new([
			new("▲ 売掛・請求", new([
				new("請求計算", typeof(Views._31Monthly.BillingCalculationView), addInfo:"締日･請求月･得意先範囲を指定してSummaryUriSeiを作成する"),
				new("請求一覧表", typeof(Views._06Uriage.SeikyuListReportView), addInfo:"請求日単位に得意先別の請求額･残高を一覧印刷"),
				new("請求台帳（発行控え）", typeof(Views._06Uriage.SeikyuLedgerReportView), addInfo:"請求計算が保存した請求書番号･再発行世代･入金予定日を含む確定結果を一覧印刷(数値突合･発行控え用)"),
				new("請求書印刷", typeof(Views._06Uriage.SeikyuBalanceDetailView), addInfo:"得意先別に請求ヘッダ＋対象期間の売上･入金明細を単票印刷"),
				new("入金入力", typeof(Views._06Uriage.NyukinInputView), addInfo:"得意先からの入金を金種別明細で入力(売掛の減算)"),
				new("入金消込", typeof(Views._06Uriage.NyukinMatchingView), addInfo:"請求先単位に卸売上を一覧し伝票単位で消込(EndFlag)。入金は区分別集計で金額を突合"),
				new("得意先元帳", typeof(Views._06Uriage.TokuiLedgerView), addInfo:"得意先別に繰越残高･売上･入金･差引残高を日付順に印刷"),
				new("売掛金管理表", typeof(Views._06Uriage.UrikakeBalanceReportView), addInfo:"得意先別に前月残･当月売上･当月入金･当月残を印刷(締め処理の集計結果)"),
				new("月別入金予定表", typeof(Views._06Uriage.MonthlyNyukinYoteiTableView), addInfo:"得意先の回収条件から入金予定日別の予定額を印刷"),
			])),
			new("▲ 買掛・支払", new([
				new("支払計算", typeof(Views._31Monthly.PaymentCalculationView), addInfo:"締日･支払月･仕入先範囲を指定してSummaryKaiShiを作成する"),
				new("支払入力", typeof(Views._05Shiire.ShiharaiInputView)),
				new("支払消込", typeof(Views._05Shiire.ShiharaiMatchingView), addInfo:"支払先単位に仕入を一覧し伝票単位で消込(EndFlag)。支払は区分別集計で金額を突合"),
				new("仕入先元帳", typeof(Views._05Shiire.ShiireLedgerView), addInfo:"仕入先別に繰越残高･仕入･支払･差引残高を日付順に印刷"),
				new("買掛金管理表", typeof(Views._05Shiire.KaikakeBalanceReportView), addInfo:"仕入先別に前月残･当月仕入･当月支払･当月残を印刷(締め処理の集計結果)"),
				new("支払一覧表", typeof(Views._05Shiire.ShiharaiListReportView), addInfo:"支払日単位に仕入先別の支払額･残高を一覧印刷"),
				new("支払台帳（発行控え）", typeof(Views._05Shiire.ShiharaiLedgerReportView), addInfo:"支払計算が保存した支払予定日を含む確定結果を一覧印刷(数値突合･発行控え用)"),
				new("月別支払予定表", typeof(Views._05Shiire.MonthlyShiharaiYoteiTableView), addInfo:"仕入先の支払条件から支払予定日別の予定額を印刷"),
				new("支払残高明細書", typeof(Views._05Shiire.ShiharaiBalanceDetailView), addInfo:"仕入先別に支払ヘッダ＋対象期間の仕入･支払明細を単票印刷"),
			])),
		])),
		/* ================================================================
		 * 20 分析 : 旧「売上分析」「卸・販売員・経営分析」「C.P.A」を集約
		 * ================================================================ */
		new("■ 分析", new([
			new("▲ 売上分析", new([
				new("販売動向表", typeof(Views._20UriageAnalysis.SalesTrendReportView), addInfo:"店舗×日/週/月で売上･数量･客単価･累計を印刷"),
				new("品番別販売動向表", typeof(Views._20UriageAnalysis.HinbanSalesTrendReportView), addInfo:"品番×日/週/月で数量･金額･累計を印刷"),
				new("投入売上在庫表", typeof(Views._20UriageAnalysis.InputSalesStockReportView), addInfo:"品番別に投入(仕入)･売上･在庫･消化率を並べて印刷"),
				new("ベスト表", typeof(Views._20UriageAnalysis.BestSalesReportView), addInfo:"売上を品番別に順位付けし構成比･累計構成比を印刷"),
				new("商品消化率表", typeof(Views._20UriageAnalysis.ShohinTurnoverRateReportView), addInfo:"商品別に消化率と値入率を印刷。分母は売上+在庫/投入を選択"),
				new("セット売上分析表", typeof(Views._20UriageAnalysis.SetSalesAnalysisReportView), addInfo:"未実装 セット定義テーブルが無く分析の切り口も未確定"),
				new("店別売上日報", typeof(Views._20UriageAnalysis.ShopSalesDailyView), addInfo:"店舗×日で伝票数･数量･金額･消費税･値引･客単価を印刷"),
				new("店舗別売上日計表", typeof(Views._20UriageAnalysis.ShopSalesDailySummaryView), addInfo:"日計を売上･返品･値引へ分解して純売上と累計を印刷"),
				new("売上速報", typeof(Views._20UriageAnalysis.SalesQuickReportView), addInfo:"指定日の全店売上を当日･累計･予算比･前年比で1枚に印刷"),
				new("売上週報･月報", typeof(Views._20UriageAnalysis.UriageShuhouGeppouView), addInfo:"店舗×週/月で売上･予算比･前年同期比･累計を印刷"),
				new("売上予算構成比", typeof(Views._20UriageAnalysis.SalesBudgetRatioReportView), addInfo:"店舗×ブランドで予算構成比と実績構成比を対比"),
				new("分類別売上消化率表", typeof(Views._20UriageAnalysis.CategorySalesConsumptionRateView), addInfo:"ブランド/アイテム/シーズン別の消化率･値入率･構成比"),
				new("分類別店別売上報告", typeof(Views._20UriageAnalysis.CategoryShopSalesReportView), addInfo:"店舗×分類の売上と店舗内構成比･値入率を印刷"),
				new("店舗売上ランキング表", typeof(Views._20UriageAnalysis.ShopSalesRankingReportView), addInfo:"店舗を売上順に順位付けし客単価･予算比･構成比を印刷"),
			])),
			// 店舗配布版(原価無)の帳票。ロール別「店舗業務」からも同じViewを参照する
			new("▲ 店舗配布版(原価無)", new([
				new("棚卸明細表(原価無)", typeof(Views._40Shop.StockTakeDetailReportCostlessView), addInfo:"棚卸明細表の店舗配布版。原価単価･差異金額を出さない"),
				new("汎用在庫表(原価無)", typeof(Views._40Shop.GeneralInventoryTableCostlessView), addInfo:"汎用在庫表の店舗配布版。原価単価･原価金額を出さない"),
				new("売上速報(原価無)", typeof(Views._40Shop.SalesQuickReportCostlessView), addInfo:"売上速報の店舗配布版。粗利･粗利率を出さない"),
				new("売上週報･月報(原価無)", typeof(Views._40Shop.SalesWeeklyMonthlyReportCostlessView), addInfo:"売上週報･月報の店舗配布版。粗利･粗利率を出さない"),
				new("分類別店別売上報告(原価無)", typeof(Views._40Shop.CategoryShopSalesReportCostlessView), addInfo:"分類別店別売上報告の店舗配布版。値入率を出さない"),
			])),
			new("▲ 卸・販売員・経営分析", new([
				new("得意先別売上日報", typeof(Views._21OroshiAnalysis.TokuiSalesDailyReportView), addInfo:"卸売上を得意先×日で集計し値入率･累計を印刷"),
				new("得意先別売上月報", typeof(Views._21OroshiAnalysis.TokuiSalesMonthlyReportView), addInfo:"卸売上を得意先×年月で集計し前年同月比･累計を印刷"),
				new("得意先別売上推移表", typeof(Views._06Uriage.TokuiTrendReportView), addInfo:"得意先×年月の数量･金額･累計･前年同月比を印刷"),
				new("担当別売上実績半期報", typeof(Views._21OroshiAnalysis.TantoSalesHalfYearReportView), addInfo:"営業担当別に半期6ヶ月の月別実績･前年比･累計を印刷"),
				new("担当得意先別予算実績対比表", typeof(Views._21OroshiAnalysis.TantoTokuiBudgetActualReportView), addInfo:"担当×得意先の実績と担当単位の予算･達成率を対比"),
				new("個人売上ランキング表", typeof(Views._21OroshiAnalysis.PersonalSalesRankingReportView), addInfo:"社員別売上を順位付け。営業担当(卸)/販売員(店舗)を選択"),
				new("販売員別予算実績対比表", typeof(Views._21OroshiAnalysis.SalesStaffBudgetVsActualReportView), addInfo:"販売員予算と店舗売上実績を年月別に差異･達成率で対比"),
				new("半期報", typeof(Views._21OroshiAnalysis.HalfYearReportView), addInfo:"全社の半期6ヶ月を卸･店舗･合計･前年比･累計で印刷"),
				new("全社受払表", typeof(Views._21OroshiAnalysis.CorporateInOutReportView), addInfo:"全倉庫合計の受払を年月順に印刷(在庫累計更新の結果)"),
				new("卸・店舗売上実績表", typeof(Views._21OroshiAnalysis.OroshiShopSalesActualReportView), addInfo:"卸と店舗の売上･構成比･合計･前年比を年月別に印刷"),
			])),
		])),
		/* ================================================================
		 * 30 月次・更新処理 : 請求計算/支払計算は「掛管理（請求・支払）」へ移動済み
		 * ================================================================ */
		new("■ 月次・更新処理", new([
			new("▲ 締め・集計", new([
				new("締日更新", typeof(Views._31Monthly.ShimebiUpdateView), addInfo:"1.1以降 1.0では伝票の遡及制御を有効日数のワーニングで行う"),
				/* 月間データ集計 Views._31Monthly.MonthlyDataSummaryView 夜間の自動実行処理で対応するため不要 旧システムでは月の分析データを集計するために使用 */
				/* 在庫累計更新 Views._31Monthly.StockRuikeiUpdateView cv10ではSummaryRealStock SummaryStock で足りているため不要 旧システムでは増えた過去在庫データを集計し縮小するために使用 */
			])),
			new("▲ 棚卸更新", new([
				new("棚卸開始処理", typeof(Views._31Monthly.StockTakeInitiationView), addInfo:"棚卸年月末時点の帳簿在庫を保存し棚卸中に動かないようにする。差異調査後は再実行する"),
				new("棚卸確定処理", typeof(Views._31Monthly.StockTakeFinalizationView), addInfo:"実棚数と帳簿在庫の差を在庫調整伝票(Tran61Chosei)にして在庫へ反映する。再確定可"),
			])),
			new("▲ 再更新", new([
				new("在庫・掛再更新", typeof(Views._31Monthly.StockKakeUpdateView), addInfo:"在庫･売掛･買掛を取引明細から再集計する"),
				new("消費税再計算", typeof(Views._31Monthly.TaxRecalculationView), addInfo:"準備中"),
			])),
			new("▲ 原価・評価", new([
				new("AfterToDo: 原価変更登録", typeof(Views._01Master.GenkaChangeEntryView), addInfo:"準備中 他のがだいたい終わってから実装する"),
				new("諸掛更新", typeof(Views._31Monthly.SundryChargesUpdateView), addInfo:"準備中"),
				new("最終仕入原価更新", typeof(Views._31Monthly.LastPurchaseCostRefreshView), addInfo:"準備中"),
				new("総平均原価更新", typeof(Views._31Monthly.TotalAverageCostUpdateView), addInfo:"準備中"),
				new("消化仕入更新", typeof(Views._31Monthly.ConsumptionPurchaseUpdateView), addInfo:"準備中"),
				new("AfterToDo: 評価替", typeof(Views._01Master.ProductRatingChangeView), addInfo:"準備中 他のがだいたい終わってから実装する"),
			])),
			new("▲ その他更新", new([
				new("積送中クリア", typeof(Views._31Monthly.InTransitClearView), addInfo:"準備中"),
				new("自動発注・補充の実行", typeof(Views._31Monthly.AutoOrderReplenishExecuteView), addInfo:"準備中"),
				new("残高登録処理", typeof(Views._31Monthly.BalanceRegistrationView), addInfo:"期首の売掛/請求/買掛/支払残をテンプレートCSVで投入。期首前の年月で登録し再計算から凍結される"),
				/* データ整理更新 Views._31Monthly.DataCleanupUpdateView 整理対象のデータが未確定 旧システムでは増えた完了済データを削除し縮小するために使用 */
				new("一時処理用(管理者用)", typeof(Views._31Monthly.TemporaryProcessingView), addInfo:"準備中 データ整理更新などが必要であればここに入れる"),
			])),
		])),
		/* ================================================================
		 * 31 外部連携 : 旧「HHT / POS連携」「物流」を統合
		 * 2026-08-17 の決定 I6「ハンディは無し」は、配分→出荷のフローでハンディ読取に依存しない
		 * （伝票作成は「出荷処理入力」に一本化する）という意味であり、HHT連携機能そのものは残す。
		 * POS は専用画面が未作成のため小分類を作っていない(POS日別精算入力/売上金種Viewerは「売上」配下)
		 * ================================================================ */
		new("■ 外部連携", new([
			new("▲ HHT", new([
				new("HHT用マスタデータ作成", typeof(Views._30HHT.HhtMasterDataCreateView), addInfo:"CSV または固定長で HHT マスタを出力"),
				new("HHT手動データ受信", typeof(Views._30HHT.HhtManualDataReceiveView), addInfo:"受信フォルダ内の HHT データを手動取込"),
				new("HHTエラーデータ修正入力", typeof(Views._30HHT.HhtErrorDataInputView), addInfo:"変換エラーのHHTデータを確認・修正"),
				new("HHTデータ更新", typeof(Views._30HHT.HhtDataUpdateView), addInfo:"受信済みHHTデータを伝票へ展開"),
				new("HHT未更新データ印刷", typeof(Views._30HHT.HhtUnupdatedDataPrintView), addInfo:"準備中"),
				new("HHT未更新データ一括削除", typeof(Views._30HHT.HhtUnupdatedDataDeleteView), addInfo:"準備中"),
				new("出荷指示明細書印刷", typeof(Views._30HHT.ShippingConfirmDetailPrintView), addInfo:"準備中"),
				new("移動明細書印刷", typeof(Views._30HHT.IdoDetailBookPrintView), addInfo:"準備中"),
				new("即時移動明細書", typeof(Views._30HHT.IdoSokuDetailBookPrintView), addInfo:"準備中"),
			])),
			new("▲ 物流連携", new([
				new("マスタデータ作成", typeof(Views._41Logistics.LogisticsMasterDataCreateView), addInfo:"準備中"),
				new("連携データ手動送信", typeof(Views._41Logistics.IntegrationDataManualTransmitView), addInfo:"準備中 配分の指示数を倉庫へ送信する(TranHaibun.SendFlg)"),
				new("連携データ手動受信", typeof(Views._41Logistics.IntegrationDataManualReceiveView), addInfo:"準備中 倉庫から確定数･欠品数を受信する(JitsuSu/ShortSu)"),
				new("連携エラーデータ照会", typeof(Views._41Logistics.IntegrationErrorDataQueryView), addInfo:"準備中"),
			])),
		])),
		/* ================================================================
		 * 32 顧客管理
		 * ================================================================ */
		new("■ 顧客管理", new([
			new("▲ 顧客", new([
				new("顧客マスタ", typeof(Views._32LoyalCustomer.CustomerMasterView), addInfo:"準備中"),
				new("顧客カルテ", typeof(Views._32LoyalCustomer.EndCustomerProfileView), addInfo:"準備中"),
			])),
			new("▲ ポイント", new([
				new("ポイントマスタ（ベース）（管理者用)", typeof(Views._32LoyalCustomer.PointMasterBaseAdminView), addInfo:"準備中"),
				new("ポイントマスタ（キャンペーン）", typeof(Views._32LoyalCustomer.PointMasterCampaignView), addInfo:"準備中"),
				new("ポイントマスタ（ボーナス）", typeof(Views._32LoyalCustomer.PointMasterBonusView), addInfo:"準備中"),
				new("店舗別キャンペーン設定", typeof(Views._32LoyalCustomer.ShopCampaignSettingView), addInfo:"準備中"),
				new("商品店舗別ポイント設定", typeof(Views._32LoyalCustomer.ShohinShopPointSettingView), addInfo:"準備中"),
				new("ポイント集計", typeof(Views._32LoyalCustomer.PointSummaryView), addInfo:"準備中"),
			])),
			new("顧客分析", new([
				new("RFMクロス分析表", typeof(Views._32LoyalCustomer.RfmCrossAnalysisTableView), addInfo:"準備中"),
			])),
		])),
		/* ================================================================
		 * 90 システム管理 : 旧「管理メニュー / テスト画面」
		 * 「保守ツール」は Support / Developer 権限向け。権限分離は Permission 実装後に対応する
		 * ================================================================ */
		new("■ システム管理", new([
			new("▲ ログイン管理", new([
				new("ログイン管理マスタ", typeof(Views._00System.SysLoginView), addInfo:"ログインIDの管理とユーザ割当、有効期限の設定"),
				new("ログイン履歴情報", typeof(Views._00System.SysLoginHistoryView), addInfo:"ログイン履歴の確認"),
			])),
			new("▲ 自動実行", new([
				new("自動実行管理マスタ", typeof(Views._00System.SysSchedulerJobMenteView), addInfo:"自動実行ジョブの一覧・変更"),
				new("自動実行履歴", typeof(Views._00System.SysAutoExecHistoryView), addInfo:"自動実行ジョブの履歴"),
			])),
			new("▲ 保守ツール", new([
				new("汎用マスタメンテ", typeof(Views._00System.SysGeneralMenteView), addInfo:"MasterMeisho を汎用編集UIで表示・更新"),
				new("DB定義書出力", typeof(Views._00System.SysTableSpecView), addInfo:"選択テーブルのDB定義書を印刷"),
				new("旧DBからの変換処理", typeof(Views._00System.ConvertDbView), addInfo:"旧OracleDBからのデータ変換 サーバ側にOracle接続定義が必要"),
				new("旧DBからの選択変換処理", typeof(Views._00System.ConvertSelectedView), addInfo:"旧OracleDBからの選択変換処理 サーバ側にOracle接続定義が必要"),
				new("管理者用システム処理", typeof(Views._00System.SysExecMiscView), addInfo:"管理者用の各種システム処理"),
			])),
		])),
	]);
	}
}
/// <summary>
/// サブメニューの種類
/// </summary>
public enum SubMenuType {
	[Comment("システム管理")]
	_00System =0,
	[Comment("マスター")]
	_01Master = 1,
	[Comment("予算")]
	_02Yosan=2,
	[Comment("発注")]
	_03Hatchu=3, // 主Table : Tran13Hachu
	[Comment("受注")]
	_04Juchu = 4, // 主Table : Tran12Jyuchu
	[Comment("仕入")]
	_05Shiire =5, // 主Table : Tran03Shiire
	[Comment("売上")]
	_06Uriage=6, // 主Table : Tran00Uriage, Tran01Tenuri,Tran02PosSeisan
	[Comment("配分・出荷")]
	_07Haibun=7, // 主Table : TranHaibun
	[Comment("在庫")]
	_08Zaiko=8,// 主Table : Tran60Tana,Tran61Chosei
	[Comment("売上分析")]
	_20UriageAnalysis =20,
	[Comment("卸・販売員・経営分析")]
	_21OroshiAnalysis=21,
	[Comment("HHT")]
	_30HHT=30, // 主Table : TranVulcanHht
	[Comment("月次処理")]
	_31Monthly=31,
	[Comment("顧客管理")]
	_32LoyalCustomer=32, // 主Table : MasterEndCustomer, MasterPointRank, SummaryPoint
	[Comment("店舗専用")]
	_40Shop = 40,
	[Comment("物流連携")]
	_41Logistics=41,
}
