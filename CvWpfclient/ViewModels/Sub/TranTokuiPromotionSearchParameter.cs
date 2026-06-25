namespace CvWpfclient.ViewModels.Sub;

public sealed record class TranTokuiPromotionSearchParameter {
	public long? FromTokuiId { get; set; }
	public long? ToTokuiId { get; set; }
	public string? FromDate { get; set; }
	public string? ToDate { get; set; }
	public int? MaxCount { get; set; }
	public string? DisplayName { get; set; }
}
