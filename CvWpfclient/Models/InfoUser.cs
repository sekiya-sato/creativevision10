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
