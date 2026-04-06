using CommunityToolkit.Mvvm.Messaging;
using CvWpfclient.Helpers;
using System.Windows.Controls;

namespace CvWpfclient.Views.Sub;

public partial class SelectWinView : Helpers.BaseWindow {
	public SelectWinView() {
		InitializeComponent();
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		WeakReferenceMessenger.Default.Register<SelectItemMessage>(this, (recipient, message) => {
			FocusDataGrid(recipient, message.Value);
		});
	}

	private void FocusDataGrid(object recipient, long initId) {
		SelectGrid.Focus();
		if (initId != 0) {
			if (SelectGrid.Items == null || SelectGrid.Items.Count == 0) return;
			if (SelectGrid.Columns == null || SelectGrid.Columns.Count < 2) return;
			if (SelectGrid.SelectedIndex != -1) {
				SelectGrid.ScrollIntoView(SelectGrid.Items[SelectGrid.SelectedIndex]);
				SelectGrid.CurrentCell = new DataGridCellInfo(SelectGrid.Items[0], SelectGrid.Columns[0]);
				SelectGrid.CurrentItem = SelectGrid.SelectedItem;
				SelectGrid.CurrentCell = new DataGridCellInfo();
			}
		}
	}
}
