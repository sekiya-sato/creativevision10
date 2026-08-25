namespace CvWpfclient.ViewModels.Sub;

/// <summary>
/// カテゴリ選択 + Id範囲 + 件数、の一覧取得条件。呼び出し元がCategoryListを渡して構築する。
/// </summary>
public sealed record class CategoryRangeParameter {
	public string? DisplayName { get; set; }
	public List<string> CategoryList { get; set; } = [];
	public string? SelectedCategory { get; set; }
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public int? MaxCount { get; set; }
}
