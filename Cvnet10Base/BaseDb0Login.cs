using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Newtonsoft.Json;
using System.ComponentModel;


namespace Cvnet10Base;

/// <summary>
/// ログイン管理テーブル
/// [Login management table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：ログインID管理テーブル")]
[KeyDml("SysLogin_uq1",true, "LoginId")]
[KeyDml("SysLogin_nk2", false, "Id_Shain")]
[KeyDml("SysLogin_nk3", false, "Id_Role")]
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
	[property: DefaultValue("")]
	string loginId = string.Empty;
	/// <summary>
	/// パスワード 暗号化by Vdc
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string cryptPassword = string.Empty;
	/// <summary>
	/// 有効期限 yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: DefaultValue("")]
	string expDate = string.Empty;
	/// <summary>
	/// 最終ログイン yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: DefaultValue("")]
	string lastDate = string.Empty;
	/// <summary>
	/// 社員CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Shain = string.Empty;
	/// <summary>
	/// 社員名
	/// </summary>
	[ObservableProperty]
	[property: DefaultValue("")]
	[property: ColumnSizeDml(100)]
	string mei_Shain = string.Empty;
}
/// <summary>
/// ログイン履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：ログイン履歴テーブル")]
[KeyDml("SysHistJwt_nk1", false, "Id_Login")]
[KeyDml("SysHistJwt_nk2", false, "JwtUnixTime")]
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
	SysHistJwtSub? jsub;
	/// <summary>
	/// 有効期限yyyyMMddHHmmss
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	[property: DefaultValue("")]
	string expDate = string.Empty;
	/// <summary>
	/// IPアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string ip = string.Empty;
	/// <summary>
	/// サービスオペレーション
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string op = string.Empty;
}
/// <summary>
/// ログイン履歴サブテーブル Jsubプロパティ用
/// </summary>
[NoCreate]
public sealed partial class SysHistJwtSub : ObservableObject {
	[ObservableProperty]
	[property: DefaultValue("")]
	string machine = string.Empty;
	[ObservableProperty]
	[property: DefaultValue("")]
	string user = string.Empty;
	[ObservableProperty]
	[property: DefaultValue("")]
	string osVer = string.Empty;
	/// <summary>
	/// IPアドレス
	/// </summary>
	[ObservableProperty]
	[property: JsonProperty("IP")]
	[property: DefaultValue("")]
	string ipAddress = string.Empty;
	/// <summary>
	/// MACアドレス
	/// </summary>
	[ObservableProperty]
	[property: JsonProperty("MacA")]
	[property: DefaultValue("")]
	string macAddress = string.Empty;
}

/// <summary>
/// 削除履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：マスター系操作履歴テーブル")]
[KeyDml("SysHistryMaster_nk1", false, "Vdc")]
[KeyDml("SysHistryMaster_nk2", false, "TableName")]
public sealed partial class SysHistryMaster : BaseDbClass {
	/// <summary>
	/// TableName (テーブル名)
	/// [TableName (Table Name)]
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
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
	[property: DefaultValue("")]
	string itemBefore = string.Empty;
	/// <summary>
	/// 変更後JSONデータ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	[property: DefaultValue("")]
	string itemAfter = string.Empty;
}
