using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CvWpfclient.Helpers;

/// <summary>
/// DatePicker で入力した yyyyMMdd 形式の日付を yyyy/MM/dd 表示へ正規化する添付ビヘイビア。
/// </summary>
public static class DatePickerYmdInputBehavior {
	static readonly DependencyProperty IsHookedProperty =
		DependencyProperty.RegisterAttached(
			"IsHooked",
			typeof(bool),
			typeof(DatePickerYmdInputBehavior),
			new PropertyMetadata(false));

	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(DatePickerYmdInputBehavior),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

	static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DatePicker picker) return;

		if ((bool)e.NewValue) {
			Attach(picker);
		}
		else {
			Detach(picker);
		}
	}

	static void Attach(DatePicker picker) {
		if ((bool)picker.GetValue(IsHookedProperty)) return;

		picker.DateValidationError += OnDateValidationError;
		picker.SetValue(IsHookedProperty, true);
	}

	static void Detach(DatePicker picker) {
		if (!(bool)picker.GetValue(IsHookedProperty)) return;

		picker.DateValidationError -= OnDateValidationError;
		picker.SetValue(IsHookedProperty, false);
	}

	static void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e) {
		if (sender is not DatePicker picker || !GetIsEnabled(picker)) return;

		var input = e.Text?.Trim();
		if (input is null || input.Length != 8
			|| !DateTime.TryParseExact(input, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			|| !CanSelectDate(picker, date)) {
			return;
		}

		e.ThrowException = false;
		picker.SelectedDate = date;
		picker.Text = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
	}

	static bool CanSelectDate(DatePicker picker, DateTime date) {
		var targetDate = date.Date;
		if (picker.DisplayDateStart is DateTime start && targetDate < start.Date) return false;
		if (picker.DisplayDateEnd is DateTime end && targetDate > end.Date) return false;
		return !picker.BlackoutDates.Contains(targetDate);
	}
}
