using CommunityToolkit.Mvvm.Messaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Cvnet10Wpfclient.Helpers;

public static class DataGridLongMsgFocus {
	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(DataGridLongMsgFocus),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static readonly DependencyProperty IdPropertyNameProperty =
		DependencyProperty.RegisterAttached(
			"IdPropertyName",
			typeof(string),
			typeof(DataGridLongMsgFocus),
			new PropertyMetadata("Id"));

	public static bool GetIsEnabled(DependencyObject obj) =>
		(bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) =>
		obj.SetValue(IsEnabledProperty, value);

	public static string GetIdPropertyName(DependencyObject obj) =>
		(string)obj.GetValue(IdPropertyNameProperty);

	public static void SetIdPropertyName(DependencyObject obj, string value) =>
		obj.SetValue(IdPropertyNameProperty, value);

	private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid grid) return;

		if ((bool)e.NewValue) {
			grid.Loaded += Grid_Loaded;
			grid.Unloaded += Grid_Unloaded;

			if (grid.IsLoaded) Register(grid);
		}
		else {
			Unregister(grid);
			grid.Loaded -= Grid_Loaded;
			grid.Unloaded -= Grid_Unloaded;
		}
	}

	private static void Grid_Loaded(object sender, RoutedEventArgs e) {
		if (sender is DataGrid grid) Register(grid);
	}

	private static void Grid_Unloaded(object sender, RoutedEventArgs e) {
		if (sender is DataGrid grid) Unregister(grid);
	}

	private static void Register(DataGrid grid) {
		WeakReferenceMessenger.Default.Register<ChangeSelectMsg>(grid, (recipient, message) => {
			if (recipient is DataGrid dg) FocusById(dg, message.Value);
		});
	}

	private static void Unregister(DataGrid grid) {
		WeakReferenceMessenger.Default.Unregister<ChangeSelectMsg>(grid);
	}

	private static void FocusById(DataGrid grid, long value) {
		var targetId = value;
		if(value == 0) return;
		if (grid.Items.Count == 0 || grid.Columns.Count == 0) return;

		var idPropertyName = GetIdPropertyName(grid);
		var targetItem = grid.Items.Cast<object>()
			.FirstOrDefault(item => TryGetItemId(item, idPropertyName, out var id) && id == targetId);

		if (targetItem == null) return;

		grid.Dispatcher.BeginInvoke(() => {
			grid.UpdateLayout();
			grid.SelectedItem = targetItem;
			grid.ScrollIntoView(targetItem);
			grid.CurrentItem = targetItem;
			grid.CurrentCell = new DataGridCellInfo(targetItem, grid.Columns[0]);
			grid.Focus();
		}, DispatcherPriority.Background);
	}

	private static bool TryGetItemId(object item, string idPropertyName, out long id) {
		id = 0;
		var prop = item.GetType().GetProperty(idPropertyName);
		if (prop?.GetValue(item) is null) return false;
		return long.TryParse(prop.GetValue(item)?.ToString(), out id);
	}
}