using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace UatAvalonia;

public partial class MainWindow : Window, IPrototypeDialogs {
	private readonly MainViewModel vm;
	public MainWindow() {
		InitializeComponent();
		DataContext = vm = new MainViewModel(new DummyProductService(), this);
		vm.PropertyChanged += OnViewModelChanged;
		Opened += async (_, _) => {
			await vm.LoadInitialAsync();
			// 手動検証用。指定先に実ウィンドウを描画し、確認後に終了する。
			var captureDir = Environment.GetEnvironmentVariable("UAT_AVALONIA_CAPTURE_DIR");
			if (!string.IsNullOrWhiteSpace(captureDir)) {
				Directory.CreateDirectory(captureDir);
				await Task.Delay(100);
				await vm.DetailsTask;
				await CaptureAsync(Path.Combine(captureDir, "standard.png"));
				Width = MinWidth; Height = MinHeight;
				await CaptureAsync(Path.Combine(captureDir, "minimum.png"));
				vm.SelectedTab = 1; vm.DarkTheme = true;
				await CaptureAsync(Path.Combine(captureDir, "details-dark.png"));
				vm.SelectedTab = 0;
				ProductGrid.ScrollIntoView(vm.Rows.LastOrDefault(), null);
				await Task.Delay(150);
				await vm.DetailsTask;
				await CaptureAsync(Path.Combine(captureDir, "scrolled.png"));
				var scrolledCount = vm.FullLoadedCount;
				ProductGrid.ScrollIntoView(vm.Rows.FirstOrDefault(), null);
				await Task.Delay(800);
				File.WriteAllText(Path.Combine(captureDir, "load-count.txt"), $"{vm.DetailStatus}\n末尾表示後: {scrolledCount} 件 / 先頭へ戻った後: {vm.FullLoadedCount} 件");
				var search = new ProductSearchWindow();
				search.Opened += async (_, _) => {
					await Task.Delay(300);
					using var bitmap = new RenderTargetBitmap(new PixelSize((int)search.Bounds.Width, (int)search.Bounds.Height), new Vector(96, 96));
					bitmap.Render(search);
					bitmap.Save(Path.Combine(captureDir, "search.png"));
					search.Close(false);
				};
				await search.ShowDialog<bool>(this);
				Close();
			}
		};
		Closing += (_, _) => vm.StopLoading();
		Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
		KeyDown += (_, e) => {
			if (e.Key == Key.Escape) {
				if (vm.IsBusy) vm.CancelCommand.Execute(null);
				else if (vm.SelectedTab != 0) vm.SelectedTab = 0;
				else Close();
				e.Handled = true;
			}
		};
	}

	private void ProductRowLoading(object? sender, DataGridRowEventArgs e) {
		if (e.Row.DataContext is ProductRow row) vm.RequestFullRow(row);
	}

	public Task<bool> ShowProductSearchAsync() => new ProductSearchWindow().ShowDialog<bool>(this);

	private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(MainViewModel.DarkTheme))
			RequestedThemeVariant = vm.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
	}

	private async Task CaptureAsync(string path) {
		await Task.Delay(450);
		using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
		bitmap.Render(this);
		bitmap.Save(path);
	}

	public async Task<bool> ConfirmAsync(string message) {
		var dialog = CreateDialog("確認", out var panel);
		panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
		var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
		var yes = new Button { Content = "はい", IsDefault = true };
		var no = new Button { Content = "取消", IsCancel = true };
		yes.Click += (_, _) => dialog.Close(true);
		no.Click += (_, _) => dialog.Close(false);
		buttons.Children.Add(yes); buttons.Children.Add(no); panel.Children.Add(buttons);
		return await dialog.ShowDialog<bool>(this);
	}

	public async Task<string?> SelectSupplierAsync() {
		var dialog = CreateDialog("仕入先選択（ダミー）", out var panel);
		var list = new ListBox { ItemsSource = new[] { "1001 ダミー商事", "1002 テスト繊維", "1003 仮想アパレル" }, SelectedIndex = 0 };
		panel.Children.Add(list);
		var select = new Button { Content = "選択", IsDefault = true };
		select.Click += (_, _) => dialog.Close(list.SelectedItem as string);
		panel.Children.Add(select);
		return await dialog.ShowDialog<string?>(this);
	}

	private static Window CreateDialog(string title, out StackPanel panel) {
		panel = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
		var dialog = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height,
			CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
		dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
		return dialog;
	}

	private async void SaveCsv(object? sender, RoutedEventArgs e) {
		try {
			var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
				Title = "ダミー一覧をCSV保存", SuggestedFileName = "dummy-products.csv",
				FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
			});
			if (file == null) { vm.Status = "CSV保存を取り消しました。"; return; }
			await using var stream = await file.OpenWriteAsync();
			stream.SetLength(0);
			await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
			await writer.WriteLineAsync("コード,商品名,カナ,分類,単価");
			foreach (var row in vm.Rows) await writer.WriteLineAsync(string.Join(",",
				new[] { row.Code, row.Name, row.Kana, row.Category, row.IsFullLoaded ? row.Price.ToString(CultureInfo.InvariantCulture) : "未取得" }.Select(Csv)));
			vm.Status = "表示中のダミー一覧をCSVに保存しました。";
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { vm.Status = $"CSV保存失敗: {ex.Message}"; }
	}
	private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
	private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
