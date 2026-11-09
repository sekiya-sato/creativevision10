namespace CvWpfclient.ViewModels.Sub;

/// <summary>
/// 一覧取得前の簡易条件（<see cref="Views.Sub.SimpleRangeParamView"/> で入力する）。
/// Id範囲と件数は常に表示し、カテゴリ・名前・日付範囲は呼出元が必要なものだけ表示する。
/// </summary>
public sealed record class SimpleRangeParameter {
	public string? DisplayName { get; set; }

	/// <summary>カテゴリの候補。空のときはカテゴリ行を表示しない</summary>
	public List<string> CategoryList { get; set; } = [];
	public string? SelectedCategory { get; set; }
	/// <summary>カテゴリ欄の見出し</summary>
	public string CategoryLabel { get; set; } = "カテゴリ名";
	public bool IsCategoryVisible => CategoryList.Count > 0;

	/// <summary>Id欄の見出し（例: 店舗Id）</summary>
	public string IdLabel { get; set; } = "Id";
	public long? FromId { get; set; }
	public long? ToId { get; set; }

	/// <summary>名前（部分一致）行を表示するか</summary>
	public bool IsNameVisible { get; set; }
	/// <summary>名前欄の見出し（例: ログインID）</summary>
	public string NameLabel { get; set; } = "名前";
	public string? Name { get; set; }

	/// <summary>日付範囲行を表示するか</summary>
	public bool IsDateVisible { get; set; }
	/// <summary>日付欄の見出し（例: 開始日）</summary>
	public string DateLabel { get; set; } = "日付";
	/// <summary>yyyyMMdd</summary>
	public string? FromDate { get; set; }
	/// <summary>yyyyMMdd</summary>
	public string? ToDate { get; set; }

	public int? MaxCount { get; set; }
}
