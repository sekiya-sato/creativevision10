namespace CvWpfclient.ViewModels.Sub;

public sealed record class SelectParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public List<long> Ids { get; set; } = [];
	public string IdsText { get; set; } = "未選択";
	public string? IdsDisplayName { get; set; }
	public string? FromCode { get; set; }
	public string? ToCode { get; set; }
	public List<long> ItemIds { get; set; } = [];
	public string ItemIdsText { get; set; } = "未選択";
	public string? DisplayName { get; set; }
	public string? Name { get; set; }
	public string? Jan { get; set; }
	public int? MaxCount { get; set; }
}
