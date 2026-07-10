using CommunityToolkit.Mvvm.ComponentModel;
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
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// グループロールユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(SysLogin), nameof(Id))]
	public partial long Id_Role { get; set; }
	/// <summary>
	/// ログインID
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	public partial string LoginId { get; set; } = string.Empty;
	/// <summary>
	/// パスワード 暗号化by Vdc
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	public partial string CryptPassword { get; set; } = string.Empty;
	/// <summary>
	/// 有効期限 yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string ExpDate { get; set; } = string.Empty;
	/// <summary>
	/// 最終ログイン yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string LastDate { get; set; } = string.Empty;
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
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
	public partial long Id_Login { get; set; }
	/// <summary>
	/// JwtのUnix有効期限
	/// </summary>
	[ObservableProperty]
	public partial long JwtUnixTime { get; set; }
	/// <summary>
	/// SysHistJwtSub JSON
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	public partial SysHistJwtSub Jsub { get; set; } = new();
	/// <summary>
	/// 有効期限yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string ExpDate { get; set; } = string.Empty;
	/// <summary>
	/// IPアドレス
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Ip { get; set; } = string.Empty;
	/// <summary>
	/// サービスオペレーション
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Op { get; set; } = string.Empty;
}
/// <summary>
/// ログイン履歴サブテーブル Jsubプロパティ用
/// </summary>
[SubTableDefine]
public sealed partial class SysHistJwtSub : ObservableObject {
	[ObservableProperty]
	public partial string Machine { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string User { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string OsVer { get; set; } = string.Empty;
	/// <summary>
	/// IPアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[Newtonsoft.Json.JsonProperty("IP")]
	public partial string IpAddress { get; set; } = string.Empty;
	/// <summary>
	/// MACアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[Newtonsoft.Json.JsonProperty("MacA")]
	public partial string MacAddress { get; set; } = string.Empty;
}
