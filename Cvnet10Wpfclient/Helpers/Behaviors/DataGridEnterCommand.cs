using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Helpers;

public static class DataGridEnterCommand {
	public static readonly DependencyProperty TargetButtonProperty =
		DependencyProperty.RegisterAttached(
			"TargetButton",
			typeof(ButtonBase),
			typeof(DataGridEnterCommand),
			new PropertyMetadata(null, OnTargetButtonChanged));

	public static ButtonBase? GetTargetButton(DependencyObject obj) =>
		(ButtonBase?)obj.GetValue(TargetButtonProperty);

	public static void SetTargetButton(DependencyObject obj, ButtonBase? value) =>
		obj.SetValue(TargetButtonProperty, value);

	private static void OnTargetButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not UIElement element) return;

		if (e.OldValue is not null) {
			element.PreviewKeyDown -= Element_PreviewKeyDown;
		}

		if (e.NewValue is not null) {
			element.PreviewKeyDown += Element_PreviewKeyDown;
		}
	}

	private static void Element_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter || sender is not DependencyObject dependencyObject) return;

		var targetButton = GetTargetButton(dependencyObject);
		var command = targetButton?.Command;
		var parameter = targetButton?.CommandParameter;
		if (targetButton?.IsEnabled != true || command?.CanExecute(parameter) != true) return;

		e.Handled = true;
		command.Execute(parameter);
	}
}
