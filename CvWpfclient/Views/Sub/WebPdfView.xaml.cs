using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Input;

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
	// 画面構造がレンダリングされるタイミングで事前にWebView2の内部を確定させる(重要!)
	protected override async void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);
		try {
			// クライアントライブラリから共通のデータディレクトリを安全に解決
			string userDataFolder = System.IO.Path.Combine(ClientLib.GetDataDir(), "WebView2Profile");
			var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

			// XAML上のコントロール名 (myWebView2) に対して
			// ブラウザコアの初期化を明示的に即時完了させる（白画面・チラつき防止）
			await WebView.EnsureCoreWebView2Async(env);

			// 完了後に背景色を Material Design のテーマ色に合わせ、描画フリーズ感を排除
			WebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
		}
		catch (Exception) {
			// WebView2 was already initialized with a different CoreWebView2Environment. この場合は初期化済みなのでエラー無視でOK
			// MessageEx.ShowErrorDialog($"PDFコンポーネントの初期化に失敗しました: {ex.Message}", owner: this);
		}
	}
}
