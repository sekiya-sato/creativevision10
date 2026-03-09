using System.Windows;

namespace Cvnet10Wpfclient.Helpers;

public static class OwnerWindowPlacement {
	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(OwnerWindowPlacement),
			new PropertyMetadata(false, OnIsEnabledChanged));

	public static readonly DependencyProperty LeftOffsetProperty =
		DependencyProperty.RegisterAttached(
			"LeftOffset",
			typeof(double),
			typeof(OwnerWindowPlacement),
			new PropertyMetadata(0d));

	public static readonly DependencyProperty TopOffsetProperty =
		DependencyProperty.RegisterAttached(
			"TopOffset",
			typeof(double),
			typeof(OwnerWindowPlacement),
			new PropertyMetadata(0d));

	public static bool GetIsEnabled(DependencyObject obj) =>
		(bool)obj.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject obj, bool value) =>
		obj.SetValue(IsEnabledProperty, value);

	public static double GetLeftOffset(DependencyObject obj) =>
		(double)obj.GetValue(LeftOffsetProperty);

	public static void SetLeftOffset(DependencyObject obj, double value) =>
		obj.SetValue(LeftOffsetProperty, value);

	public static double GetTopOffset(DependencyObject obj) =>
		(double)obj.GetValue(TopOffsetProperty);

	public static void SetTopOffset(DependencyObject obj, double value) =>
		obj.SetValue(TopOffsetProperty, value);

	private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not Window window) return;

		if ((bool)e.NewValue) {
			window.SourceInitialized += Window_SourceInitialized;
		}
		else {
			window.SourceInitialized -= Window_SourceInitialized;
		}
	}

	private static void Window_SourceInitialized(object? sender, EventArgs e) {
		if (sender is Window window) {
			ApplyPlacement(window);
		}
	}

	private static void ApplyPlacement(Window window) {
		if (window.Owner is not Window owner) return;

		window.WindowStartupLocation = WindowStartupLocation.Manual;

		var ownerBounds = owner.WindowState == WindowState.Normal
			? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight)
			: owner.RestoreBounds;

		window.Left = ownerBounds.Left + GetLeftOffset(window);
		window.Top = ownerBounds.Top + GetTopOffset(window);
	}
}
