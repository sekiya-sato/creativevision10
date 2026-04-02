/*
# file name
DataGridCellEnterNavigation.cs

# description
DataGridCellEnterNavigation は DataGrid に適用する添付プロパティ Behavior です。
明細行で Enter キー押下時に現在のセル編集をコミットし、次の編集可能セル（DataGridTextColumn かつ IsReadOnly == false）へフォーカスを移動します。
行末に達した場合は次の行の最初の編集可能セルへ移動します。最終行末では何もしません。
DataGridTemplateColumn（選択ボタン付きセル）はスキップします。

# example
<DataGrid helpers:DataGridCellEnterNavigation.IsEnabled="True" ... />
 */
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.Helpers;

public static class DataGridCellEnterNavigation {
	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(DataGridCellEnterNavigation),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static bool GetIsEnabled(DependencyObject obj) =>
		(bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) =>
		obj.SetValue(IsEnabledProperty, value);

	private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if (e.OldValue is not null) {
			grid.PreviewKeyDown -= OnPreviewKeyDown;
		}

		if (e.NewValue is not null && (bool)e.NewValue) {
			grid.PreviewKeyDown += OnPreviewKeyDown;
		}
	}

	private static void OnPreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) return;
		if (sender is not DataGrid grid) return;

		// 現在のセルが有効でないなら処理を行わない
		if (grid.CurrentCell.Item == null || grid.CurrentCell.Column == null) {
			e.Handled = true;
			return;
		}

		var currentItem = grid.CurrentCell.Item;
		var currentColumn = grid.CurrentCell.Column;
		var currentDisplayIndex = currentColumn.DisplayIndex;

		// 編集中なら CommitEdit
		if (!grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true)) {
			e.Handled = true;
			return;
		}

		// 現在のアイテムのインデックスを取得
		var currentRowIndex = grid.Items.IndexOf(currentItem);
		if (currentRowIndex < 0) {
			e.Handled = true;
			return;
		}

		// 同じ行で次の編集可能カラムを探す
		var sortedColumns = grid.Columns.OrderBy(col => col.DisplayIndex).ToList();
		var nextColumn = sortedColumns.FirstOrDefault(col =>
			col.DisplayIndex > currentDisplayIndex &&
			col is DataGridTextColumn textCol &&
			!textCol.IsReadOnly);

		if (nextColumn != null) {
			// 同じ行の次の編集可能セルへ移動
			NavigateToCell(grid, currentItem, nextColumn);
			e.Handled = true;
			return;
		}

		// 行末なので、次の行を探す
		var nextRowIndex = currentRowIndex + 1;
		if (nextRowIndex >= grid.Items.Count) {
			e.Handled = true;
			return;
		}

		var nextItem = grid.Items[nextRowIndex];
		if (nextItem == null) {
			e.Handled = true;
			return;
		}

		// 次の行の最初の編集可能カラムを探す
		var firstEditableColumn = sortedColumns.FirstOrDefault(col =>
			col is DataGridTextColumn textCol &&
			!textCol.IsReadOnly);

		if (firstEditableColumn != null) {
			NavigateToCell(grid, nextItem, firstEditableColumn);
			e.Handled = true;
			return;
		}

		e.Handled = true;
	}

	private static void NavigateToCell(DataGrid grid, object item, DataGridColumn column) {
		grid.CurrentCell = new DataGridCellInfo(item, column);
		grid.ScrollIntoView(item, column);
		grid.UpdateLayout();

		grid.Dispatcher.BeginInvoke(() => {
			var cell = GetCell(grid, grid.CurrentCell);
			if (cell != null) {
				cell.Focus();
			}
			grid.BeginEdit();
		}, DispatcherPriority.Render);
	}

	private static DataGridCell? GetCell(DataGrid grid, DataGridCellInfo cellInfo) {
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
