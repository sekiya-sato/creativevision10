/*
# file name
TagToImageSourceConverter.cs

# description
文字列パスをBitmapImageに変換します。 

# example
View側:
<Window.Resources>
    <helpers:TagToImageSourceConverter x:Key="TagToImage" />
</Window.Resources>

<Image Source="{Binding IconPath, Converter={StaticResource TagToImage}}" />
 */

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Cvnet10Wpfclient.Helpers;

public class TagToImageSourceConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is string path) {
			return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
		}
		return new BitmapImage();
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotImplementedException();
	}
}
