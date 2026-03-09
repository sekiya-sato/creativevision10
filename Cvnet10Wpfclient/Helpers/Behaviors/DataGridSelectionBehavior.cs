using CommunityToolkit.Mvvm.Messaging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.Helpers;

public static class DataGridSelectionBehavior {
	public static readonly DependencyProperty AutoScrollToSelectedItemProperty =
		DependencyProperty.RegisterAttached(
			"AutoScrollToSelectedItem",
			typeof(bool),
			typeof(DataGridSelectionBehavior),
			new PropertyMetadata(false, OnAutoScrollToSelectedItemChanged));

	public static readonly DependencyProperty FocusOnSelectionMessageProperty =
		DependencyProperty.RegisterAttached(
			"FocusOnSelectionMessage",
			typeof(bool),
			typeof(DataGridSelectionBehavior),
			new PropertyMetadata(false, OnFocusOnSelectionMessageChanged));

	public static readonly DependencyProperty SelectionIdPropertyNameProperty =
		DependencyProperty.RegisterAttached(
			"SelectionIdPropertyName",
			typeof(string),
			typeof(DataGridSelectionBehavior),
			new PropertyMetadata("Id"));

	public static bool GetAutoScrollToSelectedItem(DependencyObject obj) =>
		(bool)obj.GetValue(AutoScrollToSelectedItemProperty);

	public static void SetAutoScrollToSelectedItem(DependencyObject obj, bool value) =>
		obj.SetValue(AutoScrollToSelectedItemProperty, value);

	public static bool GetFocusOnSelectionMessage(DependencyObject obj) =>
		(bool)obj.GetValue(FocusOnSelectionMessageProperty);

	public static void SetFocusOnSelectionMessage(DependencyObject obj, bool value) =>
		obj.SetValue(FocusOnSelectionMessageProperty, value);

	public static string GetSelectionIdPropertyName(DependencyObject obj) =>
		(string)obj.GetValue(SelectionIdPropertyNameProperty);

	public static void SetSelectionIdPropertyName(DependencyObject obj, string value) =>
		obj.SetValue(SelectionIdPropertyNameProperty, value);

	private static void OnAutoScrollToSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if ((bool)e.NewValue) {
			grid.SelectionChanged += Grid_SelectionChanged;
		}
		else {
			grid.SelectionChanged -= Grid_SelectionChanged;
		}
	}

	private static void OnFocusOnSelectionMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if ((bool)e.NewValue) {
			grid.Loaded += Grid_Loaded;
			grid.Unloaded += Grid_Unloaded;

			if (grid.IsLoaded) {
				RegisterSelectionMessage(grid);
			}
		}
		else {
			UnregisterSelectionMessage(grid);
			grid.Loaded -= Grid_Loaded;
			grid.Unloaded -= Grid_Unloaded;
		}
	}

	private static void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (sender is not DataGrid grid) return;
		BringSelectionIntoView(grid, grid.SelectedItem, focusCell: false);
	}

	private static void Grid_Loaded(object sender, RoutedEventArgs e) {
		if (sender is DataGrid grid) {
			RegisterSelectionMessage(grid);
		}
	}

	private static void Grid_Unloaded(object sender, RoutedEventArgs e) {
		if (sender is DataGrid grid) {
			UnregisterSelectionMessage(grid);
		}
	}

	private static void RegisterSelectionMessage(DataGrid grid) {
		WeakReferenceMessenger.Default.Unregister<SelectItemMessage>(grid);
		WeakReferenceMessenger.Default.Register<SelectItemMessage>(grid, static (recipient, message) => {
			if (recipient is DataGrid dataGrid) {
				FocusById(dataGrid, message.Value);
			}
		});
	}

	private static void UnregisterSelectionMessage(DataGrid grid) {
		WeakReferenceMessenger.Default.Unregister<SelectItemMessage>(grid);
	}

	private static void FocusById(DataGrid grid, long targetId) {
		if (targetId == 0 || grid.Items.Count == 0) return;

		var idPropertyName = GetSelectionIdPropertyName(grid);
		var targetItem = grid.Items.Cast<object>()
			.FirstOrDefault(item => TryGetItemId(item, idPropertyName, out var id) && id == targetId);

		BringSelectionIntoView(grid, targetItem, focusCell: true);
	}

	private static void BringSelectionIntoView(DataGrid grid, object? item, bool focusCell) {
		if (item == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			grid.UpdateLayout();
			grid.SelectedItem = item;
			grid.ScrollIntoView(item);
			grid.CurrentItem = item;

			if (!focusCell || grid.Columns.Count == 0) {
				if (focusCell) {
					grid.Focus();
				}
				return;
			}

			grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
			var cell = GetCell(grid, grid.CurrentCell);
			if (cell != null) {
				cell.Focus();
			}
			else {
				grid.Focus();
			}
		}, DispatcherPriority.Background);
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

	private static bool TryGetItemId(object item, string idPropertyName, out long id) {
		id = 0;
		var prop = item.GetType().GetProperty(idPropertyName);
		var rawValue = prop?.GetValue(item);
		if (rawValue == null) return false;

		return long.TryParse(Convert.ToString(rawValue, CultureInfo.InvariantCulture), out id);
	}
}
