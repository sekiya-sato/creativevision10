using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using NPoco;

namespace CvBase;

// 物流連携（WMS）の送受信履歴。ファイル単位のバッチと処理単位の行で結果・再処理を追跡する。
// 旧CVのワーク(HC$WKS_TORI1)・エラー退避(HC$TRAN_ERRTORI1)を、送受信共通のバッチ／行の2表に置き換える。

/// <summary>
/// 物流連携のバッチ。送受信したファイル1本を1行で表す。
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, [nameof(Direction), nameof(DataKind), nameof(Status)])]
[KeyDml("nk2", false, nameof(FileHash))]
[Comment("トランザクション：物流連携バッチ 送受信したファイル1本を1行で表す")]
public sealed partial class TranLogisticsBatch : BaseDbClass {
	/// <summary>
	/// 連携先コード（MasterConfig LogisticsLinkCode）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("連携先コード（MasterConfig LogisticsLinkCode）")]
	public partial string LinkCode { get; set; } = string.Empty;
	/// <summary>
	/// 方向（EnumLogisticsDirection）1=送信 2=受信
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumLogisticsDirection))]
	[Comment("方向（EnumLogisticsDirection）1=送信 2=受信")]
	public partial int Direction { get; set; }
	/// <summary>
	/// データ種別（LogisticsDataKind）PD/BSY/ORDER/STOCK/ZAIKO/ORDERFIX/LACK/STOCKFIX/INVENTORY
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("データ種別（LogisticsDataKind）PD/BSY/ORDER/STOCK/ZAIKO/ORDERFIX/LACK/STOCKFIX/INVENTORY")]
	public partial string DataKind { get; set; } = string.Empty;
	/// <summary>
	/// 配置・受信したファイル名（パスを含まない）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[Comment("配置・受信したファイル名（パスを含まない）")]
	public partial string FileName { get; set; } = string.Empty;
	/// <summary>
	/// 原文のSHA-256（16進小文字）。受信の重複検出に使う
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(64)]
	[Comment("原文のSHA-256（16進小文字）。受信の重複検出に使う")]
	public partial string FileHash { get; set; } = string.Empty;
	/// <summary>
	/// 状態。送信は EnumLogisticsSendStatus、受信は EnumLogisticsReceiveStatus
	/// </summary>
	[ObservableProperty]
	[Comment("状態。送信 0作成中 1配置済み 8配置失敗 9取消／受信 0取込済み 1反映済み 2一部エラー 8取込失敗 9取消")]
	public partial int Status { get; set; }
	/// <summary>
	/// 実行種別 0=自動 1=手動（SysHistAutoexec.SysHistType と同じ意味）
	/// </summary>
	[ObservableProperty]
	[Comment("実行種別 0=自動 1=手動（SysHistAutoexec.SysHistType と同じ意味）")]
	public partial int ExecType { get; set; } = 1;
	/// <summary>
	/// データ行数
	/// </summary>
	[ObservableProperty]
	[Comment("データ行数")]
	public partial int RowCount { get; set; }
	/// <summary>
	/// 正常（適用済み）行数
	/// </summary>
	[ObservableProperty]
	[Comment("正常（適用済み）行数")]
	public partial int OkCount { get; set; }
	/// <summary>
	/// エラー行数
	/// </summary>
	[ObservableProperty]
	[Comment("エラー行数")]
	public partial int ErrorCount { get; set; }
	/// <summary>
	/// 実行者の社員Id（自動実行時は0）
	/// </summary>
	[ObservableProperty]
	[Comment("実行者の社員Id（自動実行時は0）")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 結果・例外の要約。個人情報・パスを含めない
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("結果・例外の要約。個人情報・パスを含めない")]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>
/// 物流連携の行。送受信ファイルのデータ1行を1行で表す。
/// 原文 <see cref="RawText"/> は訂正しない（訂正は <see cref="Id_LineOrg"/> 付きの新しい行を追加する）。
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, [nameof(Id_Batch), nameof(LineNo)])]
[KeyDml("nk2", false, [nameof(RefTable), nameof(RefId)])]
[Comment("トランザクション：物流連携行 送受信ファイルのデータ1行。原文は訂正せず訂正版を追加する")]
public sealed partial class TranLogisticsLine : BaseDbClass {
	/// <summary>
	/// バッチId（TranLogisticsBatch.Id）
	/// </summary>
	[ObservableProperty]
	[Comment("バッチId（TranLogisticsBatch.Id）")]
	public partial long Id_Batch { get; set; }
	/// <summary>
	/// ファイル内行番号（ヘッダ行を除く1始まり）。訂正版は元行と同じ番号
	/// </summary>
	[ObservableProperty]
	[Comment("ファイル内行番号（ヘッダ行を除く1始まり）。訂正版は元行と同じ番号")]
	public partial int LineNo { get; set; }
	/// <summary>
	/// 原文1行（受信）／出力した1行（送信）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(2000)]
	[Comment("原文1行（受信）／出力した1行（送信）")]
	public partial string RawText { get; set; } = string.Empty;
	/// <summary>
	/// CV側の参照テーブル名（TranHaibun / Tran13Hachu / Tran10IdoOut）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("CV側の参照テーブル名（TranHaibun / Tran13Hachu / Tran10IdoOut）")]
	public partial string RefTable { get; set; } = string.Empty;
	/// <summary>
	/// CV側の参照Id（配分Id／元伝票Id）
	/// </summary>
	[ObservableProperty]
	[Comment("CV側の参照Id（配分Id／元伝票Id）")]
	public partial long RefId { get; set; }
	/// <summary>
	/// CV側の参照行No（元伝票の明細行No。配分は0）
	/// </summary>
	[ObservableProperty]
	[Comment("CV側の参照行No（元伝票の明細行No。配分は0）")]
	public partial int RefNo { get; set; }
	/// <summary>
	/// 送信時の参照行Vdu（送信後の変更検出）
	/// </summary>
	[ObservableProperty]
	[Comment("送信時の参照行Vdu（送信後の変更検出）")]
	public partial long RefVdu { get; set; }
	/// <summary>
	/// 解決済み商品Id
	/// </summary>
	[ObservableProperty]
	[Comment("解決済み商品Id")]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 解決済み色Id
	/// </summary>
	[ObservableProperty]
	[Comment("解決済み色Id")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// 解決済みサイズId
	/// </summary>
	[ObservableProperty]
	[Comment("解決済みサイズId")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 解決済み倉庫Id
	/// </summary>
	[ObservableProperty]
	[Comment("解決済み倉庫Id")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 数量1。送信は指示数・予定数・在庫数、受信は確定数・入荷数・棚卸数
	/// </summary>
	[ObservableProperty]
	[Comment("数量1。送信は指示数・予定数・在庫数、受信は確定数・入荷数・棚卸数")]
	public partial int Su { get; set; }
	/// <summary>
	/// 数量2。受信の欠品数、在庫送信の積送中
	/// </summary>
	[ObservableProperty]
	[Comment("数量2。受信の欠品数、在庫送信の積送中")]
	public partial int Su2 { get; set; }
	/// <summary>
	/// 作業日 yyyyMMdd（出荷日・入荷日・棚卸日）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("作業日 yyyyMMdd（出荷日・入荷日・棚卸日）")]
	public partial string WorkDay { get; set; } = string.Empty;
	/// <summary>
	/// 行状態（EnumLogisticsLineStatus）0未処理 1適用済み 2エラー 3除外 4訂正済み
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumLogisticsLineStatus))]
	[Comment("行状態（EnumLogisticsLineStatus）0未処理 1適用済み 2エラー 3除外 4訂正済み")]
	public partial int Status { get; set; }
	/// <summary>
	/// エラーコード（E01等。警告は W01等）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(10)]
	[Comment("エラーコード（E01等。警告は W01等）")]
	public partial string ErrorCode { get; set; } = string.Empty;
	/// <summary>
	/// エラー・警告の内容
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	[Comment("エラー・警告の内容")]
	public partial string ErrorMsg { get; set; } = string.Empty;
	/// <summary>
	/// 生成・更新した伝票のテーブル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("生成・更新した伝票のテーブル名")]
	public partial string TargetTable { get; set; } = string.Empty;
	/// <summary>
	/// 生成・更新した伝票のId
	/// </summary>
	[ObservableProperty]
	[Comment("生成・更新した伝票のId")]
	public partial long TargetId { get; set; }
	/// <summary>
	/// 訂正版の元行Id（訂正版でなければ0）
	/// </summary>
	[ObservableProperty]
	[Comment("訂正版の元行Id（訂正版でなければ0）")]
	public partial long Id_LineOrg { get; set; }
}
