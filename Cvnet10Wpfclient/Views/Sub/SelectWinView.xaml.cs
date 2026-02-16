using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Wpfclient.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace Cvnet10Wpfclient.Views.Sub;

/// <summary>
/// SelectWinView.xaml の相互作用ロジック
/// [Interaction logic for SelectWinView.xaml]
/// </summary>
public partial class SelectWinView : Helpers.BaseWindow {
	public SelectWinView() {
		InitializeComponent();
	}

	private void SelectGrid_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key == Key.Enter) {
			e.Handled = true; // Enterキーのデフォルト動作を無効にする [Disable the default behavior of the Enter key]
			DefaultButton?.Command?.Execute(null); // デフォルトボタンのコマンドを実行 [Execute the command of the default button]
        }
    }
	protected override void OnSourceInitialized(EventArgs e) {
		base.OnSourceInitialized(e);

		if (Owner is not Window owner) return;

		WindowStartupLocation = WindowStartupLocation.Manual;

		var ownerBounds = owner.WindowState == WindowState.Normal
			? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight)
			: owner.RestoreBounds;

		var width = double.IsNaN(Width) ? ActualWidth : Width;
		var height = double.IsNaN(Height) ? ActualHeight : Height;

		Left = ownerBounds.Left - 40; // 右上合わせなら: ownerBounds.Right - width
		Top = ownerBounds.Top - 20;
	}

	
}
