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
/// 締め日
/// </summary>
[Comment("締日")]
public enum EnumShime : int {
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
