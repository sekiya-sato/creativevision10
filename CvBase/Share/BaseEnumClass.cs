namespace CvBase.Share;

/// <summary>
/// 性別 [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
[Comment("性別")]
public enum EnumGender : int {
	[Comment("不明")]
	Unknown = 0,
	[Comment("女性")]
	Woman = 1,
	[Comment("男性")]
	Man = 2
}

/// <summary>
/// する,しない [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
[Comment("汎用Yes／No")]
public enum EnumYesNo : int {
	[Comment("しない")]
	No = 0,
	[Comment("する")]
	Yes = 1
}
/// <summary>
/// 掛計上する、しない
/// </summary>
[Comment("掛計上")]
public enum EnumIsPay : int {
	[Comment("掛計上しない")]
	No = 0,
	[Comment("掛計上する")]
	Yes = 1
}
/// <summary>
/// 顧客LCV区分
/// </summary>
[Comment("顧客区分")]
public enum EnumCustomerLcvKubun : int {
	[Comment("0 未使用")]
	UnUse = 0,
	[Comment("1 スマホ")]
	UseMobile = 1,
	[Comment("4 スマホログインのみ")]
	UseMobileLoginOnly = 4,
	[Comment("9 会員情報未登録")]
	UseMobileNoDetail = 9
}
/// <summary>
/// 締日
/// </summary>
[Comment("締日")]
public enum EnumShime : int {
	[Comment("未使用")]
	Day00 = 0,
	Day01 = 1,
	Day02 = 2,
	Day03 = 3,
	Day04 = 4,
	Day05 = 5,
	Day06 = 6,
	Day07 = 7,
	Day08 = 8,
	Day09 = 9,
	Day10 = 10,
	Day11 = 11,
	Day12 = 12,
	Day13 = 13,
	Day14 = 14,
	Day15 = 15,
	Day16 = 16,
	Day17 = 17,
	Day18 = 18,
	Day19 = 19,
	Day20 = 20,
	Day21 = 21,
	Day22 = 22,
	Day23 = 23,
	Day24 = 24,
	Day25 = 25,
	Day26 = 26,
	Day27 = 27,
	Day28 = 28,
	[Comment("末")]
	DayLast = 99
}

/// <summary>
/// 得意先種別
/// </summary>
[Comment("得意先種別")]
public enum EnumTokui : int {
	/// <summary>
	/// 倉庫
	/// </summary>
	[Comment("倉庫")]
	_0_Soko = 0,
	/// <summary>
	/// 卸先
	/// </summary>
	[Comment("卸先")]
	_1_Oroshi = 1,
	/// <summary>
	/// 売仕店
	/// </summary>
	[Comment("売仕店")]
	_3_UriShi = 3,
	/// <summary>
	/// 直営店
	/// </summary>
	[Comment("直営店")]
	_6_Tenpo = 6,
}

/// <summary>
/// 外税内税区分
/// </summary>
[Comment("外税内税区分 外税／内税")]
public enum EnumTaxPriceType : int {
	/// <summary>
	/// 外税
	/// </summary>
	[Comment("外税")]
	Exclusive = 0,
	/// <summary>
	/// 内税
	/// </summary>
	[Comment("内税")]
	Inclusive = 1
}
/// <summary>
/// 端数処理
/// </summary>
[Comment("端数処理")]
public enum EnumRounding : int {
	/// <summary>
	/// 四捨五入
	/// </summary>
	[Comment("四捨五入")]
	Round = 0,
	/// <summary>
	/// 切上
	/// </summary>
	[Comment("切上")]
	Ceiling = 1,
	/// <summary>
	/// 切捨
	/// </summary>
	[Comment("切捨")]
	Floor = 2
}
/// <summary>
/// 税計算単位
/// </summary>
[Comment("税計算単位 請求／伝票")]
public enum EnumTaxCalcUnit : int {
	/// <summary>
	/// 締め請求期間単位
	/// </summary>
	[Comment("締め請求期間単位")]
	Billing = 0,
	/// <summary>
	/// 取引・伝票単位
	/// </summary>
	[Comment("取引・伝票単位")]
	Slip = 1
}
/// <summary>
/// 伝票印字タイプ
/// </summary>
[Comment("伝票印字タイプ")]
public enum  EnumSlipFormType {
	[Comment("印字しない")]
	_0_None = 0,
	[Comment("自社伝票")]
	_1_Standard = 1,
	[Comment("百貨店伝票")]
	_2_DepartmentStore = 2,
	[Comment("チェーンストア統一伝票1型")]
	_3_ChainStoreStandardType1 = 3,
	[Comment("チェーンストア統一伝票")]
	_4_ChainStoreStandard = 4,
	[Comment("百貨店伝票（丸井用）")]
	_5_DepartmentStoreMarui = 5,
	[Comment("チェーンストア統一伝票（ターンアラウンド用2型）")]
	_6_ChainStoreTurnaroundType2 = 6,
	[Comment("チェーンストア統一伝票（ターンアラウンド用1型）")]
	_7_ChainStoreTurnaroundType1 = 7,
	[Comment("百貨店伝票Ⅱ型")]
	_8_DepartmentStoreType2 = 8
}

