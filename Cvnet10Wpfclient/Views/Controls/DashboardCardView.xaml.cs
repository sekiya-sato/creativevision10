using System.Windows;
using System.Windows.Controls;

namespace Cvnet10Wpfclient.Views.Controls;

public partial class DashboardCardView : UserControl {
	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
		nameof(Title),
		typeof(string),
		typeof(DashboardCardView),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof(Value),
		typeof(string),
		typeof(DashboardCardView),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty CardContentProperty = DependencyProperty.Register(
		nameof(CardContent),
		typeof(object),
		typeof(DashboardCardView),
		new PropertyMetadata(null));

	public DashboardCardView() {
		InitializeComponent();
	}

	/// <summary>
	/// カードのタイトル。
	/// </summary>
	public string Title {
		get => (string)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	/// <summary>
	/// カードの値。
	/// </summary>
	public string Value {
		get => (string)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>
	/// カード内に配置する追加コンテンツ。
	/// </summary>
	public object? CardContent {
		get => GetValue(CardContentProperty);
		set => SetValue(CardContentProperty, value);
	}
}
