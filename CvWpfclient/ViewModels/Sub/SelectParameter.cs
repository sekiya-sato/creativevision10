namespace CvWpfclient.ViewModels.Sub;

public sealed record class SelectParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public List<long> Ids { get; set; } = [];
	public string IdsText { get; set; } = "未選択";
	public string? FromCode { get; set; }
	public string? ToCode { get; set; }
	public string? DisplayName { get; set; }
	public string? Name { get; set; }
	public int? MaxCount { get; set; }
}
