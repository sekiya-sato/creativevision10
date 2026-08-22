/*
# description
ClientLib は ViewModel からアクティブな Window の終了、ダイアログ表示、DataGrid 設定、カーソル状態、および URL 起動を行う共通ユーティリティです。

# example
ClientLib.Exit(this);
 */
using System.Diagnostics;
using System.Net.Mail;
using System.Windows;

namespace CvWpfclient.Helpers;
/// <summary>
/// 主にViewModel側からViewを操作するためのクラス
/// [Class mainly for manipulating the View from the ViewModel]
/// </summary>
public class ClientLib {
	/// <summary>
	/// アクティブなWindowを閉じる
	/// [Close the active Window]
	/// </summary>
	public static void Exit(object vm) {
		var win = GetActiveView(vm);
		if (win != null) {
			try {
				win.Close();
				if (win.Owner != null)
					win.Owner.Activate();
			}
			catch (InvalidOperationException) { }
		}
	}
	/// <summary>
	/// 自分と親以外全てのWindowを閉じる
	/// [Close all Windows except for the current and parent ones]
	/// </summary>
	public static void ExitAllWithoutMe(object vm) {
		var myview = GetActiveView(vm);
		var parent = myview?.Owner;
		foreach (var win in Application.Current.Windows.OfType<Window>()) {
			if (win != myview && win != parent)
				win.Close();
		}
	}
	/// <summary>
	/// ViewModelが紐づけられてるViewを取得する
	/// [Retrieve the View associated with the ViewModel]
	/// </summary>
	/// <returns></returns>
	public static Window? GetActiveView(object vm) {
		Window? myWin = null;
		var activeWins = Application.Current.Windows.OfType<Window>().Reverse();
		foreach (var ac in activeWins) {
			var myVm = ac.DataContext;
			if (myVm == vm)
				myWin = ac;
		}
		return myWin;
	}
	/// <summary>
	/// ViewのDialogResultを設定して閉じる
	/// [Set the DialogResult of the View and close it]
	/// </summary>
	/// <param name="result"></param>
	public static void ExitDialogResult(object vm, bool result) {
		var win = GetActiveView(vm);
		if (win != null) {
			try {
				win.DialogResult = result;
			}
			catch (InvalidOperationException) {
				/* ShowかShowDialogか自分でわかってない*/
				//[Whether to use Show or ShowDialog is not determined by this code]
				win.Close();
			}
		}
	}

	/// <summary>
	/// Viewを親として子Windowをオープンする
	/// [Open a child Window with the View as its parent]
	/// </summary>
	/// <param name="childWin">子Window</param> [Child Window]
	/// <param name="loc">表示位置</param> [Display position]
	/// <param name="IsDialog">true=ダイアログとして表示 false=独立Windowsとして表示</param> 
	/// [true = Display as a dialog, false = Display as an independent Window]
	/// <param name="IsShowTaskbar">true=タスクバーに表示 false=表示しない</param>
	/// [true = Display in the taskbar, false = Do not display]
	/// IsShowTaskbar
	public static bool? ShowDialogView(Window childWin, object? myVm, bool IsDialog = true) {
		if (myVm != null)
			childWin.Owner = GetActiveView(myVm);
		if (AppGlobal.DebugMode) {
			var name = $"{childWin.GetType().FullName}";
			childWin.ToolTip = name;
		}
		// childWin.WindowStartupLocation = loc;
		// childWin.ShowInTaskbar = IsShowTaskbar;
		if (IsDialog)
			return childWin.ShowDialog();
		else {
			childWin.Show();
			return null;
		}
	}
	/// <summary>
	/// 使用可能なデータフォルダを取得
	/// [Retrieve the available data folder]
	/// </summary>
	/// <returns>データフォルダ</returns> [Data folder]
	public static string GetDataDir() {
		try {
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); // AppData/Local
			string folder = System.IO.Path.Combine(appData, "CreativeVision10");
			if (!System.IO.Directory.Exists(folder)) {
				System.IO.Directory.CreateDirectory(folder);
			}
			return folder;
		}
		catch (Exception) {
			return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // AppData/Roaming
		}
	}
	/// <summary>
	/// マウスカーソルを待機状態にする
	/// </summary>
	public static void Cursor2Wait() {
		System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
	}
	/// <summary>
	///	マウスカーソルを標準に戻す
	/// </summary>
	public static void Cursor2Normal() {
		System.Windows.Input.Mouse.OverrideCursor = null;
	}
	public static bool IsCursorWait() {
		return System.Windows.Input.Mouse.OverrideCursor == System.Windows.Input.Cursors.Wait;
	}
	/// <summary>
	/// 指定したURLを既定のブラウザで開く
	/// </summary>
	/// <param name="url"></param>
	public static void OpenUrl(string url) {
		if (string.IsNullOrEmpty(url)) return;
		try {
			using var process = Process.Start(new ProcessStartInfo {
				FileName = url,
				UseShellExecute = true
			});
		}
		catch (Exception ex) {
			Debug.WriteLine(ex.Message);
		}
	}
	public static bool ValidateMail(string mail, Window?activeWindow, bool showSuccess = false) {
		if (string.IsNullOrWhiteSpace(mail)) {
			if (!showSuccess) return true;
			MessageEx.ShowWarningDialog("メールアドレスを入力してください。", owner: activeWindow);
			return false;
		}
		try {
			var address = new MailAddress(mail);
			if (!string.Equals(address.Address, mail, StringComparison.OrdinalIgnoreCase)) throw new FormatException();
			if (showSuccess) MessageEx.ShowInformationDialog("メールアドレスの形式は正しいです。", owner: activeWindow);
			return true;
		}
		catch (FormatException) {
			MessageEx.ShowWarningDialog("メールアドレスの形式が正しくありません。", owner: activeWindow);
			return false;
		}
	}
}

