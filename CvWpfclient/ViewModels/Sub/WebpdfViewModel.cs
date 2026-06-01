using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class WebpdfViewModel : ObservableObject {
	const string ReloadPlaceholderUrl = "https://localhost/";
	const string ReloadQueryKey = "cv_reload";

	[ObservableProperty]
	string? pdfdata;

	[RelayCommand]
	async Task ReloadAsync() {
		if (string.IsNullOrWhiteSpace(Pdfdata)) {
			return;
		}

		var current = Pdfdata;
		Pdfdata = ReloadPlaceholderUrl;
		await Task.Yield();
		Pdfdata = AddReloadQuery(current);
	}

	static string AddReloadQuery(string source) {
		var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

		if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) {
			return AddReloadQueryToRawText(source, stamp);
		}

		var builder = new UriBuilder(uri) {
			Query = ReplaceReloadQuery(uri.Query, stamp)
		};
		return builder.Uri.AbsoluteUri;
	}

	static string ReplaceReloadQuery(string query, string stamp) {
		var queryText = query.StartsWith('?') ? query[1..] : query;
		List<string> values = queryText.Length == 0
			? []
			: queryText
				.Split('&', StringSplitOptions.RemoveEmptyEntries)
				.Where(value => !value.StartsWith($"{ReloadQueryKey}=", StringComparison.OrdinalIgnoreCase))
				.ToList();

		values.Add($"{ReloadQueryKey}={stamp}");
		return string.Join("&", values);
	}

	static string AddReloadQueryToRawText(string source, string stamp) {
		var fragmentIndex = source.IndexOf('#', StringComparison.Ordinal);
		var body = fragmentIndex < 0 ? source : source[..fragmentIndex];
		var fragment = fragmentIndex < 0 ? string.Empty : source[fragmentIndex..];
		var separator = body.Contains('?', StringComparison.Ordinal) ? "&" : "?";
		return $"{body}{separator}{ReloadQueryKey}={stamp}{fragment}";
	}
}
