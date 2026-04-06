using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;


namespace CvBase;

/// <summary>
/// ログイン管理テーブル
/// [Login management table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：ログインID管理テーブル")]
[KeyDml("uq1", true, "LoginId")]
[KeyDml("nk2", false, "Id_Shain")]
[KeyDml("nk3", false, "Id_Role")]
public sealed partial class SysLogin : BaseDbClass {
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Shain;
	/// <summary>
	/// グループロールユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Role;
	/// <summary>
	/// ログインID
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: System.ComponentModel.DefaultValue("")]
	string loginId = string.Empty;
	/// <summary>
	/// パスワード 暗号化by Vdc
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: System.ComponentModel.DefaultValue("")]
	string cryptPassword = string.Empty;
	/// <summary>
	/// 有効期限 yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: System.ComponentModel.DefaultValue("")]
	string expDate = string.Empty;
	/// <summary>
	/// 最終ログイン yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: System.ComponentModel.DefaultValue("")]
	string lastDate = string.Empty;
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vShain = new();
}
/// <summary>
/// ログイン履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：ログイン履歴テーブル")]
[KeyDml("nk1", false, "Id_Login")]
[KeyDml("nk2", false, "JwtUnixTime")]
public sealed partial class SysHistJwt : BaseDbClass {
	/// <summary>
	/// ログインユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Login;
	/// <summary>
	/// JwtのUnix有効期限
	/// </summary>
	[ObservableProperty]
	long jwtUnixTime;
	/// <summary>
	/// SysHistJwtSub JSON
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	SysHistJwtSub jsub = new();
	/// <summary>
	/// 有効期限yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: System.ComponentModel.DefaultValue("")]
	string expDate = string.Empty;
	/// <summary>
	/// IPアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string ip = string.Empty;
	/// <summary>
	/// サービスオペレーション
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	string op = string.Empty;
}
/// <summary>
/// ログイン履歴サブテーブル Jsubプロパティ用
/// </summary>
[NoCreate]
public sealed partial class SysHistJwtSub : ObservableObject {
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	string machine = string.Empty;
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	string user = string.Empty;
	[ObservableProperty]
	[property: System.ComponentModel.DefaultValue("")]
	string osVer = string.Empty;
	/// <summary>
	/// IPアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[property: Newtonsoft.Json.JsonProperty("IP")]
	[property: System.ComponentModel.DefaultValue("")]
	string ipAddress = string.Empty;
	/// <summary>
	/// MACアドレス : NpocoのJson実装(/src/NPoco/fastJSON/JSON.cs)が内部で直接デフォルト値を生成しているためJsonPropertyは無視される 2026/02/17
	/// </summary>
	[ObservableProperty]
	[property: Newtonsoft.Json.JsonProperty("MacA")]
	[property: System.ComponentModel.DefaultValue("")]
	string macAddress = string.Empty;
}

/// <summary>
/// 削除履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：マスター系操作履歴テーブル")]
[KeyDml("nk1", false, "Vdc")]
[KeyDml("nk2", false, "TableName")]
public sealed partial class SysHistryMaster : BaseDbClass {
	/// <summary>
	/// TableName (テーブル名)
	/// [TableName (Table Name)]
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string tableName = string.Empty;
	/// <summary>
	/// テーブルIdユニークキー
	/// </summary>
	[ObservableProperty]
	long id_Table;
	/// <summary>
	/// テーブル操作Type
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	Type operationType = typeof(string);
	/// <summary>
	/// テーブルType
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	Type tableType = typeof(string);
	/// <summary>
	/// 変更前JSONデータ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	[property: System.ComponentModel.DefaultValue("")]
	string itemBefore = string.Empty;
	/// <summary>
	/// 変更後JSONデータ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	[property: System.ComponentModel.DefaultValue("")]
	string itemAfter = string.Empty;
}
