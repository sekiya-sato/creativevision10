using System.Windows;

namespace CvWpfclient.Services;

public enum MainTheme {
	Default,
	Green,
}

public sealed class MainThemeService {
	private static readonly Uri DefaultThemeUri = new("/Resources/UIMainTheme.Default.xaml", UriKind.Relative);
	private static readonly Uri GreenThemeUri = new("/Resources/UIMainTheme.Green.xaml", UriKind.Relative);

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

		mainTheme.Source = theme == MainTheme.Green ? GreenThemeUri : DefaultThemeUri;
		CurrentTheme = theme;
		MainThemeChanged?.Invoke(this, theme);
	}

	public void ToggleMainTheme() {
		ApplyMainTheme(CurrentTheme == MainTheme.Green ? MainTheme.Default : MainTheme.Green);
	}
}
