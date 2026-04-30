/*
# file name
CommandActionDataGridDoubleClick.cs

# description
DataGridのダブルクリック時に、選択中の行を引数としてICommandを実行するための添付プロパティ

# example
Veiw側:
<DataGrid
    ItemsSource="{Binding Items}"
    SelectedItem="{Binding SelectedItem}"
    helpers:DataGridDoubleClick.Command="{Binding OpenItemCommand}" />
ViewModel側:
 public ICommand OpenItemCommand  = new RelayCommand<Item>(item => MessageBox.Show(item.Name) );
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

public static class DataGridDoubleClick {
	public static readonly DependencyProperty CommandProperty =
		DependencyProperty.RegisterAttached(
			"Command",
			typeof(ICommand),
			typeof(DataGridDoubleClick),
			new PropertyMetadata(null, OnCommandChanged));
	/// <example>
	/// </example>
	public static void SetCommand(DependencyObject obj, ICommand value) =>
		obj.SetValue(CommandProperty, value);

	public static ICommand GetCommand(DependencyObject obj) =>
		(ICommand)obj.GetValue(CommandProperty);

	private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DataGrid dg) return;

		if (e.OldValue is not null) {
			dg.PreviewMouseDoubleClick -= Dg_MouseDoubleClick;
		}

		if (e.NewValue is not null) {
			dg.PreviewMouseDoubleClick += Dg_MouseDoubleClick;
		}
	}

	private static void Dg_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
		if (sender is not DataGrid dg) return;

		var row = ResolveRow(dg, e);
		if (row?.Item == null || row.Item == CollectionView.NewItemPlaceholder) return;

		var command = GetCommand(dg);
		var selectedItem = row.Item;
		if (!ReferenceEquals(dg.SelectedItem, selectedItem)) {
			dg.SelectedItem = selectedItem;
		}
		dg.CurrentItem = selectedItem;
		BindingOperations.GetBindingExpression(dg, Selector.SelectedItemProperty)?.UpdateSource();

		if (command != null && selectedItem != null && command.CanExecute(selectedItem)) {
			e.Handled = true;
			command.Execute(selectedItem);
		}
	}

	static DataGridRow? ResolveRow(DataGrid dg, MouseButtonEventArgs e) {
		if (e.OriginalSource is DependencyObject originalSource) {
			if (ItemsControl.ContainerFromElement(dg, originalSource) is DataGridRow row) {
				return row;
			}

			if (FindParent<DataGridRow>(originalSource) is { } visualRow) {
				return visualRow;
			}
		}

		var hit = VisualTreeHelper.HitTest(dg, e.GetPosition(dg))?.VisualHit;
		return hit == null ? null : FindParent<DataGridRow>(hit);
	}

	static T? FindParent<T>(DependencyObject current) where T : DependencyObject {
		while (true) {
			if (current is T target) return target;

			var parent = GetParent(current);
			if (parent == null) return null;
			current = parent;
		}
	}

	static DependencyObject? GetParent(DependencyObject current) {
		if (current is FrameworkElement fe && fe.Parent != null) return fe.Parent;
		if (current is FrameworkContentElement fce) return fce.Parent;
		if (current is not Visual) return null;

		return VisualTreeHelper.GetParent(current);
	}
}
