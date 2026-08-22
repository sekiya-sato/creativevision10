using System.Windows;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

internal static class ResourceBrushHelper {
	public static Brush? Find(string key) {
		var resource = Application.Current?.TryFindResource(key);
		return resource switch {
			Brush brush => brush,
			Color color => CreateFrozen(color),
			_ => null
		};
	}

	public static SolidColorBrush CreateFrozen(Color color) {
		SolidColorBrush brush = new(color);
		brush.Freeze();
		return brush;
	}
}
