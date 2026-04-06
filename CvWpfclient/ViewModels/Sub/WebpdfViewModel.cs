using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfclient.ViewModels.Sub;

public partial class WebpdfViewModel : ObservableObject {
	[ObservableProperty]
	string? pdfdata;
}