/// <summary>
/// ログインロール（SysLogin.Id_Role）
/// メニュー表示のロール別切替に使用する。
/// </summary>
[Comment("メニュー権限")]
public enum EnumLoginRole : int {
	/// <summary>
	/// 標準（ロール指定なし。全メニューを表示する）
	/// </summary>
	[Comment("標準")]
	Standard = 0,
	/// <summary>
	/// 店舗用メニュー
	/// </summary>
	[Comment("店舗用メニュー")]
	Shop = 1,
	/// <summary>
	/// 倉庫用メニュー
	/// </summary>
	[Comment("倉庫用メニュー")]
	Warehouse = 2,
	/// <summary>
	/// 本部用メニュー
	/// </summary>
	[Comment("本部用メニュー")]
	Honbu = 3,
	/// <summary>
	/// 経理用メニュー
	/// </summary>
	[Comment("経理用メニュー")]
	Keiri = 4,
}

/// <summary>
/// 担当区分（人の業務上の立場）。MasterShain.ResponsibilityScope / SysPermissionProfile.ResponsibilityScope
/// 値は担当範囲の広い順に大きくなる（比較で「これ以上の立場か」を判定できる）
/// </summary>
public enum EnumResponsibilityScope : int {
	/// <summary>
	/// 未設定（移行直後の既定値）
	/// </summary>
	Unset = 0,
	/// <summary>
	/// 店舗スタッフ
	/// </summary>
	StoreStaff = 1,
	/// <summary>
	/// 店舗責任者
	/// </summary>
	StoreManager = 2,
	/// <summary>
	/// エリアマネージャ
	/// </summary>
	AreaManager = 3,
	/// <summary>
	/// 外部Role:顧客
	/// </summary>
	EndCustomer = 10,
	/// <summary>
	/// 外部Role:仕入先
	/// </summary>
	Supplier = 11,
	/// <summary>
	/// 外部Role:得意先
	/// </summary>
	BusinessCustomer = 12,
	/// <summary>
	/// 外部Role:その他System
	/// </summary>
	ExternalSystem = 20,
	/// <summary>
	/// 外部Role:AI Agent
	/// </summary>
	AiAgent = 30,
	/// <summary>
	/// 全社担当者
	/// </summary>
	CorporateUser = 90,
}
public enum EnumResponsibilityExternalScope : int {
	Warehouse = 21,
	Ecommerce = 22,
}

/// <summary>
/// 権限の操作種別。SysPermissionProfileDetail.PermissionType
/// 操作ログのActionType（ユーザ操作ログ基盤計画書 §2.4）のうち、権限判定に使う種別に絞った部分集合
/// </summary>
public enum EnumPermissionType : int {
	View = 1,
	Create = 2,
	Update = 3,
	Delete = 4,
	Execute = 5,
	Approve = 6,
	Export = 7,
	Configure = 8,
}

[Comment("SQL方言")]
public enum EnumSqlDialect {
	[Comment("SQLite")]
	Sqlite = 0,
	[Comment("PostgreSQL")]
	Postgre = 1,
	[Comment("MariaDB")]
	MariaDb = 2,
	[Comment("MySQL")]
	MySql = 3,
	[Comment("Oracle")]
	Oracle = 4
}

/// <summary>
/// 原価方式。`MasterSysman.CostMethod`（原価4項目 詳細設計 §2.3）。
/// 方式変更時は変更適用月から最新計上月までの原価再計算が必須で、方式変更だけでは既存原価を変更しない。
/// </summary>
[Comment("原価方式")]
public enum EnumCostMethod : int {
	/// <summary>
	/// 固定原価。最終仕入原価更新・総平均原価更新のどちらも実行不可で、商品マスタの現行値を継続使用する。
	/// </summary>
	[Comment("固定原価")]
	Fixed = 0,
	/// <summary>
	/// 最終仕入原価。最終仕入原価更新のみ実行可。
	/// </summary>
	[Comment("最終仕入原価")]
	LastPurchase = 1,
	/// <summary>
	/// 総平均原価。総平均原価更新のみ実行可。
	/// </summary>
	[Comment("総平均原価")]
	TotalAverage = 2
}

