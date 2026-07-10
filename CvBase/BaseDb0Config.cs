using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;


namespace CvBase;

/// <summary>
/// バージョン管理テーブル
/// [Login management table]
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：DB定義更新管理テーブル")]
[KeyDml("uq1", true, nameof(DbVersion))]
public sealed partial class SysUpdateDb : BaseDbClass {
	/// <summary>
	/// レコード識別のためのシリアル8桁 yymmddnn 年月日連番 例)26040101
	/// </summary>
	[ObservableProperty]
	public partial int DbVersion { get; set; }
	/// <summary>
	/// SQL実行日 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string DateStart { get; set; } = string.Empty;
	/// <summary>
	/// SQLを実行したDbVersion
	/// </summary>
	[ObservableProperty]
	public partial int PreVersion { get; set; }
	/// <summary>
	/// 実行したDDL文(複数ある場合は;区切り)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	public partial string Sql { get; set; } = string.Empty;
	/// <summary>
	/// メモ / 実行エラー
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	public partial string Memo { get; set; } = string.Empty;
}
/// <summary>
/// 連番管理テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：連番管理テーブル BaseDbClass.Id以外の項目で連番を発行し管理する")]
public sealed partial class SysSequence : BaseDbClass {
	/// <summary>
	/// テーブル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	public partial string TableName { get; set; } = string.Empty;
	/// <summary>
	/// 対象カラム名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	public partial string ColumnName { get; set; } = string.Empty;
	/// <summary>
	/// 連番
	/// </summary>
	[ObservableProperty]
	public partial long SeqNo { get; set; }
	/// <summary>
	/// メモ (用途、意図などを記述)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(300)]
	public partial string Memo { get; set; } = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：自動実行履歴テーブル 定期実行されるタスクの履歴")]
public sealed partial class SysHistAutoexec : BaseDbClass {
	/// <summary>
	/// タスク名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string TaskName { get; set; } = string.Empty;
	/// <summary>
	/// 開始日時 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string StartTime { get; set; } = string.Empty;
	/// <summary>
	/// 終了日時 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	public partial string EndTime { get; set; } = string.Empty;
	/// <summary>
	/// 経過時間 (秒)
	/// </summary>
	[ObservableProperty]
	public partial int ElapsedTime { get; set; }
	/// <summary>
	/// 実行結果コード 0:成功、0以外:エラーコード
	/// </summary>
	[ObservableProperty]
	public partial int ReturnCode { get; set; }
	/// <summary>
	/// 処理件数
	/// </summary>
	[ObservableProperty]
	public partial int Count { get; set; }
	/// <summary>
	/// メモ (エラー内容や処理内容などを記述)
	/// </summary>
	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;
}

// ToDo : テーブルの変更履歴を保存するテーブルを作成すること。変更前と変更後のデータをJSON形式で保存すること。変更前と変更後のデータは、テーブル名、テーブルId、操作Type（追加、更新、削除）を含むこと。
/// <summary>
/// 削除履歴テーブル
/// [Login history table]
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：マスター系操作履歴テーブル 2026/06/04現在、まだ実DBは作成しない。保存仕様を検討中")]
[KeyDml("nk1", false, nameof(Vdc))]
[KeyDml("nk2", false, nameof(TableName))]
[NoCreate]
public sealed partial class SysHistMaster : BaseDbClass {
	/// <summary>
	/// TableName (テーブル名)
	/// [TableName (Table Name)]
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string TableName { get; set; } = string.Empty;

	/// <summary>
	/// テーブルIdユニークキー
	/// </summary>
	[ObservableProperty]
	public partial long Id_Table { get; set; }
	/// <summary>
	/// テーブル操作Type
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial Type OperationType { get; set; } = typeof(string);
	/// <summary>
	/// テーブルType
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial Type TableType { get; set; } = typeof(string);
	/// <summary>
	/// 変更前JSONデータ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	public partial string ItemBefore { get; set; } = string.Empty;
	/// <summary>
	/// 変更後JSONデータ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	public partial string ItemAfter { get; set; } = string.Empty;
}
