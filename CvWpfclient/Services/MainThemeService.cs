using System.Windows;

namespace CvWpfclient.Services;

public enum MainTheme {
	Default,
	Green,
	Orange,
	Red,
	Purple,
}

public sealed class MainThemeService {
	private static readonly Uri DefaultThemeUri = new("/Resources/UIMainTheme.Default.xaml", UriKind.Relative);
	private static readonly Uri GreenThemeUri = new("/Resources/UIMainTheme.Green.xaml", UriKind.Relative);
	private static readonly Uri OrangeThemeUri = new("/Resources/UIMainTheme.Orange.xaml", UriKind.Relative);
	private static readonly Uri RedThemeUri = new("/Resources/UIMainTheme.Red.xaml", UriKind.Relative);
	private static readonly Uri PurpleThemeUri = new("/Resources/UIMainTheme.Purple.xaml", UriKind.Relative);
	private static readonly MainTheme[] ToggleOrder = [MainTheme.Default, MainTheme.Green, MainTheme.Orange, MainTheme.Red, MainTheme.Purple];

	public MainTheme CurrentTheme { get; private set; } = MainTheme.Default;
	public event EventHandler<MainTheme>? MainThemeChanged;

	public void ApplyMainTheme(MainTheme theme) {
		var resources = Application.Current?.Resources
			?? throw new InvalidOperationException("Application resources are not available.");
		var dictionaries = resources.MergedDictionaries;
		var mainTheme = dictionaries.FirstOrDefault(dictionary => dictionary.Source is not null
			&& dictionary.Source.OriginalString.Contains("UIMainTheme.", StringComparison.OrdinalIgnoreCase));

		if (mainTheme == null) {
			mainTheme = new ResourceDictionary();
			dictionaries.Add(mainTheme);
		}

		mainTheme.Source = GetThemeUri(theme);
		CurrentTheme = theme;
		MainThemeChanged?.Invoke(this, theme);
	}

	public void ToggleMainTheme() {
		var currentIndex = Array.IndexOf(ToggleOrder, CurrentTheme);
		var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % ToggleOrder.Length;
		ApplyMainTheme(ToggleOrder[nextIndex]);
	}

	private static Uri GetThemeUri(MainTheme theme) {
		return theme switch {
			MainTheme.Green => GreenThemeUri,
			MainTheme.Orange => OrangeThemeUri,
			MainTheme.Red => RedThemeUri,
			MainTheme.Purple => PurpleThemeUri,
			_ => DefaultThemeUri,
		};
	}
}