/// <summary>
/// `TranGenka.ChangeKind`（原価4項目 詳細設計 §2.5.3）。同一計上月に月次原価計算行と評価替え行が
/// 並んだ場合、現在原価の解決順（§2.7）は `ChangeKind DESC` を `Vdu` より優先するため、常に評価替え行が選ばれる。
/// </summary>
[Comment("原価履歴の発生要因")]
public enum EnumCostChangeKind : int {
	/// <summary>
	/// 月次原価計算（最終仕入原価更新・総平均原価更新）による行。
	/// </summary>
	[Comment("月次原価計算")]
	Monthly = 0,
	/// <summary>
	/// 評価替えによる行。`TranGenka.SourceRevalId` が `TranGenkaReval.Id` を指す。
	/// </summary>
	[Comment("評価替え")]
	Reval = 1
}

/// <summary>
/// `MasterShohin.PurchaseType`（原価4項目 詳細設計 §2.5.8）。
/// </summary>
[Comment("仕入区分")]
public enum EnumPurchaseType : int {
	/// <summary>
	/// 通常仕入。
	/// </summary>
	[Comment("通常仕入")]
	Normal = 0,
	/// <summary>
	/// 消化仕入。`Id_ConsignmentShiire` の設定が必須になる。
	/// </summary>
	[Comment("消化仕入")]
	Consumption = 3
}

/// <summary>
/// `MasterShohin.ConsumptionCalcType`（原価4項目 詳細設計 §2.5.8、§4.4）。消化仕入の生成単価の算出方法。
/// </summary>
[Comment("消化仕入計算区分")]
public enum EnumConsumptionCalcType : int {
	/// <summary>
	/// 原価代用。`TankaShiire > 0 ? TankaShiire : ResolveCostAsOf(売上計上日)`（§4.4）。
	/// </summary>
	[Comment("原価代用")]
	CostBased = 0,
	/// <summary>
	/// 上代×掛率。`ConsumptionRateBasisPoints` と `ConsumptionRoundingUnit` / `ConsumptionRounding` を使用する。
	/// </summary>
	[Comment("上代×掛率")]
	RateBased = 1
}

/// <summary>
/// `TranConsumptionPurchaseLink.SourceType`（原価4項目 詳細設計 §2.5.5）。消化仕入の生成元となる売上テーブル種別。
/// </summary>
[Comment("消化仕入の売上元テーブル")]
public enum EnumConsumptionSourceType : int {
	/// <summary>
	/// `Tran00Uriage`（卸売上）。
	/// </summary>
	[Comment("卸売上")]
	Uriage = 0,
	/// <summary>
	/// `Tran01Tenuri`（店舗売上）。
	/// </summary>
	[Comment("店舗売上")]
	Tenuri = 1
}

/// <summary>
/// `Tran03Shiire.GeneratedKind`（原価4項目 詳細設計 §2.5.9）。
/// </summary>
[Comment("仕入の生成区分")]
public enum EnumGeneratedKind : int {
	/// <summary>
	/// 手動・通常。既存の仕入入力画面から入力された仕入。
	/// </summary>
	[Comment("手動・通常")]
	Manual = 0,
	/// <summary>
	/// 消化仕入更新による自動生成。仕入入力画面では読み取り専用として扱う。
	/// </summary>
	[Comment("消化仕入更新による自動生成")]
	ConsumptionPurchase = 1
}

/// <summary>
/// 原価4処理の画面表示区分（原価4項目 詳細設計 §2.5.6、§3.8）。月次状態はテーブルに保持せず、
/// 成果テーブルから都度算出する（U-13）ため、本enumは表示ロジックの区分としてのみ使用する。
/// 値2（諸掛）は欠番。諸掛は §3.8 で更新処理そのものを廃止し、確認専用画面になったため区分値を持たない。
/// </summary>
[Comment("原価処理区分")]
public enum EnumCostProcessKind : int {
	/// <summary>
	/// 消化仕入。成果テーブルは `TranConsumptionPurchaseLink`。
	/// </summary>
	[Comment("消化仕入")]
	ConsumptionPurchase = 1,
	/// <summary>
	/// 原価更新（最終仕入原価更新・総平均原価更新）。成果テーブルは `TranGenka`（`ChangeKind=0`）。
	/// </summary>
	[Comment("原価更新")]
	CostUpdate = 3
}

/// <summary>
/// 原価4処理の画面表示用の実行状態（原価4項目 詳細設計 §2.5.6）。永続化はせず、画面表示時に
/// 成果テーブルと入力データを都度突合して算出する。状態3（エラー）は実行直後のセッション内でのみ表示する。
/// </summary>
[Comment("原価処理の実行状態")]
public enum EnumCostProcessStatus : int {
	/// <summary>
	/// 未実行。対象月に成果行が1件も無い。
	/// </summary>
	[Comment("未実行")]
	NotRun = 0,
	/// <summary>
	/// 完了。入力データが最終成功時から変化していない。
	/// </summary>
	[Comment("完了")]
	Completed = 1,
	/// <summary>
	/// 再実行要。先行処理または入力データが最終成功後に変更された。
	/// </summary>
	[Comment("再実行要")]
	RerunRequired = 2,
	/// <summary>
	/// エラー。最終実行が失敗。永続化せず実行直後のセッション内でのみ表示する。
	/// </summary>
	[Comment("エラー")]
	Error = 3
}

