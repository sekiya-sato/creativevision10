namespace CvWpfclient.ViewModels.Sub;

public sealed record class TranPromotionSearchParameter {
	public long? FromTargetId { get; set; }
	public long? ToTargetId { get; set; }
	public string? FromDate { get; set; }
	public string? ToDate { get; set; }
	public int? MaxCount { get; set; }
	public string? DisplayName { get; set; }
	public string TargetIdLabel { get; set; } = "対象Id";
}
