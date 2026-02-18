using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Cvnet10Wpfclient.Helpers {
	/// <summary>
	/// SearchTextBox.xaml の相互作用ロジック
	/// </summary>
	public partial class SearchTextBox : UserControl {
		public static readonly DependencyProperty TextProperty =
			DependencyProperty.Register(
				nameof(Text),
				typeof(string),
				typeof(SearchTextBox),
				new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

		public static readonly DependencyProperty CommandProperty =
			DependencyProperty.Register(
				nameof(Command),
				typeof(ICommand),
				typeof(SearchTextBox));

		public static readonly DependencyProperty ButtonBackgroundProperty =
		DependencyProperty.Register(
			nameof(ButtonBackground),
			typeof(Brush),
			typeof(SearchTextBox));

		public static readonly DependencyProperty TextHorizontalContentAlignmentProperty =
			DependencyProperty.Register(
				nameof(TextHorizontalContentAlignment),
				typeof(HorizontalAlignment),
				typeof(SearchTextBox),
				new FrameworkPropertyMetadata(HorizontalAlignment.Left));

		public string Text {
			get => (string)GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		public ICommand? Command {
			get => (ICommand?)GetValue(CommandProperty);
			set => SetValue(CommandProperty, value);
		}

		public Brush? ButtonBackground {
			get => (Brush?)GetValue(ButtonBackgroundProperty);
			set => SetValue(ButtonBackgroundProperty, value);
		}

		public HorizontalAlignment TextHorizontalContentAlignment {
			get => (HorizontalAlignment)GetValue(TextHorizontalContentAlignmentProperty);
			set => SetValue(TextHorizontalContentAlignmentProperty, value);
		}

		public SearchTextBox() => InitializeComponent();
	}
}
