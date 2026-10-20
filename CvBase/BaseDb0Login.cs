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
/// 操作履歴テーブル（メニュー利用履歴）。旧CV HC$TRAN_CLICKCNT の後継
/// <para>
/// 誰が・いつ・どのメニューを開き・どの条件で何を実行したかを利用分析のために残す（Doc/spec/2026-10-20_メニュー利用履歴_最小変更設計案.md）。
/// 利用者（<see cref="Id_Login"/>・<see cref="Id_Shain"/>）・時刻・IPはサーバが解決する。
/// <see cref="ClientId"/>・<see cref="ScreenSession"/>・<see cref="FunctionId"/>・<see cref="MenuPath"/> はクライアントの申告値。
/// 汎用の追加・更新・削除は拒否し、記録はサーバだけが行う。
/// </para>
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：操作履歴テーブル メニュー起動・終了と照会条件・実行・印刷の記録")]
[KeyDml("nk1", false, nameof(OpTime))]
[KeyDml("nk2", false, nameof(Id_Shain), nameof(OpTime))]
[KeyDml("nk3", false, nameof(FunctionId), nameof(OpTime))]
[KeyDml("nk4", false, nameof(ScreenSession))]
public sealed partial class SysHistOperation : BaseDbClass {
	/// <summary>
	/// 記録時刻 yyyyMMddHHmmssfff（サーバ時刻）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(17)]
	[Comment("記録時刻 yyyyMMddHHmmssfff（サーバ時刻）")]
	public partial string OpTime { get; set; } = string.Empty;
	/// <summary>
	/// 種類 <see cref="OperationKind"/>
	/// </summary>
	[ObservableProperty]
	[Comment("種類 1:画面起動 2:画面終了 3:照会 4:更新 5:処理実行 6:印刷")]
	public partial int Kind { get; set; }
	/// <summary>
	/// ログインユニークキー（SysLogin.Id、JWTからサーバ解決。未ログインは0）
	/// </summary>
	[ObservableProperty]
	[Comment("ログインユニークキー SysLogin.Id（JWTからサーバ解決）")]
	public partial long Id_Login { get; set; }
	/// <summary>
	/// 社員Id（SysLogin.Id_Shain、サーバ解決）
	/// </summary>
	[ObservableProperty]
	[Comment("社員Id SysLogin.Id_Shain（サーバ解決）")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 端末インスタンスId（X-ClientId、申告）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(36)]
	[Comment("端末インスタンスId X-ClientId（申告）")]
	public partial string ClientId { get; set; } = string.Empty;
	/// <summary>
	/// 画面を1回開くごとのGUID。起動〜終了とその間の照会・実行を結ぶ（申告）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(36)]
	[Comment("画面セッション 画面を1回開くごとのGUID（申告）")]
	public partial string ScreenSession { get; set; } = string.Empty;
	/// <summary>
	/// 機能Id 例 06Uriage.ShopUriageInput（申告）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("機能Id 例 06Uriage.ShopUriageInput（申告）")]
	public partial string FunctionId { get; set; } = string.Empty;
	/// <summary>
	/// メニューの階層表示（画面起動行のみ、申告）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[Comment("メニュー階層 画面起動行のみ（申告）")]
	public partial string MenuPath { get; set; } = string.Empty;
	/// <summary>
	/// 操作 CvFlag名・PrintPdf・MenuOpen 等
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("操作 CvFlag名・PrintPdf・MenuOpen 等")]
	public partial string Op { get; set; } = string.Empty;
	/// <summary>
	/// パラメータ型名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("パラメータ型名")]
	public partial string DataType { get; set; } = string.Empty;
	/// <summary>
	/// 条件JSON（最大4000文字。超える場合は {"truncated":true,"head":…} に包む）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(4000)]
	[Comment("条件JSON 最大4000文字")]
	public partial string Condition { get; set; } = string.Empty;
	/// <summary>
	/// 結果コード 0:成功 負:エラー
	/// </summary>
	[ObservableProperty]
	[Comment("結果コード 0:成功 負:エラー")]
	public partial int ResultCode { get; set; }
	/// <summary>
	/// 取得件数（不明は-1）
	/// </summary>
	[ObservableProperty]
	[Comment("取得件数 不明は-1")]
	public partial int ResultCount { get; set; }
	/// <summary>
	/// 処理時間(ミリ秒)
	/// </summary>
	[ObservableProperty]
	[Comment("処理時間(ミリ秒)")]
	public partial int ElapsedMs { get; set; }
	/// <summary>
	/// 接続元IP（サーバ取得）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(45)]
	[Comment("接続元IP（サーバ取得）")]
	public partial string Ip { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="SysHistOperation.Kind"/> の値
/// </summary>
public static class OperationKind {
	public const int MenuOpen = 1;
	public const int MenuClose = 2;
	public const int Query = 3;
	public const int Update = 4;
	public const int Execute = 5;
	public const int Print = 6;
}

/// <summary>
/// システム：権限プロファイルマスタ（システム操作権限セットの親）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：権限プロファイルマスタ 機能単位の操作権限セット")]
[KeyDml("uq1", true, nameof(Code))]
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
	/// 説明
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("説明")]
	public partial string Memo { get; set; } = string.Empty;
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
	/// 初期プロファイルデータ（Role非依存の標準プロファイル。担当者Role・Scope・権限判定 詳細設計 §15.2）
	/// </summary>
	static readonly List<SysPermissionProfile> DefaultProfileData =
	[
		new SysPermissionProfile { Id = 1, Code = "FullAccess", Name = "全機能標準権限", IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 2, Code = "StoreOperationStandard", Name = "店舗業務標準権限", IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 3, Code = "WarehouseOperationStandard", Name = "倉庫業務標準権限", IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfile { Id = 4, Code = "ReadOnlyStandard", Name = "参照専用標準権限", IsActive = true, ProfileVersion = 1, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
	];

	/// <summary>
	/// 初期権限明細データ（機能ID×View/Editの許可/禁止）。明細に無い機能は
	/// MasterSysman.PermissionDefaultMode の既定ポリシーに従う運用のため、
	/// ここには「明示的に許可/禁止を定義したい行」だけを持つ（担当者Role・Scope・権限判定 詳細設計 §15.2）
	/// </summary>
	static readonly List<SysPermissionProfileDetail> DefaultDetailData =
	[
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "08Zaiko.ZaikoQuery", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 2, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = false, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "08Zaiko.IdoInputSoku", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "08Zaiko.IdoInputSoku", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 3, FunctionId = "08Zaiko.ZaikoQuery", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "06Uriage.ShopUriageInput", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "08Zaiko.StockForceInput", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "20UriageAnalysis.SalesQuickReport", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "07Haibun.PurchaseReceiptAllocationInput", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 1, FunctionId = "07Haibun.PurchaseReceiptAllocationInput", PermissionType = (int)EnumPermissionType.Edit, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 4, FunctionId = "08Zaiko.ZaikoQuery", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		new SysPermissionProfileDetail { Id_PermissionProfile = 4, FunctionId = "20UriageAnalysis.SalesQuickReport", PermissionType = (int)EnumPermissionType.View, IsAllowed = true, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
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
	/// 操作種別 1=View 2=Edit
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnPermissionType))]
	[Comment("操作種別 1=View 2=Edit")]
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
