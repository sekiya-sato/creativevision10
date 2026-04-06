namespace CvWpfclient.ViewModels.Sub;

public class SelectParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public string? FromCode { get; set; }
	public string? ToCode { get; set; }
	public string? DisplayName { get; set; }
	public string? Name { get; set; }
	public int? MaxCount { get; set; }
}
