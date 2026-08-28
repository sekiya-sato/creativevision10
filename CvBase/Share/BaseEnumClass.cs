namespace CvBase.Share;

/// <summary>
/// 性別 [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
public enum EnumGender : int {
	Unknown = 0,
	Woman = 1,
	Man = 2
}

/// <summary>
/// する,しない [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
public enum EnumYesNo : int {
	No = 0,
	Yes = 1
}
/// <summary>
/// 締め日
/// </summary>
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
	DayLast = 99
}

/// <summary>
/// 得意先種別
/// </summary>
public enum EnumTokui : int {
	/// <summary>
	/// 倉庫
	/// </summary>
	_0_Soko = 0,
	/// <summary>
	/// 卸先
	/// </summary>
	_1_Oroshi = 1,
	/// <summary>
	/// 売仕店
	/// </summary>
	_3_UriShi = 3,
	/// <summary>
	/// 直営店
	/// </summary>
	_6_Tenpo = 6,
}
/// <summary>
/// ログインロール（SysLogin.Id_Role）
/// メニュー表示のロール別切替に使用する。
/// </summary>
public enum EnumLoginRole : int {
	/// <summary>
	/// 標準（ロール指定なし。全メニューを表示する）
	/// </summary>
	Standard = 0,
	/// <summary>
	/// 店舗担当
	/// </summary>
	Shop = 1,
	/// <summary>
	/// 倉庫担当
	/// </summary>
	Warehouse = 2,
	/// <summary>
	/// 本部担当
	/// </summary>
	Honbu = 3,
	/// <summary>
	/// 経理担当
	/// </summary>
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
	/// 全社担当者
	/// </summary>
	CorporateUser = 4,
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
