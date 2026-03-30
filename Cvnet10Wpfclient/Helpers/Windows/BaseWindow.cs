using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

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

			// 非同期コマンドが実行中の場合は確認ダイアログを表示
			if (HasRunningCommand()) {
				var result = MessageEx.ShowQuestionDialog("処理を実行中です。\nメインメニューに戻りますか？", owner: this);
				if (result != MessageBoxResult.Yes)
					return;
			}

			Close();
			if (Owner is Window owner)
				owner.Activate();
		}
	}

	/// <summary>
	/// DataContext に実行中の非同期コマンド（IAsyncRelayCommand.IsRunning == true）があるか判定
	/// </summary>
	private bool HasRunningCommand() {
		var dc = DataContext;
		if (dc == null) return false;

		foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (prop.GetValue(dc) is IAsyncRelayCommand cmd && cmd.IsRunning)
				return true;
		}
		return false;
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
