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

	public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
	public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

	public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);
	public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

	private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is PasswordBox passwordBox) {
			// ViewModelからの変更をPasswordBoxに反映（無限ループ防止のためイベントを一時解除）
			passwordBox.PasswordChanged -= HandlePasswordChanged;
			if ((string)e.NewValue != passwordBox.Password) {
				passwordBox.Password = (string)e.NewValue ?? string.Empty;
			}
			passwordBox.PasswordChanged += HandlePasswordChanged;
		}
	}

	private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is PasswordBox passwordBox) {
			if ((bool)e.NewValue) passwordBox.PasswordChanged += HandlePasswordChanged;
			else passwordBox.PasswordChanged -= HandlePasswordChanged;
		}
	}

	private static void HandlePasswordChanged(object sender, RoutedEventArgs e) {
		PasswordBox passwordBox = (PasswordBox)sender;
		// PasswordBoxの入力を添付プロパティに反映 -> ViewModelへ
		SetBoundPassword(passwordBox, passwordBox.Password);
	}
}
