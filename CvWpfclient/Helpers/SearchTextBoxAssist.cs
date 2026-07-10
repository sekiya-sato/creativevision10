/*
# description
SearchTextBoxAssist は SearchTextBox 風 TextBox テンプレートへ検索コマンドとボタン背景色を設定する添付プロパティです。

# example
<TextBox helpers:SearchTextBoxAssist.Command="{Binding SearchCommand}" />
 */
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

/// <summary>
/// SearchTextBox 風の TextBox テンプレートに Command / ButtonBackground を付与する添付プロパティ。
/// </summary>
public static class SearchTextBoxAssist {
	public static readonly DependencyProperty CommandProperty =
		DependencyProperty.RegisterAttached(
			"Command",
			typeof(ICommand),
			typeof(SearchTextBoxAssist),
			new PropertyMetadata(default(ICommand)));

	public static readonly DependencyProperty ButtonBackgroundProperty =
		DependencyProperty.RegisterAttached(
			"ButtonBackground",
			typeof(Brush),
			typeof(SearchTextBoxAssist),
			new PropertyMetadata(default(Brush)));

	public static ICommand? GetCommand(DependencyObject obj) =>
		(ICommand?)obj.GetValue(CommandProperty);

	public static void SetCommand(DependencyObject obj, ICommand? value) =>
		obj.SetValue(CommandProperty, value);

	public static Brush? GetButtonBackground(DependencyObject obj) =>
		(Brush?)obj.GetValue(ButtonBackgroundProperty);

	public static void SetButtonBackground(DependencyObject obj, Brush? value) =>
		obj.SetValue(ButtonBackgroundProperty, value);
}
