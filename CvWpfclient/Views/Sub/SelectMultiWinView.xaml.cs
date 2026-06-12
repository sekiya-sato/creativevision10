namespace CvWpfclient.Views.Sub;

public partial class SelectMultiWinView : Helpers.BaseWindow {
	public SelectMultiWinView() {
		InitializeComponent();
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		SelectGrid.Focus();
	}
}
