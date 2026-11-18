using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace UatAvalonia;

// 条件の保持・サーバ要求は行わず、既存検索画面に近い部品配置を検証する。
public partial class ProductSearchWindow : Window {
	private readonly HashSet<string> brands = [];
	private readonly HashSet<string> items = [];
	public ProductSearchWindow() {
		InitializeComponent();
		KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(false); } };
	}

	private async void SelectBrands(object? sender, RoutedEventArgs e) {
		if (await SelectManyAsync("ブランド選択", ["101 検証ブランドA", "102 検証ブランドB", "103 検証ブランドC"], brands))
			BrandText.Text = Format(brands);
	}
	private async void SelectItems(object? sender, RoutedEventArgs e) {
		if (await SelectManyAsync("アイテム選択", ["201 シャツ", "202 パンツ", "203 ジャケット"], items))
			ItemText.Text = Format(items);
	}
	private async Task<bool> SelectManyAsync(string title, string[] options, HashSet<string> selected) {
		var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
		var checks = options.Select(x => new CheckBox { Content = x, IsChecked = selected.Contains(x) }).ToArray();
		foreach (var check in checks) panel.Children.Add(check);
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
		var ok = new Button { Content = "選択", IsDefault = true };
		var cancel = new Button { Content = "取消", IsCancel = true };
		var dialog = new Window { Title = title + "（ダミー）", Width = 420, SizeToContent = SizeToContent.Height,
			WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel, CanResize = false };
		ok.Click += (_, _) => dialog.Close(true);
		cancel.Click += (_, _) => dialog.Close(false);
		dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(false); };
		buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
		if (!await dialog.ShowDialog<bool>(this)) return false;
		selected.Clear();
		foreach (var check in checks.Where(x => x.IsChecked == true)) selected.Add((string)check.Content!);
		return true;
	}
	private static string Format(HashSet<string> selected) => selected.Count == 0 ? "未選択" : string.Join(" / ", selected);
	private void ClearBrands(object? sender, RoutedEventArgs e) { brands.Clear(); BrandText.Text = "未選択"; }
	private void ClearItems(object? sender, RoutedEventArgs e) { items.Clear(); ItemText.Text = "未選択"; }
	private void AcceptSearch(object? sender, RoutedEventArgs e) => Close(true);
	private void CancelSearch(object? sender, RoutedEventArgs e) => Close(false);
}
