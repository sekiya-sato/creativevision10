using CommunityToolkit.Mvvm.Messaging;
using CvWpfclient.Helpers;
using System.Windows.Controls;

namespace CvWpfclient.Views.Sub;

public partial class SelectKubunView : Helpers.BaseWindow {
	public SelectKubunView() {
		InitializeComponent();
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		WeakReferenceMessenger.Default.Register<SelectStringMessage>(this, (recipient, message) => {
			FocusDataGrid(recipient, message.Value);
		});
	}
	private void FocusDataGrid(object recipient, string initKubun) {
		SelectGrid.Focus();
		if (!string.IsNullOrEmpty(initKubun)) {
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
