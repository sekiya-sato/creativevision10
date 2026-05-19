using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CvWpfclient.Helpers;

/// <summary>
/// DatePicker のカレンダーポップアップに「今日」ボタンを追加するアタッチドビヘイビア。
/// CalendarStyle の ControlTemplate を上書きしてボタンを埋め込むことで、
/// DatePicker.OnApplyTemplate の再実行に対して安定して動作する（Approach A）。
/// </summary>
public static class DatePickerTodayButtonBehavior {
	static readonly DependencyProperty OriginalCalendarStyleProperty =
		DependencyProperty.RegisterAttached(
			"OriginalCalendarStyle",
			typeof(Style),
			typeof(DatePickerTodayButtonBehavior),
			new PropertyMetadata(null));

	static readonly DependencyProperty IsCalendarStyleAppliedProperty =
		DependencyProperty.RegisterAttached(
			"IsCalendarStyleApplied",
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
			// リソースが使えるようになってから適用する
			if (picker.IsLoaded)
				AttachCalendarStyle(picker);
			else
				picker.Loaded += OnPickerLoaded;
		}
		else {
			picker.Loaded -= OnPickerLoaded;
			RestoreCalendarStyle(picker);
		}
	}

	static void OnPickerLoaded(object sender, RoutedEventArgs e) {
		if (sender is not DatePicker picker) return;
		picker.Loaded -= OnPickerLoaded;
		AttachCalendarStyle(picker);
	}

	/// <summary>
	/// Calendar の ControlTemplate を差し替え、CalendarItem の下に「今日」ボタンのフッターを追加する。
	/// popup.Child の直接操作（Approach B）は DatePicker.OnApplyTemplate() による
	/// _popUp.Child = this._calendar のリセットで破棄されるため使用しない。
	/// </summary>
	static void AttachCalendarStyle(DatePicker picker) {
		if ((bool)picker.GetValue(IsCalendarStyleAppliedProperty)) return;

		picker.SetValue(OriginalCalendarStyleProperty, picker.CalendarStyle);

		// ① Calendar ControlTemplate のルート: Border (PART_Root)
		// CalendarStyle の背景 Setters はテンプレート側で描画しないと反映されないため、
		// MDIX の Paper/Divider を使ってポップアップ全体の背景と枠線を明示する。
		var rootFactory = new FrameworkElementFactory(typeof(Border)) { Name = "PART_Root" };
		rootFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
		rootFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
		rootFactory.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
		rootFactory.SetResourceReference(Border.BackgroundProperty, "MaterialDesignPaper");
		rootFactory.SetResourceReference(Border.BorderBrushProperty, "MaterialDesignDivider");

		var contentFactory = new FrameworkElementFactory(typeof(StackPanel));

		// ② CalendarItem (PART_CalendarItem) — MDIX の Card ビジュアルを維持するためスタイルを継承
		var calItemFactory = new FrameworkElementFactory(typeof(CalendarItem)) { Name = "PART_CalendarItem" };
		calItemFactory.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignCalendarItemPortrait");

		// ③ フッター Border
		var footerFactory = new FrameworkElementFactory(typeof(Border));
		footerFactory.SetValue(Border.PaddingProperty, new Thickness(8));
		footerFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 1, 0, 0));
		footerFactory.SetResourceReference(Border.BorderBrushProperty, "MaterialDesignDivider");
		footerFactory.SetResourceReference(Border.BackgroundProperty, "MaterialDesignPaper");

		// ④ 「今日」ボタン
		var buttonFactory = new FrameworkElementFactory(typeof(Button));
		buttonFactory.SetValue(ContentControl.ContentProperty, "今日");
		buttonFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
		buttonFactory.SetValue(FrameworkElement.MinWidthProperty, 72.0);
		buttonFactory.SetValue(UIElement.IsEnabledProperty, CanSelectDate(picker, DateTime.Today));
		buttonFactory.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignOutlinedButton");
		buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => {
			var today = DateTime.Today;
			if (!CanSelectDate(picker, today)) return;
			picker.SelectedDate = today;
			picker.DisplayDate = today;
			picker.IsDropDownOpen = false;
		}));

		footerFactory.AppendChild(buttonFactory);
		contentFactory.AppendChild(calItemFactory);
		contentFactory.AppendChild(footerFactory);
		rootFactory.AppendChild(contentFactory);

		var template = new ControlTemplate(typeof(Calendar)) { VisualTree = rootFactory };

		// MDIX の CalendarStyle をベースとして ControlTemplate のみ差し替える
		// → DayButtonStyle, CalendarButtonStyle 等の MDIX セッターを継承しつつ
		//   テンプレートだけ追加ボタン付きのものに置き換える
		var baseStyle = picker.CalendarStyle ?? picker.TryFindResource("MaterialDesignDatePickerCalendarPortrait") as Style;
		var style = new Style(typeof(Calendar), baseStyle);
		style.Setters.Add(new Setter(Control.TemplateProperty, template));

		picker.CalendarStyle = style;
		picker.SetValue(IsCalendarStyleAppliedProperty, true);
	}

	static void RestoreCalendarStyle(DatePicker picker) {
		if (!(bool)picker.GetValue(IsCalendarStyleAppliedProperty)) return;

		picker.CalendarStyle = picker.GetValue(OriginalCalendarStyleProperty) as Style;
		picker.ClearValue(OriginalCalendarStyleProperty);
		picker.SetValue(IsCalendarStyleAppliedProperty, false);
	}

	static bool CanSelectDate(DatePicker picker, DateTime date) {
		var targetDate = date.Date;
		if (picker.DisplayDateStart is DateTime start && targetDate < start.Date) return false;
		if (picker.DisplayDateEnd is DateTime end && targetDate > end.Date) return false;
		if (picker.BlackoutDates.Contains(targetDate)) return false;

		return true;
	}
}
