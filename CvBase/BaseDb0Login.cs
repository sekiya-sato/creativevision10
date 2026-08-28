using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;


namespace CvBase;

/// <summary>
/// ログイン管理テーブル
/// [Login management table]
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：ログインID管理テーブル パスワードは暗号化")]
[KeyDml("uq1", true, nameof(LoginId))]
[KeyDml("nk2", false, nameof(Id_Shain))]
[KeyDml("nk3", false, nameof(Id_Role))]
public sealed partial class SysLogin : BaseDbClass {
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	[Comment("社員ユニークキー")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// グループロールユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(SysLogin), nameof(Id))]
	[Comment("グループロールユニークキー")]
	public partial long Id_Role { get; set; }
	/// <summary>
	/// ログインID
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("ログインID")]
	public partial string LoginId { get; set; } = string.Empty;
	/// <summary>
	/// パスワード 暗号化by Vdc
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("パスワード 暗号化by Vdc")]
	public partial string CryptPassword { get; set; } = string.Empty;
	/// <summary>
	/// 有効期限 yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("有効期限 yyyyMMddHHmmss")]
	public partial string ExpDate { get; set; } = string.Empty;
	/// <summary>
	/// 最終ログイン yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("最終ログイン yyyyMMddHHmmss")]
	public partial string LastDate { get; set; } = string.Empty;
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("社員データ")]
	public partial CodeNameView VShain { get; set; } = new();
}
/// <summary>
/// ログイン履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：ログイン履歴テーブル")]
[KeyDml("nk1", false, nameof(Id_Login))]
[KeyDml("nk2", false, nameof(JwtUnixTime))]
public sealed partial class SysHistJwt : BaseDbClass {
	/// <summary>
	/// ログインユニークキー
	/// </summary>
	[ObservableProperty]
	[Comment("ログインユニークキー")]
	public partial long Id_Login { get; set; }
	/// <summary>
	/// JwtのUnix有効期限
	/// </summary>
	[ObservableProperty]
	[Comment("JwtのUnix有効期限")]
	public partial long JwtUnixTime { get; set; }
	/// <summary>
	/// SysHistJwtSub JSON
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[Comment("SysHistJwtSub JSON")]
	public partial SysHistJwtSub Jsub { get; set; } = new();
	/// <summary>
	/// 有効期限yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("有効期限yyyyMMddHHmmss")]
	public partial string ExpDate { get; set; } = string.Empty;
	/// <summary>
	/// IPアドレス
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("IPアドレス")]
	public partial string Ip { get; set; } = string.Empty;
	/// <summary>
	/// サービスオペレーション
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("サービスオペレーション")]
	public partial string Op { get; set; } = string.Empty;
}
/// <summary>
/// ログイン履歴サブテーブル Jsubプロパティ用
/// </summary>
[SubTableDefine]
[Comment("システム：ログイン履歴サブテーブル SysHistJwt.Jsub にJSONで格納する接続端末情報")]
public sealed partial class SysHistJwtSub : ObservableObject {
	[ObservableProperty]
	[Comment("接続端末のマシン名")]
	public partial string Machine { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("接続端末のログオンユーザー名")]
	public partial string User { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("接続端末のOSバージョン")]
	public partial string OsVer { get; set; } = string.Empty;
	/// <summary>
	/// IPアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[Newtonsoft.Json.JsonProperty("IP")]
	[Comment("IPアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17")]
	public partial string IpAddress { get; set; } = string.Empty;
	/// <summary>
	/// MACアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[Newtonsoft.Json.JsonProperty("MacA")]
	[Comment("MACアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17")]
	public partial string MacAddress { get; set; } = string.Empty;
}

