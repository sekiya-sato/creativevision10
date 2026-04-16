using CvWpfclient.Helpers;
using System.Windows.Controls;

namespace CvWpfclient.Views.Sub;

public partial class SelectPostalAddressView : Helpers.BaseWindow {
	public SelectPostalAddressView() {
		InitializeComponent();
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		FocusDataGrid();
	}

	private void FocusDataGrid() {
		SelectGrid.Focus();
		if (SelectGrid.Items != null && SelectGrid.Items.Count > 0 && SelectGrid.Columns != null && SelectGrid.Columns.Count > 0) {
			if (SelectGrid.SelectedIndex == -1) {
				SelectGrid.SelectedIndex = 0;
			}
			SelectGrid.ScrollIntoView(SelectGrid.Items[SelectGrid.SelectedIndex]);
			SelectGrid.CurrentCell = new DataGridCellInfo(SelectGrid.Items[SelectGrid.SelectedIndex], SelectGrid.Columns[0]);
			SelectGrid.CurrentItem = SelectGrid.SelectedItem;
		}
	}
}
