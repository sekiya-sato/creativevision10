/*
# description
DatePickerTodayButtonBehavior は DatePicker のカレンダーポップアップへ「今日」ボタンを追加する添付プロパティ Behavior です。

# example
<DatePicker helpers:DatePickerTodayButtonBehavior.IsEnabled="True" />
 */
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

/// <summary>
/// DatePicker のカレンダーポップアップに「今日」ボタンを追加するアタッチドビヘイビア。
/// Calendar の ControlTemplate は差し替えず、MaterialDesign 標準の上部表示を維持する。
/// </summary>
public static class DatePickerTodayButtonBehavior {
	static readonly DependencyProperty IsCalendarOpenedHookedProperty =
		DependencyProperty.RegisterAttached(
			"IsCalendarOpenedHooked",
			typeof(bool),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(false));

	static readonly DependencyProperty IsTodayButtonHostProperty =
		DependencyProperty.RegisterAttached(
			"IsTodayButtonHost",
			typeof(bool),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(false));

	static readonly DependencyProperty IsTodayButtonProperty =
		DependencyProperty.RegisterAttached(
			"IsTodayButton",
			typeof(bool),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(false));

	static readonly DependencyProperty IsOriginalPopupContentProperty =
		DependencyProperty.RegisterAttached(
			"IsOriginalPopupContent",
			typeof(bool),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(false));

	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