/// <summary>
/// システム：権限プロファイルマスタ（システム操作権限セットの親）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：権限プロファイルマスタ 機能単位の操作権限セット")]
[KeyDml("uq1", true, nameof(Code))]
[KeyDml("nk2", false, nameof(ResponsibilityScope))]
public sealed partial class SysPermissionProfile : BaseDbClass {
	/// <summary>
	/// 権限プロファイルコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("権限プロファイルコード 例 CorporateUserDefault")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>
	/// 表示名称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("表示名称 例 全社担当者 標準権限")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 主に想定する担当区分 0=未設定 1=店舗スタッフ 2=店舗責任者 3=エリアマネージャ 4=全社担当者
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnResponsibilityScope))]
	[Comment("主に想定する担当区分 0=未設定 1=店舗スタッフ 2=店舗責任者 3=エリアマネージャ 4=全社担当者")]
	public partial int ResponsibilityScope { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumResponsibilityScope EnResponsibilityScope {
		get => (EnumResponsibilityScope)ResponsibilityScope;
		set => ResponsibilityScope = (int)value;
	}
	/// <summary>
	/// 説明
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("説明")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 担当区分の標準プロファイルか
	/// </summary>
	[ObservableProperty]
	[Comment("担当区分の標準プロファイルか")]
	public partial bool IsDefault { get; set; }
	/// <summary>
	/// 使用可能か
	/// </summary>
	[ObservableProperty]
	[Comment("使用可能か")]
	public partial bool IsActive { get; set; }
	/// <summary>
	/// 権限定義バージョン
	/// </summary>
	[ObservableProperty]
	[Comment("権限定義バージョン")]
	public partial int ProfileVersion { get; set; } = 1;

	/// <summary>
	/// 初期プロファイルデータ（担当区分ごとの標準プロファイル）
	/// </summary>
	static readonly List<SysPermissionProfile> DefaultProfileData =
	[
		new SysPermissionProfile { Id = 1, Code = "CorporateUserDefault", Name = "全社担当者 標準権限", ResponsibilityScope = (int)EnumResponsibilityScope.CorporateUser, IsDefault = true, IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 2, Code = "AreaManagerDefault", Name = "エリアマネージャ 標準権限", ResponsibilityScope = (int)EnumResponsibilityScope.AreaManager, IsDefault = true, IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 3, Code = "StoreManagerDefault", Name = "店舗責任者 標準権限", ResponsibilityScope = (int)EnumResponsibilityScope.StoreManager, IsDefault = true, IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 4, Code = "StoreStaffDefault", Name = "店舗スタッフ 標準権限", ResponsibilityScope = (int)EnumResponsibilityScope.StoreStaff, IsDefault = true, IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
	];

	/// <summary>
	/// 初期権限明細データ（機能ID×操作種別の許可/禁止）。明細に無い機能は許可として扱う運用のため、
	/// ここには「明示的に許可/禁止を定義したい行」だけを持つ
	/// </summary>
	static readonly List<SysPermissionProfileDetail> DefaultDetailData =
	[
		new SysPermissionProfileDetail { Id_PermissionProfile = 4, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 4, FunctionId = "08Zaiko.ZaikoQuery", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 4, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = false, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.Approve, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "08Zaiko.ZaikoQuery", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "08Zaiko.IdoInputSoku", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "08Zaiko.IdoInputSoku", PermissionType = (int)EnumPermissionType.Approve, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "20UriageAnalysis.SalesQuickReport", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "07Haibun.ShopHaibunInput", PermissionType = (int)EnumPermissionType.Execute, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
	];

	/// <summary>
	/// 権限プロファイル・権限明細の初期データを投入する（件数0のときだけ動作。既存DBは変更しない）
	/// </summary>
	public static void CreateDefaultData(ExDatabase db) {
		var tableCnt = db.GetTableCounts(nameof(SysPermissionProfile));
		if (tableCnt?.FirstOrDefault()?.Item3 == 0) {
			db.InsertBulk<SysPermissionProfile>(DefaultProfileData);
			db.InsertBulk<SysPermissionProfileDetail>(DefaultDetailData);
		}
	}
}

/// <summary>
/// システム：権限プロファイル明細（SysPermissionProfile と 1:N）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：権限プロファイル明細 機能ID×操作種別の許可/禁止")]
[KeyDml("uq1", true, nameof(Id_PermissionProfile), nameof(FunctionId), nameof(PermissionType))]
[KeyDml("nk2", false, nameof(FunctionId))]
public sealed partial class SysPermissionProfileDetail : BaseDbClass {
	/// <summary>
	/// 親プロファイルId
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(SysPermissionProfile))]
	[Comment("親プロファイルId")]
	public partial long Id_PermissionProfile { get; set; }
	/// <summary>
	/// CV10の機能ID
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("CV10の機能ID 例 06Uriage.ShopUriageInput")]
	public partial string FunctionId { get; set; } = string.Empty;
	/// <summary>
	/// 操作種別 1=View 2=Create 3=Update 4=Delete 5=Execute 6=Approve 7=Export 8=Configure
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnPermissionType))]
	[Comment("操作種別 1=View 2=Create 3=Update 4=Delete 5=Execute 6=Approve 7=Export 8=Configure")]
	public partial int PermissionType { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumPermissionType EnPermissionType {
		get => (EnumPermissionType)PermissionType;
		set => PermissionType = (int)value;
	}
	/// <summary>
	/// 許可/禁止
	/// </summary>
	[ObservableProperty]
	[Comment("許可/禁止")]
	public partial bool IsAllowed { get; set; }
}
