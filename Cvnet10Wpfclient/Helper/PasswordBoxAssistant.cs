using System.Windows;
using System.Windows.Controls;

namespace Cvnet10Wpfclient.Helpers;


public static class PasswordBoxAssistant {
	public static readonly DependencyProperty BoundPasswordProperty =
		DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxAssistant),
			new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

	public static readonly DependencyProperty BindPasswordProperty =
		DependencyProperty.RegisterAttached("BindPassword", typeof(bool), typeof(PasswordBoxAssistant),
			new PropertyMetadata(false, OnBindPasswordChanged));

	// 更新中フラグ（無限ループ回避用）
	private static readonly DependencyProperty IsUpdatingProperty =
		DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxAssistant),
			new PropertyMetadata(false));

	public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
	public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

	public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);
	public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

	private static bool GetIsUpdating(DependencyObject obj) => (bool)obj.GetValue(IsUpdatingProperty);
	private static void SetIsUpdating(DependencyObject obj, bool value) => obj.SetValue(IsUpdatingProperty, value);

	private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not PasswordBox passwordBox) return;

		// ViewModel -> PasswordBox の同期（フラグで再帰回避）
		if (GetIsUpdating(passwordBox)) return;

		var newPassword = (string?)e.NewValue ?? string.Empty;
		if (passwordBox.Password == newPassword) return;

		SetIsUpdating(passwordBox, true);
		passwordBox.Password = newPassword;
		SetIsUpdating(passwordBox, false);
	}

	private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not PasswordBox passwordBox) return;

		if ((bool)e.NewValue) passwordBox.PasswordChanged += HandlePasswordChanged;
		else passwordBox.PasswordChanged -= HandlePasswordChanged;
	}

	private static void HandlePasswordChanged(object sender, RoutedEventArgs e) {
		if (sender is not PasswordBox passwordBox) return;

		// PasswordBox -> ViewModel の同期（フラグで再帰回避）
		if (GetIsUpdating(passwordBox)) return;

		SetIsUpdating(passwordBox, true);
		SetBoundPassword(passwordBox, passwordBox.Password);
		SetIsUpdating(passwordBox, false);
	}
}
