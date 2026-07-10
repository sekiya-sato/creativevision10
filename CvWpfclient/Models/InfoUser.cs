using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfclient.Models;

public partial class InfoUser : ObservableObject {
	[ObservableProperty]
	public partial string? OsVer { get; set; } = null;
	[ObservableProperty]
	public partial string? DotnetVer { get; set; } = null;
	[ObservableProperty]
	public partial string? ComputerName { get; set; } = null;
	[ObservableProperty]
	public partial string? UserName { get; set; } = null;
	[ObservableProperty]
	public partial string? LoginTime { get; set; } = null;
	[ObservableProperty]
	public partial string? ExpireTime { get; set; } = null;
}
