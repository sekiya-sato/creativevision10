using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Wpfclient.Helpers;

namespace Cvnet10Wpfclient.Views.Sub;

public partial class SelectWinView : Helpers.BaseWindow {
	public SelectWinView() {
		InitializeComponent();
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		WeakReferenceMessenger.Default.Register<ShortMsg>(this, (recipient, message) => {
			FocusDataGrid(recipient, message);
		});
	}

	protected override void OnClosed(EventArgs e) {
		WeakReferenceMessenger.Default.Unregister<ShortMsg>(this);
		base.OnClosed(e);
	}

	private void FocusDataGrid(object recipient, ShortMsg message) {
		SelectGrid.Focus();
		if (long.TryParse(message.Value, out var initId)) {
			if (SelectGrid.Items == null || SelectGrid.Items.Count == 0) return;
			if (SelectGrid.Columns == null || SelectGrid.Columns.Count < 2) return;
			var selectedIndex = SelectGrid.SelectedIndex;
			if (selectedIndex > 0 && SelectGrid.Items.Count > (selectedIndex + 12)) {
				selectedIndex += 12;
			}
			SelectGrid.ScrollIntoView(SelectGrid.Items[selectedIndex]);
			SelectGrid.CurrentCell = new DataGridCellInfo(SelectGrid.Items[0], SelectGrid.Columns[0]);
			SelectGrid.CurrentItem = SelectGrid.SelectedItem;
			SelectGrid.CurrentCell = new DataGridCellInfo();
		}
	}
}
