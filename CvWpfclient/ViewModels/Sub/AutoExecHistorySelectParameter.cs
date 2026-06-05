namespace CvWpfclient.ViewModels.Sub;

public sealed record class AutoExecHistorySelectParameter {
	public long? FromId { get; set; }
	public long? ToId { get; set; }
	public string? FromStartTime { get; set; }
	public string? ToStartTime { get; set; }
	public int? MaxCount { get; set; }
	public string? DisplayName { get; set; }
}
