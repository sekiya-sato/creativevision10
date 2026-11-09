namespace CvWpfclient.ViewModels.Sub;

/// <summary>Id範囲・日付範囲・件数で一覧を絞り込む条件（<see cref="Views.Sub.IdDateRangeParamView"/> で入力する）</summary>
public sealed record class IdDateRangeParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	/// <summary>yyyyMMdd</summary>
	public string? FromDate { get; set; }
	/// <summary>yyyyMMdd</summary>
	public string? ToDate { get; set; }
	public int? MaxCount { get; set; }
	public string? DisplayName { get; set; }
	/// <summary>Id欄の見出し（例: 店舗Id）</summary>
	public string IdLabel { get; set; } = "Id";
	/// <summary>日付欄の見出し（例: 開始日）</summary>
	public string DateLabel { get; set; } = "日付";
}
