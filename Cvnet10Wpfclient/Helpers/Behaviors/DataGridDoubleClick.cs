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
using System.Windows.Input;

namespace Cvnet10Wpfclient.Helpers;

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
			dg.MouseDoubleClick -= Dg_MouseDoubleClick;
		}

		if (e.NewValue is not null) {
			dg.MouseDoubleClick += Dg_MouseDoubleClick;
		}
	}

	private static void Dg_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
		if (sender is not DataGrid dg) return;

		var command = GetCommand(dg);
		var selectedItem = dg.SelectedItem;

		if (command != null && selectedItem != null && command.CanExecute(selectedItem)) {
			command.Execute(selectedItem);
		}
	}
}
