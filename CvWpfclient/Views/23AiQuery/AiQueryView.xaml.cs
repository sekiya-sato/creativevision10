using CvWpfclient.ViewModels._23AiQuery;
using System.Collections.Specialized;
using System.Data;
using System.Windows.Controls;
using System.Windows.Input;

namespace CvWpfclient.Views._23AiQuery;

public partial class AiQueryView : Helpers.BaseWindow {
	public AiQueryView() {
		InitializeComponent();
		// 会話が増えたら最新の発言まで送る（表示だけの処理）
		if (DataContext is AiQueryViewModel vm) {
			vm.ChatLines.CollectionChanged += ChatLines_CollectionChanged;
		}
	}

	void ChatLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0) {
			ChatList.ScrollIntoView(e.NewItems[^1]);
		}
	}

	/// <summary>結果の自動列は、列名の置き換え前（SQLの別名）を見出しに出す</summary>
	void ResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) {
		if (sender is DataGrid { ItemsSource: DataView view } && view.Table?.Columns[e.PropertyName] is DataColumn column) {
			e.Column.Header = column.Caption;
		}
	}

	/// <summary>保存一覧のダブルクリックで開く</summary>
	void SavedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
		if (DataContext is not AiQueryViewModel vm || sender is not DataGrid grid || grid.SelectedItem == null) return;
		var command = grid.Tag as string == "Shared" ? vm.OpenSharedCommand : vm.OpenPersonalCommand;
		if (command.CanExecute(null)) command.Execute(null);
	}
}
