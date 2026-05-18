using CvWpfclient.Services;
using CvWpfclient.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace CvWpfclient.Views;

public partial class MainMenuView : Window {
	public MainMenuView() {
		InitializeComponent();
		ApplyWindowIcon(App.MainThemeService.CurrentTheme);
		App.MainThemeService.MainThemeChanged += OnMainThemeChanged;
		Closed += MainMenuView_Closed;
	}

	private void OnMainThemeChanged(object? sender, MainTheme theme) {
		if (Dispatcher.CheckAccess()) {
			ApplyWindowIcon(theme);
			return;
		}

		Dispatcher.Invoke(() => ApplyWindowIcon(theme));
	}

	private void MainMenuView_Closed(object? sender, EventArgs e) {
		App.MainThemeService.MainThemeChanged -= OnMainThemeChanged;
		Closed -= MainMenuView_Closed;
	}

	private void ApplyWindowIcon(MainTheme theme) {
		Icon = MainThemeService.GetWindowIcon(theme);
	}

	private void MenuTree_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) {
			return;
		}
		if (DataContext is not MainMenuViewModel vm) {
			return;
		}
		if (vm.SelectedMenu?.ViewType == null) {
			return;
		}
		if (vm.DoMenuCommand.CanExecute(null)) {
			vm.DoMenuCommand.Execute(null);
			e.Handled = true;
		}
	}
}
