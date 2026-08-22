namespace CvBase;

/// <summary>
/// 発注残・受注残完了設定の一覧取得結果。
/// クライアントとサーバーの両方で <see cref="QueryListSqlParam"/> の行型として使用する。
/// </summary>
public sealed class ZanCompletionQueryRow {
	public long Id { get; set; }
	public long Vdu { get; set; }
	public string DenDay { get; set; } = string.Empty;
	public int RelateNo1 { get; set; }
	public int SuTotal { get; set; }
	public int KingakuTotal { get; set; }
	public int EndFlag { get; set; }
	public long Id_Tori { get; set; }
	public string ToriCode { get; set; } = string.Empty;
	public string ToriName { get; set; } = string.Empty;
	public int ActualSu { get; set; }
	public int ZanSu { get; set; }
}
