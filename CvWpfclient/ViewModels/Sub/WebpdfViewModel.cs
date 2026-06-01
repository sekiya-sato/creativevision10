using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvWpfclient.ViewModels.Sub;

public partial class WebpdfViewModel : ObservableObject {
	[ObservableProperty]
	string? pdfdata;

	[RelayCommand]
	async Task ReloadAsync() {
		if (string.IsNullOrWhiteSpace(Pdfdata)) {
			return;
		}

		var current = Pdfdata;
		Pdfdata = null;
		await Task.Yield();
		Pdfdata = current;
	}
}
