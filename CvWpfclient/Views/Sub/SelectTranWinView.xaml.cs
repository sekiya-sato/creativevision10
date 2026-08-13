using CvWpfclient.ViewModels.Sub;
using System.ComponentModel;
using System.Windows;

namespace CvWpfclient.Views.Sub;

public partial class SelectTranWinView : Helpers.BaseWindow {
	SelectTranWinViewModel? viewModel;

	public SelectTranWinView() {
		InitializeComponent();
		// DataGridColumn は視覚ツリーに含まれないため RelativeSource バインドが解決できない。
		// 取引先の見出しと数量列の有無は伝票種別で変わるので、ここで ViewModel から反映する。
		viewModel = DataContext as SelectTranWinViewModel;
		if (viewModel != null) {
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			ApplyColumnSettings();
		}
		Closed += (_, _) => {
			if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		};
	}

	void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(SelectTranWinViewModel.TorisakiHeader) or nameof(SelectTranWinViewModel.HasSuTotal)) {
			ApplyColumnSettings();
		}
	}

	void ApplyColumnSettings() {
		if (viewModel == null) return;
		TorisakiColumn.Header = viewModel.TorisakiHeader;
		SuTotalColumn.Visibility = viewModel.HasSuTotal ? Visibility.Visible : Visibility.Collapsed;
	}
}
