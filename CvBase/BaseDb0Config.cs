using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;


namespace CvBase;

/// <summary>
/// バージョン管理テーブル
/// [Login management table]
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：DB定義更新管理テーブル")]
[KeyDml("uq1", true, "DbVersion")]
public sealed partial class SysUpdateDb : BaseDbClass {
	/// <summary>
	/// レコード識別のためのシリアル8桁 yymmddnn 年月日連番 例)26040101
	/// </summary>
	[ObservableProperty]
	int dbVersion;
	/// <summary>
	/// SQL実行日 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(14)]
	string dateStart = string.Empty;
	/// <summary>
	/// SQLを実行したDbVersion
	/// </summary>
	[ObservableProperty]
	int newVersion;
	/// <summary>
	/// 実行したDDL文(複数ある場合は;区切り)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	string sql = string.Empty;
	/// <summary>
	/// メモ / 実行エラー
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(1000)]
	string memo = string.Empty;
}
