namespace CvBase;

/// <summary>
/// マニュアル排他制御の照会結果1行分（詳細設計 §2.5.2 の表に対応）。
/// クライアントの確認ダイアログ表示に使う。gRPC経由でクライアントと共有する。
/// </summary>
public sealed class ManualLockRow {
	/// <summary>一連処理名（<c>SysSequence.TableName</c>）。</summary>
	public string TableName { get; set; } = string.Empty;
	/// <summary>現在の処理名（<c>SysSequence.ColumnName</c>）。</summary>
	public string ColumnName { get; set; } = string.Empty;
	/// <summary>処理順No（<c>SysSequence.SeqNo</c>）。</summary>
	public long SeqNo { get; set; }
	/// <summary>一連処理の開始UTC Ticks（<c>SysSequence.Vdc</c>）。</summary>
	public long Vdc { get; set; }
	/// <summary>最終更新UTC Ticks（<c>SysSequence.Vdu</c>）。</summary>
	public long Vdu { get; set; }
	/// <summary>
	/// 最終更新（<see cref="Vdu"/>）からの経過秒。クライアントとサーバーで時計がずれる可能性があるため、
	/// サーバー時刻で算出したものをそのまま返す。
	/// </summary>
	public long ElapsedSecondsSinceVdu { get; set; }
	/// <summary>予想処理時間（秒）。<c>SysSequence.ExpectedDuration</c>。</summary>
	public long ExpectedDuration { get; set; }
	/// <summary>補足メモ（<c>SysSequence.Memo</c>）。</summary>
	public string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 経過時間が詳細設計 §3.4 の閾値（<c>ManualLockMonitor.ComputeThresholdTicks</c>、
	/// <c>CvDomainLogic</c>）未満かどうか。trueの場合、この処理はまだ動いている可能性がある。
	/// </summary>
	public bool IsLikelyAlive { get; set; }
}

/// <summary>
/// マニュアル排他制御の状態照会（<c>Msg061_ManualLockStatus</c>）の応答全体。
/// </summary>
public sealed class ManualLockStatus {
	/// <summary>現在排他が掛かっている行（<c>Id</c>昇順）。</summary>
	public List<ManualLockRow> Rows { get; set; } = [];
	/// <summary>
	/// いずれかの行が<see cref="ManualLockRow.IsLikelyAlive"/>かどうか。
	/// 確認ダイアログ本文の警告要否の判定に使う。
	/// </summary>
	public bool HasLikelyAlive { get; set; }
}
