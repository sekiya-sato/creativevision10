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
/// <summary>
/// 連番管理テーブル
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[Comment("システム：連番管理テーブル BaseDbClass.Id以外の項目で連番を発行し管理する")]
public sealed partial class SysSequence : BaseDbClass {
	/// <summary>
	/// テーブル名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	string tableName = string.Empty;
	/// <summary>
	/// 対象カラム名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	string columnName = string.Empty;
	/// <summary>
	/// 連番
	/// </summary>
	[ObservableProperty]
	long seqNo;
	/// <summary>
	/// メモ (用途、意図などを記述)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(300)]
	string memo = string.Empty;
}

// ToDo : テーブルの変更履歴を保存するテーブルを作成すること。変更前と変更後のデータをJSON形式で保存すること。変更前と変更後のデータは、テーブル名、テーブルId、操作Type（追加、更新、削除）を含むこと。
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
