using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfclient.Models;

public partial class InfoUser : ObservableObject {
	[ObservableProperty]
	string? osVer = null;
	[ObservableProperty]
	string? dotnetVer = null;
	[ObservableProperty]
	string? computerName = null;
	[ObservableProperty]
	string? userName = null;
	[ObservableProperty]
	string? loginTime = null;
	[ObservableProperty]
	string? expireTime = null;
}
public partial class InfoServerTemp : ObservableObject {
	[ObservableProperty]
	string? productVer = null;
	[ObservableProperty]
	string? buildDate = null;
	[ObservableProperty]
	string? startTime = null;
	[ObservableProperty]
	string? baseDir = null;
}
