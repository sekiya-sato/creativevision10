using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.Helpers;

public static class DataGridScrollAssistant {
	public static readonly DependencyProperty AutoScrollToSelectedItemProperty =
		DependencyProperty.RegisterAttached(
			"AutoScrollToSelectedItem",
			typeof(bool),
			typeof(DataGridScrollAssistant),
			new PropertyMetadata(false, OnAutoScrollToSelectedItemChanged));

	public static bool GetAutoScrollToSelectedItem(DependencyObject obj) =>
		(bool)obj.GetValue(AutoScrollToSelectedItemProperty);

	public static void SetAutoScrollToSelectedItem(DependencyObject obj, bool value) =>
		obj.SetValue(AutoScrollToSelectedItemProperty, value);

	private static void OnAutoScrollToSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if ((bool)e.NewValue) grid.SelectionChanged += Grid_SelectionChanged;
		else grid.SelectionChanged -= Grid_SelectionChanged;
	}

	private static void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (sender is not DataGrid grid || grid.SelectedItem == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			grid.UpdateLayout();
			grid.ScrollIntoView(grid.SelectedItem);
			grid.CurrentItem = grid.SelectedItem;
		}, DispatcherPriority.Background);
	}
}