/// <summary>
/// `TranGenkaReval.Method`（原価4項目 詳細設計 §2.5.11、§16.4）。評価替えの指定方式。
/// `0` は欠番（未設定を表すため定義しない。既存の <see cref="EnumCostProcessKind"/> と同じ流儀）。
/// </summary>
[Comment("評価替え指定方式")]
public enum EnumCostRevaluationMethod : int {
	/// <summary>
	/// 率一括指定。`RatePercent`（1～100）を品番原価に掛ける。
	/// </summary>
	[Comment("率一括")]
	ByRate = 1,
	/// <summary>
	/// 金額一括指定。`FixedCost`（1円以上）を品番原価へ設定する。
	/// </summary>
	[Comment("金額一括")]
	ByFixed = 2
}

/// <summary>
/// `CostRevaluationCondRow.FieldKind`（原価4項目 詳細設計 §16.4）。評価替え抽出条件の項目種別。
/// 年度は含めない（§13 U-17。CV10 `MasterShohin` に年度に相当する列が無いため）。
/// </summary>
[Comment("評価替え抽出条件項目")]
public enum EnumCostRevalCondField : int {
	/// <summary>
	/// 商品CD。
	/// </summary>
	[Comment("商品CD")]
	ShohinCode = 0,
	/// <summary>
	/// メーカー品番。
	/// </summary>
	[Comment("メーカー品番")]
	MakerCode = 1,
	/// <summary>
	/// ブランド。
	/// </summary>
	[Comment("ブランド")]
	Brand = 2,
	/// <summary>
	/// アイテム。
	/// </summary>
	[Comment("アイテム")]
	Item = 3,
	/// <summary>
	/// メーカー。
	/// </summary>
	[Comment("メーカー")]
	Maker = 4,
	/// <summary>
	/// シーズン。
	/// </summary>
	[Comment("シーズン")]
	Season = 5,
	/// <summary>
	/// 展示会。
	/// </summary>
	[Comment("展示会")]
	Tenji = 6,
	/// <summary>
	/// 素材。
	/// </summary>
	[Comment("素材")]
	Material = 7,
	/// <summary>
	/// 原産国。
	/// </summary>
	[Comment("原産国")]
	Country = 8
}

/// <summary>
/// `TranGenkaReval.GroupKey`（原価4項目 詳細設計 §2.5.11）。評価替えの集計単位。
/// </summary>
[Comment("評価替え集計単位")]
public enum EnumCostRevalGroupKey : int {
	/// <summary>
	/// ブランド別集計（既定）。
	/// </summary>
	[Comment("ブランド")]
	Brand = 0,
	/// <summary>
	/// アイテム別集計。
	/// </summary>
	[Comment("アイテム")]
	Item = 1,
	/// <summary>
	/// シーズン別集計。
	/// </summary>
	[Comment("シーズン")]
	Season = 2,
	/// <summary>
	/// メーカー別集計。
	/// </summary>
	[Comment("メーカー")]
	Maker = 3,
	/// <summary>
	/// 展示会別集計。
	/// </summary>
	[Comment("展示会")]
	Tenji = 4
}

/// <summary>
/// `TranGenkaReval.ApplyPoint`（原価4項目 詳細設計 §2.5.11、§16.4）。評価替えの適用時点。
/// </summary>
[Comment("評価替え適用時点")]
public enum EnumCostRevalApplyPoint : int {
	/// <summary>
	/// 月末。入力した計上月の締日基準期間末日を適用日とする。
	/// </summary>
	[Comment("月末")]
	MonthEnd = 0,
	/// <summary>
	/// 期末。入力計上月が属する会計年度の決算期末月を適用日とする。
	/// </summary>
	[Comment("期末")]
	FiscalEnd = 1
}

/// <summary>
/// `TranGenkaReval.Status`（原価4項目 詳細設計 §2.5.11、§16.7）。
/// </summary>
[Comment("評価替え実行状態")]
public enum EnumCostRevalStatus : int {
	/// <summary>
	/// 有効。
	/// </summary>
	[Comment("有効")]
	Active = 0,
	/// <summary>
	/// 取消。監査のため行は残し、対応する `TranGenka` 行だけを削除する。
	/// </summary>
	[Comment("取消")]
	Canceled = 1
}
