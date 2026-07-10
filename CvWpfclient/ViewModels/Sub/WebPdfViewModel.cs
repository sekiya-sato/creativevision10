using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class WebPdfViewModel : ObservableObject {
	const string ReloadQueryKey = "cv_reload";
	long reloadSequence;

	[ObservableProperty]
	public partial string? Pdfdata { get; set; }

	[RelayCommand]
	void Reload() {
		if (string.IsNullOrWhiteSpace(Pdfdata)) {
			return;
		}

		Pdfdata = AddReloadQuery(Pdfdata, CreateReloadStamp());
	}

	string CreateReloadStamp() {
		var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var sequence = (++reloadSequence).ToString(CultureInfo.InvariantCulture);
		return $"{stamp}_{sequence}";
	}

	static string AddReloadQuery(string source, string stamp) {
		var fragmentIndex = source.IndexOf('#', StringComparison.Ordinal);
		var body = fragmentIndex < 0 ? source : source[..fragmentIndex];
		var fragment = fragmentIndex < 0 ? string.Empty : source[fragmentIndex..];
		var queryIndex = body.IndexOf('?', StringComparison.Ordinal);
		var path = queryIndex < 0 ? body : body[..queryIndex];
		var query = queryIndex < 0 ? string.Empty : body[(queryIndex + 1)..];

		return $"{path}?{ReplaceReloadQuery(query, stamp)}{fragment}";
	}

	static string ReplaceReloadQuery(string query, string stamp) {
		List<string> values = query.Length == 0
			? []
			: query
				.Split('&', StringSplitOptions.RemoveEmptyEntries)
				.Where(value =>
					!value.Equals(ReloadQueryKey, StringComparison.OrdinalIgnoreCase) &&
					!value.StartsWith($"{ReloadQueryKey}=", StringComparison.OrdinalIgnoreCase))
				.ToList();

		values.Add($"{ReloadQueryKey}={stamp}");
		return string.Join("&", values);
	}
}
