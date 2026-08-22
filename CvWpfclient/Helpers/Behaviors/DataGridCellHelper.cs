using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

internal static class DataGridCellHelper {
	public static DataGridCell? GetCell(DataGrid grid, DataGridCellInfo cellInfo) {
		if (cellInfo.Item == null || cellInfo.Column == null) return null;

		var row = grid.ItemContainerGenerator.ContainerFromItem(cellInfo.Item) as DataGridRow;
		if (row == null) {
			grid.ScrollIntoView(cellInfo.Item);
			row = grid.ItemContainerGenerator.ContainerFromItem(cellInfo.Item) as DataGridRow;
		}
		if (row == null) return null;

		var presenter = FindVisualChild<DataGridCellsPresenter>(row);
		if (presenter == null) {
			row.ApplyTemplate();
			presenter = FindVisualChild<DataGridCellsPresenter>(row);
		}
		if (presenter == null) return null;

		var cell = presenter.ItemContainerGenerator.ContainerFromIndex(cellInfo.Column.DisplayIndex) as DataGridCell;
		if (cell == null) {
			grid.ScrollIntoView(row, cellInfo.Column);
			cell = presenter.ItemContainerGenerator.ContainerFromIndex(cellInfo.Column.DisplayIndex) as DataGridCell;
		}

		return cell;
	}

	private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T typed) return typed;

			var found = FindVisualChild<T>(child);
			if (found != null) return found;
		}

		return null;
	}
}
