using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

public static class DatePickerTodayButtonBehavior {
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
			picker.CalendarOpened += OnCalendarOpened;
		}
		else {
			picker.CalendarOpened -= OnCalendarOpened;
		}
	}

	static void OnCalendarOpened(object? sender, RoutedEventArgs e) {
		if (sender is not DatePicker picker) return;
		if (FindPopup(picker) is not Popup popup) return;
		if (popup.Child is not FrameworkElement child) return;

		if (FindNamedTodayButton(child) != null) return;

		if (FindVisualChild<Calendar>(child) is not Calendar calendar) return;
		if (calendar.Parent is not Panel originalParent) return;

		originalParent.Children.Remove(calendar);

		var host = new DockPanel();

		var footer = new Border {
			Padding = new Thickness(8),
			BorderThickness = new Thickness(0, 1, 0, 0),
			BorderBrush = TryFindBrush(picker, "MaterialDesignDivider"),
			Background = TryFindBrush(picker, "MaterialDesignPaper")
		};
		DockPanel.SetDock(footer, Dock.Bottom);

		var button = new Button {
			Name = "PART_TodayButton",
			Content = "今日",
			HorizontalAlignment = HorizontalAlignment.Right,
			MinWidth = 72,
			Style = picker.TryFindResource("MaterialDesignOutlinedButton") as Style
		};
		button.Click += (_, _) => {
			var today = DateTime.Today;
			picker.SelectedDate = today;
			picker.DisplayDate = today;
			picker.IsDropDownOpen = false;
		};

		footer.Child = button;
		host.Children.Add(footer);
		host.Children.Add(calendar);

		originalParent.Children.Add(host);
	}

	static Button? FindNamedTodayButton(DependencyObject parent) {
		return FindVisualChild<Button>(parent, b => b.Name == "PART_TodayButton");
	}

	static Popup? FindPopup(DependencyObject parent) {
		return FindVisualChild<Popup>(parent);
	}

	static Brush? TryFindBrush(FrameworkElement element, object key) {
		return element.TryFindResource(key) as Brush;
	}

	static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T typed) return typed;

			var found = FindVisualChild<T>(child);
			if (found != null) return found;
		}

		return null;
	}

	static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T typed && predicate(typed)) return typed;

			var found = FindVisualChild(child, predicate);
			if (found != null) return found;
		}

		return null;
	}
}
