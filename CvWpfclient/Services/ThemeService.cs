/*
# file name
ThemeService.cs

# description
Light/Dark テーマの切り替えを管理するサービスクラスを実装します。

# example
ToggleTheme() で切り替え
*/
using MaterialDesignThemes.Wpf;
using System.Windows;

namespace CvWpfclient.Services;

public enum AppTheme {
	Light,
	Dark
}

public sealed class ThemeService {
	private static readonly Uri LightThemeUri = new("/Resources/UIColors.xaml", UriKind.Relative);
	private static readonly Uri DarkThemeUri = new("/Resources/UIColors.Dark.xaml", UriKind.Relative);
	private readonly PaletteHelper _paletteHelper = new();

	public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

	public void ApplyTheme(AppTheme theme) {
		var resources = Application.Current?.Resources
			?? throw new InvalidOperationException("Application resources are not available.");
		var dictionaries = resources.MergedDictionaries;
		var uiColors = dictionaries.FirstOrDefault(dictionary => dictionary.Source is not null
			&& (dictionary.Source.OriginalString.EndsWith("UIColors.xaml", StringComparison.OrdinalIgnoreCase)
			|| dictionary.Source.OriginalString.EndsWith("UIColors.Dark.xaml", StringComparison.OrdinalIgnoreCase)));
		if (uiColors == null) {
			uiColors = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri };
			dictionaries.Insert(0, uiColors);
		}
		else {
			uiColors.Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri;
		}

		var materialTheme = _paletteHelper.GetTheme();
		materialTheme.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
		_paletteHelper.SetTheme(materialTheme);

		CurrentTheme = theme;
	}

	public void ToggleTheme() {
		ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
	}
}
