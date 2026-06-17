namespace CvWpfclient.Views.Sub;

public partial class InputBarcodeView : Helpers.BaseWindow {
	public InputBarcodeView() {
		InitializeComponent();
		Loaded += (_, _) => BarcodeTextBox.Focus();
	}
}
