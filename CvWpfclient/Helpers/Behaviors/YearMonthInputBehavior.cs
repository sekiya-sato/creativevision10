using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CvWpfclient.Helpers;

/// <summary>
/// TextBox で入力した yyyyMM 形式の年月を yyyy/MM 表示へ正規化する添付ビヘイビア。
/// </summary>
public static class YearMonthInputBehavior {
	static readonly DependencyProperty IsHookedProperty =
		DependencyProperty.RegisterAttached(
			"IsHooked",
			typeof(bool),
			typeof(YearMonthInputBehavior),
			new PropertyMetadata(false));

	static readonly DependencyProperty IsNormalizingProperty =
		DependencyProperty.RegisterAttached(
			"IsNormalizing",
			typeof(bool),
			typeof(YearMonthInputBehavior),
			new PropertyMetadata(false));

	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(YearMonthInputBehavior),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

	static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not TextBox textBox) return;

		if ((bool)e.NewValue) {
			Attach(textBox);
		}
		else {
			Detach(textBox);
		}
	}

	static void Attach(TextBox textBox) {
		if ((bool)textBox.GetValue(IsHookedProperty)) return;

		textBox.TextChanged += OnTextChanged;
		textBox.Loaded += OnLoaded;
		textBox.SetValue(IsHookedProperty, true);
	}

	static void Detach(TextBox textBox) {
		if (!(bool)textBox.GetValue(IsHookedProperty)) return;

		textBox.TextChanged -= OnTextChanged;
		textBox.Loaded -= OnLoaded;
		textBox.SetValue(IsHookedProperty, false);
	}

	static void OnLoaded(object sender, RoutedEventArgs e) {
		if (sender is TextBox textBox)
			Normalize(textBox);
	}

	static void OnTextChanged(object sender, TextChangedEventArgs e) {
		if (sender is TextBox textBox)
			Normalize(textBox);
	}

	static void Normalize(TextBox textBox) {
		if (!GetIsEnabled(textBox) || (bool)textBox.GetValue(IsNormalizingProperty)) return;

		var input = textBox.Text.Trim();
		if (input.Length != 6
			|| !DateTime.TryParseExact(input + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var yearMonth)) {
			return;
		}

		textBox.SetValue(IsNormalizingProperty, true);
		try {
			textBox.Text = yearMonth.ToString("yyyy/MM", CultureInfo.InvariantCulture);
			textBox.CaretIndex = textBox.Text.Length;
		}
		finally {
			textBox.SetValue(IsNormalizingProperty, false);
		}
	}
}
