using System.Windows;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Views;

public partial class ZzMainMenuView : Window {
    public ZzMainMenuView() {
        InitializeComponent();
	}

	private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed) {
            DragMove();
        }
    }

}
