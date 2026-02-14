/*
# file name
BaseWindow.cs

# description
新規作成のViewに、CloseMessage登録とInitCommand()の実行処理を提供する

# example
View側:
<helpers:BaseWindow x:Class="Cvnet10Wpfclient.Views.NewTargetView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:behaviors="http://schemas.microsoft.com/xaml/behaviors"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:local="clr-namespace:Cvnet10Wpfclient.Views"
    xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:vm="clr-namespace:Cvnet10Wpfclient.ViewModels"
    xmlns:helpers="clr-namespace:Cvnet10Wpfclient.Helpers"
*/
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Cvnet10Wpfclient.Helpers;

public class BaseWindow : Window {
	public BaseWindow() {
		// ViewModel 側から Dialog を閉じるための共通メッセージ登録
		// 複数のウィンドウで使う場合には登録してある全てのWindowが反応する
		/*
		WeakReferenceMessenger.Default.Register<DialogCloseMessage>(this, (recipient, message) => {
			if (recipient is Window win) {
				// Show/ShowDialog の違いで DialogResult 設定が例外になる場合があるため安全に扱う
				try {
					win.DialogResult = message.DialogResult;
				}
				catch {
					// Ignore: 表示方式により DialogResult が設定できない場合がある
				}
				win.Close();
				win.Owner?.Activate();
			}
		});
		*/
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		UseLayoutRounding = true;
		SnapsToDevicePixels = true;
	}
	/// <summary>
	/// 派生クラスでは必ずbase.OnPreviewKeyDown(e);を呼ぶ(ESCを有効にしたい場合)
	/// </summary>
	/// <param name="e"></param>
	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		base.OnPreviewKeyDown(e);

		if (e.Key == Key.Escape) {
			e.Handled = true;
			Close();
			if(Owner is Window owner)
				owner.Activate();
		}
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);

		// ViewModel に ICommand プロパティ "InitCommand" があれば実行する（XAML でのトリガーを共通化）
		var dc = DataContext;
		if (dc == null) return;

		var prop = dc.GetType().GetProperty("InitCommand", BindingFlags.Instance | BindingFlags.Public);
		if (prop?.GetValue(dc) is ICommand cmd && cmd.CanExecute(null)) {
			cmd.Execute(null);
		}
	}
	protected override void OnClosing(CancelEventArgs e) {
		base.OnClosing(e);
		CancelViewModelCommands();
	}
	protected override void OnClosed(EventArgs e) {
		base.OnClosed(e);
		// メモリリーク防止のため登録解除
		WeakReferenceMessenger.Default.UnregisterAll(this);
	}
	private void CancelViewModelCommands() {
		var dc = DataContext;
		if (dc == null) return;

		foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (!prop.Name.EndsWith("CancelCommand")) continue;
			if (prop.GetValue(dc) is ICommand cmd && cmd.CanExecute(null)) {
				cmd.Execute(null);
			}
		}
	}
}