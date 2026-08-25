namespace CvBase;

/// <summary>
/// distinct なカテゴリ名など、単一文字列列のSQL結果行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// </summary>
public sealed class CategoryNameRow {
	public string Category { get; set; } = string.Empty;
}
