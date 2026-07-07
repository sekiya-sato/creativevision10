using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

public class BaseWindow : Window {

	private const double DefaultMinWidth = 640;
	private const double DefaultMinHeight = 480;

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
			if (!TryExecuteExitCommand()) {
				Close();
				if (Owner is Window owner)
					owner.Activate();
			}
		}
		else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift)) {
			// Shift+Ctrl+C でクリップボードにコピー
			if (AppGlobal.DebugMode) {
				var name = GetType().FullName ?? "";
				if (string.IsNullOrEmpty(name))
					return;
				try { Clipboard.SetText(name); } catch { }
				e.Handled = true;
			}
		}
	}
	private bool TryExecuteViewModelCommand(string commandName) {
		var dc = DataContext;
		if (dc == null) return false;
		try {
			var prop = dc.GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
			if (prop?.GetValue(dc) is ICommand cmd && cmd.CanExecute(null)) {
				cmd.Execute(null);
				return true;
			}
		}
		catch {
			// Ignore: コマンドの取得や実行中に例外が発生した場合は無視
		}
		return false;
	}

	private bool TryExecuteExitCommand() {
		if (DataContext is IViewModelLifecycle lifecycle) {
			return lifecycle.TryExecuteExitCommand();
		}
		return TryExecuteViewModelCommand("ExitCommand");
	}

	private bool TryExecuteInitCommand() {
		if (DataContext is IViewModelLifecycle lifecycle) {
			return lifecycle.TryExecuteInitCommand();
		}
		return TryExecuteViewModelCommand("InitCommand");
	}

	/// <summary>
	/// DataContext に実行中の非同期コマンド（IAsyncRelayCommand.IsRunning == true）があるか判定
	/// </summary>
	private bool HasRunningCommand() {
		var dc = DataContext;
		if (dc == null) return false;
		if (dc is IViewModelLifecycle lifecycle) return lifecycle.HasRunningCommand;

		foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (prop.GetValue(dc) is IAsyncRelayCommand cmd && cmd.IsRunning)
				return true;
		}
		return false;
	}

	protected override void OnContentRendered(EventArgs e) {
		base.OnContentRendered(e);

		// デザイン時は実行しない
		if (DesignerProperties.GetIsInDesignMode(this))
			return;

		ApplyDefaultMinimumSize();
		EnsureWithinDisplayBounds();

		TryExecuteInitCommand();
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
		if (dc is IViewModelLifecycle lifecycle) {
			lifecycle.CancelRunningCommands();
			return;
		}

		foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (!prop.Name.EndsWith("CancelCommand")) continue;
			if (prop.GetValue(dc) is ICommand cmd && cmd.CanExecute(null)) {
				cmd.Execute(null);
			}
		}
	}

	private void ApplyDefaultMinimumSize() {
		var defaultMinWidth = GetDefaultMinimumSize(DefaultMinWidth, Width, ActualWidth);
		if (MinWidth < defaultMinWidth)
			MinWidth = defaultMinWidth;

		var defaultMinHeight = GetDefaultMinimumSize(DefaultMinHeight, Height, ActualHeight);
		if (MinHeight < defaultMinHeight)
			MinHeight = defaultMinHeight;
	}

	private static double GetDefaultMinimumSize(double defaultSize, double configuredSize, double actualSize) {
		var currentSize = !double.IsNaN(configuredSize) && configuredSize > 0 ? configuredSize : actualSize;
		if (currentSize > 0)
			return Math.Min(defaultSize, currentSize);
		return defaultSize;
	}

	private void EnsureWithinDisplayBounds() {
		if (WindowState != WindowState.Normal)
			return;

		var handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
			return;

		var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
		if (monitor == IntPtr.Zero)
			return;

		var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
		if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
			return;

		var dpi = VisualTreeHelper.GetDpi(this);
		if (dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0)
			return;

		var workArea = new Rect(
			monitorInfo.rcWork.left / dpi.DpiScaleX,
			monitorInfo.rcWork.top / dpi.DpiScaleY,
			(monitorInfo.rcWork.right - monitorInfo.rcWork.left) / dpi.DpiScaleX,
			(monitorInfo.rcWork.bottom - monitorInfo.rcWork.top) / dpi.DpiScaleY);

		if (Left < workArea.Left)
			Left = workArea.Left;
		if (Top < workArea.Top)
			Top = workArea.Top;

		if (Width > workArea.Width)
			Width = workArea.Width;
		if (Height > workArea.Height)
			Height = workArea.Height;

		if (Left + Width > workArea.Left + workArea.Width)
			Left = workArea.Left + workArea.Width - Width;
		if (Top + Height > workArea.Top + workArea.Height)
			Top = workArea.Top + workArea.Height - Height;
	}
}

internal static class NativeMethods {
	public const uint MONITOR_DEFAULTTONEAREST = 2;

	[DllImport("user32.dll", SetLastError = false)]
	public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

	[StructLayout(LayoutKind.Sequential)]
	public struct MONITORINFO {
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT {
		public int left;
		public int top;
		public int right;
		public int bottom;
	}
}
