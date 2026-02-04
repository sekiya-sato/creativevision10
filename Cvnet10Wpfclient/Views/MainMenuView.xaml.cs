using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Wpfclient.Models;
using System.Windows;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Views;

public partial class MainMenuView : Window {
    public MainMenuView() {
        InitializeComponent();
	}

	private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed) {
            DragMove();
        }
    }

}
