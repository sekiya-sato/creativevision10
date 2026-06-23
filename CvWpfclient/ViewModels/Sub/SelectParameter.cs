namespace CvWpfclient.ViewModels.Sub;

public sealed record class SelectParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public List<long> Ids { get; set; } = [];
	public string IdsText { get; set; } = "未選択";
	public string? IdsDisplayName { get; set; }
	public bool IsToriVisible { get; set; }
	public string ToriLabel { get; set; } = "取引先Id";
	public string? ToriSearchWhere { get; set; }
	public List<long> ToriIds { get; set; } = [];
	public string ToriIdsText { get; set; } = "未選択";
	public string? FromCode { get; set; }
	public string? ToCode { get; set; }
	public List<long> ItemIds { get; set; } = [];
	public string ItemIdsText { get; set; } = "未選択";
	public string? DisplayName { get; set; }
	public string? Name { get; set; }
	public string? Jan { get; set; }
	public int? MaxCount { get; set; }
}