	static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not DatePicker picker) return;

		if ((bool)e.NewValue) {
			// テンプレートが使えるようになってから Popup を拡張する。
			if (picker.IsLoaded)
				AttachCalendarHandlers(picker);
			else
				picker.Loaded += OnPickerLoaded;
		}
		else {
			picker.Loaded -= OnPickerLoaded;
			DetachCalendarHandlers(picker);
		}
	}

	static void OnPickerLoaded(object sender, RoutedEventArgs e) {
		if (sender is not DatePicker picker) return;
		picker.Loaded -= OnPickerLoaded;
		AttachCalendarHandlers(picker);
	}

	static void AttachCalendarHandlers(DatePicker picker) {
		if ((bool)picker.GetValue(IsCalendarOpenedHookedProperty)) return;

		picker.CalendarOpened += OnCalendarOpened;
		picker.CalendarClosed += OnCalendarClosed;
		picker.SetValue(IsCalendarOpenedHookedProperty, true);
	}

	static void DetachCalendarHandlers(DatePicker picker) {
		if (!(bool)picker.GetValue(IsCalendarOpenedHookedProperty)) return;

		picker.CalendarOpened -= OnCalendarOpened;
		picker.CalendarClosed -= OnCalendarClosed;
		UnwrapTodayButtonHost(picker);
		picker.SetValue(IsCalendarOpenedHookedProperty, false);
	}

	static void OnCalendarOpened(object sender, RoutedEventArgs e) {
		if (sender is not DatePicker picker) return;

		if (picker.Template.FindName("PART_Popup", picker) is not Popup popup) return;
		if (!TryFindCalendar(popup.Child, out var calendar)) return;

		WrapPopupWithTodayButton(picker, popup, calendar);
	}

	static void OnCalendarClosed(object? sender, RoutedEventArgs e) {
		if (sender is DatePicker picker)
			UnwrapTodayButtonHost(picker);
	}

	static void WrapPopupWithTodayButton(DatePicker picker, Popup popup, Calendar calendar) {
		if (popup.Child is DependencyObject currentChild && IsTodayButtonHost(currentChild)) {
			UpdateTodayButton(currentChild, picker);
			return;
		}

		if (popup.Child is not UIElement originalChild) return;

		popup.Child = null;

		var root = new Grid();
		root.SetResourceReference(Panel.BackgroundProperty, "MaterialDesignPaper");
		root.SetValue(IsTodayButtonHostProperty, true);
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		originalChild.SetValue(IsOriginalPopupContentProperty, true);
		Grid.SetRow(originalChild, 0);
		root.Children.Add(originalChild);

		var footer = new Border();
		if (picker.TryFindResource("CvDatePickerTodayFooterStyle") is Style footerStyle)
			footer.Style = footerStyle;
		else {
			footer.Padding = new Thickness(8);
			footer.BorderThickness = new Thickness(0, 1, 0, 0);
		}

		var todayButton = new Button();
		todayButton.SetValue(IsTodayButtonProperty, true);
		todayButton.Content = "今日";
		if (picker.TryFindResource("CvDatePickerTodayButtonStyle") is Style buttonStyle)
			todayButton.Style = buttonStyle;
		else {
			todayButton.HorizontalAlignment = HorizontalAlignment.Right;
			todayButton.MinWidth = 88;
			todayButton.SetResourceReference(Control.ForegroundProperty, "PrimaryHueMidBrush");
		}

		Grid.SetRow(footer, 1);
		footer.Child = todayButton;
		root.Children.Add(footer);
		popup.Child = root;

		UpdateTodayButton(root, picker);
	}

	static void UpdateTodayButton(DependencyObject host, DatePicker picker) {
		if (FindDescendant<Button>(host, button => IsTodayButton(button)) is not Button todayButton) return;

		todayButton.Tag = picker;
		todayButton.Click -= OnTodayButtonClick;
		todayButton.Click += OnTodayButtonClick;
		todayButton.IsEnabled = CanSelectDate(picker, DateTime.Today);
	}

	static void UnwrapTodayButtonHost(DatePicker picker) {
		if (picker.Template.FindName("PART_Popup", picker) is not Popup popup) return;
		if (popup.Child is not DependencyObject currentChild || !IsTodayButtonHost(currentChild)) return;

		if (FindDescendant<Button>(currentChild, button => IsTodayButton(button)) is Button todayButton) {
			todayButton.Click -= OnTodayButtonClick;
			todayButton.Tag = null;
		}

		if (FindDescendant<UIElement>(currentChild, element => IsOriginalPopupContent(element)) is not UIElement originalChild)
			return;

		originalChild.ClearValue(IsOriginalPopupContentProperty);

		var parent = VisualTreeHelper.GetParent(originalChild);
		if (parent is Panel parentPanel)
			parentPanel.Children.Remove(originalChild);
		else if (parent is Decorator parentDecorator)
			parentDecorator.Child = null;

		popup.Child = null;
		popup.Child = originalChild;
	}

	static void OnTodayButtonClick(object sender, RoutedEventArgs e) {
		if (sender is Button btn && btn.Tag is DatePicker picker) {
			if (!GetIsEnabled(picker)) return;

			var today = DateTime.Today;
			if (!CanSelectDate(picker, today)) return;
			picker.SelectedDate = today;
			picker.DisplayDate = today;
			picker.IsDropDownOpen = false;
		}
	}

	static bool CanSelectDate(DatePicker picker, DateTime date) {
		var targetDate = date.Date;
		if (picker.DisplayDateStart is DateTime start && targetDate < start.Date) return false;
		if (picker.DisplayDateEnd is DateTime end && targetDate > end.Date) return false;
		if (picker.BlackoutDates.Contains(targetDate)) return false;

		return true;
	}

	static bool TryFindCalendar(object? popupChild, out Calendar calendar) {
		if (popupChild is Calendar directCalendar) {
			calendar = directCalendar;
			return true;
		}

		if (popupChild is DependencyObject root && FindDescendant<Calendar>(root) is Calendar descendantCalendar) {
			calendar = descendantCalendar;
			return true;
		}

		calendar = null!;
		return false;
	}

	static T? FindDescendant<T>(DependencyObject root, Predicate<T>? predicate = null) where T : DependencyObject {
		foreach (var child in LogicalTreeHelper.GetChildren(root)) {
			if (child is not DependencyObject childObject) continue;

			if (childObject is T typedChild && (predicate?.Invoke(typedChild) ?? true))
				return typedChild;

			var descendant = FindDescendant(childObject, predicate);
			if (descendant is not null)
				return descendant;
		}

		return null;
	}

	static bool IsTodayButtonHost(DependencyObject obj) => (bool)obj.GetValue(IsTodayButtonHostProperty);

	static bool IsTodayButton(DependencyObject obj) => (bool)obj.GetValue(IsTodayButtonProperty);

	static bool IsOriginalPopupContent(DependencyObject obj) => (bool)obj.GetValue(IsOriginalPopupContentProperty);
}
