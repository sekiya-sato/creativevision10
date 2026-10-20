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
	[Comment("レコード識別のためのシリアル8桁 yymmddnn 年月日連番 例)26040101")]
	public partial int DbVersion { get; set; }
	/// <summary>
	/// SQL実行日 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("SQL実行日 date0.ToString(yyyyMMddHHmmss);")]
	public partial string DateStart { get; set; } = string.Empty;
	/// <summary>
	/// SQLを実行したDbVersion
	/// </summary>
	[ObservableProperty]
	[Comment("SQLを実行したDbVersion")]
	public partial int PreVersion { get; set; }
	/// <summary>
	/// 実行したDDL文(複数ある場合は;区切り)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("実行したDDL文(複数ある場合は;区切り)")]
	public partial string Sql { get; set; } = string.Empty;
	/// <summary>
	/// メモ / 実行エラー
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("メモ / 実行エラー")]
	public partial string Memo { get; set; } = string.Empty;
}
/// <summary>
/// 連番管理テーブル（マニュアル排他制御詳細設計 §1.1）
/// SysSeqType=0（テーブル連番）: 通常の連番発行に使用。複数行が正常。
/// SysSeqType=1（排他制御）: 一連のまとまった処理の進捗を記録。全体で1行が正常。
/// 既存列の SysSeqType=1 での用途は次のとおり:
/// TableName=一連処理名、ColumnName=現在の処理名、SeqNo=処理順No、
/// Memo=処理時間と処理内容を追記、Vdc=一連処理の開始時刻、
/// Vdu=最後に進捗を書いた時刻（監視タスク生存判定に使用）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：連番管理テーブル BaseDbClass.Id以外の項目で連番を発行し管理する")]
[KeyDml("nk1", false, [nameof(SysSeqType), nameof(TableName)])]
public sealed partial class SysSequence : BaseDbClass {
	/// <summary>
	/// テーブル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("テーブル名")]
	public partial string TableName { get; set; } = string.Empty;
	/// <summary>
	/// 対象カラム名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("対象カラム名")]
	public partial string ColumnName { get; set; } = string.Empty;
	/// <summary>
	/// 連番
	/// </summary>
	[ObservableProperty]
	[Comment("連番")]
	public partial long SeqNo { get; set; }
	/// <summary>
	/// メモ (用途、意図などを記述)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(300)]
	[Comment("メモ (用途、意図などを記述)")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 連番種別 (0:テーブル連番 1:排他制御)
	/// </summary>
	[ObservableProperty]
	[Comment("連番種別 (0:テーブル連番 1:排他制御)")]
	public partial int SysSeqType { get; set; }
	/// <summary>
	/// 予想処理時間(秒) SysSeqType=1のときだけ意味を持つ
	/// </summary>
	[ObservableProperty]
	[Comment("予想処理時間(秒) SysSeqType=1のときだけ意味を持つ")]
	public partial long ExpectedDuration { get; set; }
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：自動実行履歴テーブル 定期実行されるタスクの履歴")]
public sealed partial class SysHistAutoexec : BaseDbClass {
	/// <summary>
	/// タスク名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("タスク名")]
	public partial string TaskName { get; set; } = string.Empty;
	/// <summary>
	/// 開始日時 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("開始日時 date0.ToString(yyyyMMddHHmmss);")]
	public partial string StartTime { get; set; } = string.Empty;
	/// <summary>
	/// 終了日時 date0.ToString("yyyyMMddHHmmss");
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[Comment("終了日時 date0.ToString(yyyyMMddHHmmss);")]
	public partial string EndTime { get; set; } = string.Empty;
	/// <summary>
	/// 経過時間 (秒)
	/// </summary>
	[ObservableProperty]
	[Comment("経過時間 (秒)")]
	public partial double ElapsedTime { get; set; }
	/// <summary>
	/// 実行結果コード 0:成功、0以外:エラーコード
	/// </summary>
	[ObservableProperty]
	[Comment("実行結果コード 0:成功、0以外:エラーコード")]
	public partial int ReturnCode { get; set; }
	/// <summary>
	/// 処理件数
	/// </summary>
	[ObservableProperty]
	[Comment("処理件数")]
	public partial int Count { get; set; }
	/// <summary>
	/// メモ (エラー内容や処理内容などを記述)
	/// </summary>
	/// <remarks>
	/// マニュアル排他制御 詳細設計§2.5.3(Step T8, 2026-09-07)により2000を明示した。
	/// 従来は本属性が未指定で既定(255)が使われており、アプリ側が300文字まで書こうとしていたため
	/// 方言によっては溢れうる状態だった。
	/// 強制クリア履歴へ端末情報(IP・申告Machine/User/OsVer/MacAddress)を追記すると
	/// 従来の300文字では収まらないため。SQLiteはTEXT型のため実体の変更は不要だが、
	/// 他方言(Oracle/PostgreSQL等)は本属性からDDLを生成する際にこの上限を使う。
	/// 既存DBの列幅拡張(ALTER)が必要かどうかはUpdateDb側の調査事項であり、本属性の変更だけでは
	/// 既存の物理列は広がらない(詳細は<see cref="CvDomainLogic.ManualLockDb"/>のHistoryMemoMaxLength参照)。
	/// </remarks>
	[ObservableProperty]
	[ColumnSizeDml(2000)]
	[Comment("メモ (エラー内容や処理内容などを記述)")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 実行種別 (0:自動実行 1:手動実行)
	/// </summary>
	[ObservableProperty]
	[Comment("実行種別 (0:自動実行 1:手動実行)")]
	public partial int SysHistType { get; set; }
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
	[Comment("TableName (テーブル名)")]
	public partial string TableName { get; set; } = string.Empty;

	/// <summary>
	/// テーブルIdユニークキー
	/// </summary>
	[ObservableProperty]
	[Comment("テーブルIdユニークキー")]
	public partial long Id_Table { get; set; }
	/// <summary>
	/// テーブル操作Type
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("テーブル操作Type")]
	public partial Type OperationType { get; set; } = typeof(string);
	/// <summary>
	/// テーブルType
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("テーブルType")]
	public partial Type TableType { get; set; } = typeof(string);
	/// <summary>
	/// 変更前JSONデータ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("変更前JSONデータ")]
	public partial string ItemBefore { get; set; } = string.Empty;
	/// <summary>
	/// 変更後JSONデータ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("変更後JSONデータ")]
	public partial string ItemAfter { get; set; } = string.Empty;
}

/// <summary>
/// AI照会の共有保存テーブル（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §5.1）
/// <para>
/// 保存項目は日付・タイトル・SQL文・SQLパラメータ・ユーザId・SQL内容の説明・確定した条件（AIの要約）の7項目。
/// <see cref="SaveDate"/> と <see cref="Id_Login"/> はサーバが保存時に打刻し、クライアントの申告値を使わない。
/// 全員が参照・更新でき、<see cref="Id_Login"/> を権限制御には使わない。
/// </para>
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：AI照会の共有保存SQL")]
[KeyDml("nk1", false, nameof(Title))]
[KeyDml("nk2", false, nameof(Id_Login))]
public sealed partial class SysAiQuery : BaseDbClass {
	/// <summary>
	/// 保存日 yyyyMMdd（サーバ打刻）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("保存日 yyyyMMdd（サーバ打刻）")]
	public partial string SaveDate { get; set; } = string.Empty;
	/// <summary>
	/// タイトル（会話から自動生成）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("タイトル（会話から自動生成）")]
	public partial string Title { get; set; } = string.Empty;
	/// <summary>
	/// 検査済みのSQLite照会SQL
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(4000)]
	[Comment("検査済みのSQLite照会SQL")]
	public partial string SqlText { get; set; } = string.Empty;
	/// <summary>
	/// SQLパラメータ（AiQueryParamのJSON配列。並び順が@0,@1,…に対応）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(4000)]
	[Comment("SQLパラメータ（AiQueryParamのJSON配列。並び順が@0,@1,…に対応）")]
	public partial string SqlParams { get; set; } = string.Empty;
	/// <summary>
	/// 保存したユーザ（SysLogin.Id、サーバ打刻）。権限制御には使わない
	/// </summary>
	[ObservableProperty]
	[Comment("保存したユーザ（SysLogin.Id、サーバ打刻）")]
	public partial long Id_Login { get; set; }
	/// <summary>
	/// SQL内容の説明（会話から推測・確定した集計対象・項目・条件）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(2000)]
	[Comment("SQL内容の説明（会話から推測・確定した集計対象・項目・条件）")]
	public partial string Description { get; set; } = string.Empty;
	/// <summary>
	/// 確定した条件（AIの要約）。開いたときに画面へ戻す参考表示で、SQLの実行には使わない
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(4000)]
	[Comment("確定した条件（AIの要約）")]
	public partial string SpecSummary { get; set; } = string.Empty;
}
