// Cvnet10Wpfclient/Helpers/FocusHelper.cs
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Cvnet10Wpfclient.Helpers;

public static class FocusHelper {
	public static readonly DependencyProperty IsEnterToNextProperty =
		DependencyProperty.RegisterAttached("IsEnterToNext", typeof(bool), typeof(FocusHelper),
			new PropertyMetadata(false, OnIsEnterToNextChanged));

	public static bool GetIsEnterToNext(DependencyObject obj) => (bool)obj.GetValue(IsEnterToNextProperty);
	public static void SetIsEnterToNext(DependencyObject obj, bool value) => obj.SetValue(IsEnterToNextProperty, value);

	private static void OnIsEnterToNextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is UIElement element) {
			// PreviewKeyDown を使用してトンネリングフェーズでキャッチする
			if ((bool)e.NewValue)
				element.PreviewKeyDown += Element_PreviewKeyDown;
			else
				element.PreviewKeyDown -= Element_PreviewKeyDown;
		}
	}

	private static void Element_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key == Key.Enter) {
			var element = sender as UIElement;

			// TextBox で「Enter で改行」の設定になっている場合は遷移させない（汎用性のための考慮）
			if (element is TextBox tb && tb.AcceptsReturn) return;

			// 次のコントロールへフォーカス移動
			element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

			// 重要：ここでイベントを完了させることで、
			// Window の IsDefault ボタン（OKボタン）が反応するのを防ぐ
			e.Handled = true;
		}
	}
}