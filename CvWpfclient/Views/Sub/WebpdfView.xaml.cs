using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CvWpfclient.ViewModels.Sub;
using Microsoft.Web.WebView2.Core;

namespace CvWpfclient.Views.Sub;

/// <summary>
/// WebpdfView.xaml の相互作用ロジック
/// </summary>
public partial class WebpdfView : Window {
	private int _retryCount;
	private const int MaxRetryCount = 5;
	private const int RetryDelayMs = 2000;

	public WebpdfView() {
		InitializeComponent();
		PreviewKeyDown += OnPreviewKeyDown;
		WebView.PreviewKeyDown += OnPreviewKeyDown;
		WebView.CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.F5) {
			return;
		}

		e.Handled = true;
		if (DataContext is not WebpdfViewModel vm || !vm.ReloadCommand.CanExecute(null)) {
			return;
		}

		vm.ReloadCommand.Execute(null);
	}

	private void OnCoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e) {
		if (e.IsSuccess && WebView.CoreWebView2 is not null) {
			WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
		}
	}

	private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
		if (e.IsSuccess) {
			_retryCount = 0;
			return;
		}

		if (_retryCount >= MaxRetryCount || WebView?.CoreWebView2 is null) {
			return;
		}

		_retryCount++;
		await Task.Delay(RetryDelayMs);

		if (WebView?.CoreWebView2 is null) {
			return;
		}

		WebView.CoreWebView2.Reload();
	}
}
