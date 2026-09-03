/*
# description
DataGridSelectionBehavior は選択行への自動スクロール、選択通知受信時のフォーカス、識別子による行選択、および選択行への CurrentCell 追随を行う添付プロパティ群です。

# example
<DataGrid helpers:DataGridSelectionBehavior.AutoScrollToSelectedItem="True" />
<DataGrid helpers:DataGridSelectionBehavior.SyncCurrentCellWithSelectedItem="True" />
 */
using CommunityToolkit.Mvvm.Messaging;
using CvBase;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CvWpfclient.Helpers;

public static class DataGridSelectionBehavior {
	private static ILogger Logger => new NLogExtender<object>(nameof(DataGridSelectionBehavior));

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

	public static readonly DependencyProperty SyncCurrentCellWithSelectedItemProperty =
		DependencyProperty.RegisterAttached(
			"SyncCurrentCellWithSelectedItem",
			typeof(bool),
			typeof(DataGridSelectionBehavior),
			new PropertyMetadata(false, OnSyncCurrentCellWithSelectedItemChanged));

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

	public static bool GetSyncCurrentCellWithSelectedItem(DependencyObject obj) =>
		(bool)obj.GetValue(SyncCurrentCellWithSelectedItemProperty);

	public static void SetSyncCurrentCellWithSelectedItem(DependencyObject obj, bool value) =>
		obj.SetValue(SyncCurrentCellWithSelectedItemProperty, value);

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

	private static void OnSyncCurrentCellWithSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if ((bool)e.NewValue) {
			grid.SelectionChanged += Grid_SelectionChangedForCurrentCell;
		}
		else {
			grid.SelectionChanged -= Grid_SelectionChangedForCurrentCell;
		}
	}

	private static void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (sender is not DataGrid grid) return;
		ScrollSelectionIntoView(grid, grid.SelectedItem);
	}

	private static void Grid_SelectionChangedForCurrentCell(object sender, SelectionChangedEventArgs e) {
		if (sender is not DataGrid grid) return;
		SyncCurrentCell(grid, grid.SelectedItem);
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

	private static void ScrollSelectionIntoView(DataGrid grid, object? item) {
		if (item == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			try {
				if (!grid.Items.Contains(item)) return;
				grid.ScrollIntoView(item);
			}
			catch (Exception ex) {
				Logger.LogError(ex, "Error in ScrollSelectionIntoView");
			}
		}, DispatcherPriority.Render);
	}

	private static void BringSelectionIntoView(DataGrid grid, object? item, bool focusCell) {
		if (item == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			try {
				grid.UpdateLayout();
				grid.SelectedItem = item;
				grid.ScrollIntoView(item);
				grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
				var cell = DataGridCellHelper.GetCell(grid, grid.CurrentCell);
				if (cell != null) {
					cell.Focus();
				}
				else {
					grid.Focus();
				}
			}
			catch (Exception ex) {
				Logger.LogError(ex, "Error in BringSelectionIntoView");
			}
		}, DispatcherPriority.Render);
	}

	/// <summary>選択行に CurrentCell を追随させる。キーボード操作の起点が先頭行に戻るのを防ぐ。</summary>
	private static void SyncCurrentCell(DataGrid grid, object? item) {
		if (item == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			try {
				if (!grid.Items.Contains(item)) return;
				var column = grid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible);
				if (column == null) return;
				grid.CurrentCell = new DataGridCellInfo(item, column);

				// グリッドに既にキーボードフォーカスがある場合だけセルへフォーカスを移す。
				// ボタン操作直後にフォーカスを奪わないため。
				if (!grid.IsKeyboardFocusWithin) return;
				var cell = DataGridCellHelper.GetCell(grid, grid.CurrentCell);
				cell?.Focus();
			}
			catch (Exception ex) {
				Logger.LogError(ex, "Error in SyncCurrentCell");
			}
		}, DispatcherPriority.Render);
	}

	private static bool TryGetItemId(object item, string idPropertyName, out long id) {
		id = 0;
		var prop = item.GetType().GetProperty(idPropertyName);
		var rawValue = prop?.GetValue(item);
		if (rawValue == null) return false;

		return long.TryParse(Convert.ToString(rawValue, CultureInfo.InvariantCulture), out id);
	}
}
