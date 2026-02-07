using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Media;

namespace Cvnet10Wpfclient.Services;

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
		resources["AppCommonBackgroundBrush"] = new SolidColorBrush(
			theme == AppTheme.Dark ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Color.FromRgb(0xFA, 0xEB, 0xD7));

		CurrentTheme = theme;
	}

	public void ToggleTheme() {
		ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
	}
}
