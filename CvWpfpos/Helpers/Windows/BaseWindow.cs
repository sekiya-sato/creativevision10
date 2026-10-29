/*
# description
BaseWindow は ViewModel の初期化・終了処理、Esc による閉じる操作、最小サイズ、および表示位置の補正を提供する WPF Window 基底クラスです。
CvWpfclient/Helpers/Windows/BaseWindow.cs の POS 向け簡易版（メインメニュー/複数タブ伝票固有の処理は持たない）。

# example
public partial class PosUriageInputView : BaseWindow { }
 */
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CvWpfpos.Helpers;

public class BaseWindow : Window
{
    private const double DefaultMinWidth = 640;
    private const double DefaultMinHeight = 480;

    public BaseWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>
    /// 派生クラスでは必ず base.OnPreviewKeyDown(e); を呼ぶ(ESCを有効にしたい場合)
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (TryExecuteViewModelCommand("ExitCommand")) return;
        Close();
        (Owner as Window)?.Activate();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // デザイン時は実行しない
        if (DesignerProperties.GetIsInDesignMode(this)) return;

        ApplyDefaultMinimumSize();
        EnsureWithinDisplayBounds();

        TryExecuteViewModelCommand("InitCommand");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        CancelViewModelCommands();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IDisposable disposable) disposable.Dispose();
    }

    private bool TryExecuteViewModelCommand(string commandName)
    {
        var dc = DataContext;
        if (dc == null) return false;
        try
        {
            var prop = dc.GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
            if (prop?.GetValue(dc) is ICommand cmd && cmd.CanExecute(null))
            {
                cmd.Execute(null);
                return true;
            }
        }
        catch
        {
            // Ignore: コマンドの取得や実行中に例外が発生した場合は無視
        }
        return false;
    }

    private void CancelViewModelCommands()
    {
        var dc = DataContext;
        if (dc == null) return;

        foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.Name.EndsWith("CancelCommand")) continue;
            if (prop.GetValue(dc) is ICommand cmd && cmd.CanExecute(null)) cmd.Execute(null);
        }
    }

    /// <summary>
    /// DataContext に実行中の非同期コマンド（IAsyncRelayCommand.IsRunning == true）があるか判定
    /// </summary>
    protected bool HasRunningCommand()
    {
        var dc = DataContext;
        if (dc == null) return false;

        foreach (var prop in dc.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (prop.GetValue(dc) is IAsyncRelayCommand cmd && cmd.IsRunning) return true;
        }
        return false;
    }

    private void ApplyDefaultMinimumSize()
    {
        var defaultMinWidth = GetDefaultMinimumSize(DefaultMinWidth, Width, ActualWidth);
        if (MinWidth < defaultMinWidth) MinWidth = defaultMinWidth;

        var defaultMinHeight = GetDefaultMinimumSize(DefaultMinHeight, Height, ActualHeight);
        if (MinHeight < defaultMinHeight) MinHeight = defaultMinHeight;
    }

    private static double GetDefaultMinimumSize(double defaultSize, double configuredSize, double actualSize)
    {
        var currentSize = !double.IsNaN(configuredSize) && configuredSize > 0 ? configuredSize : actualSize;
        return currentSize > 0 ? Math.Min(defaultSize, currentSize) : defaultSize;
    }

    private void EnsureWithinDisplayBounds()
    {
        if (WindowState != WindowState.Normal) return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        if (dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0) return;

        var workArea = new Rect(
            monitorInfo.rcWork.left / dpi.DpiScaleX,
            monitorInfo.rcWork.top / dpi.DpiScaleY,
            (monitorInfo.rcWork.right - monitorInfo.rcWork.left) / dpi.DpiScaleX,
            (monitorInfo.rcWork.bottom - monitorInfo.rcWork.top) / dpi.DpiScaleY);

        if (Left < workArea.Left) Left = workArea.Left;
        if (Top < workArea.Top) Top = workArea.Top;

        if (Width > workArea.Width) Width = workArea.Width;
        if (Height > workArea.Height) Height = workArea.Height;

        if (Left + Width > workArea.Left + workArea.Width) Left = workArea.Left + workArea.Width - Width;
        if (Top + Height > workArea.Top + workArea.Height) Top = workArea.Top + workArea.Height - Height;
    }
}

internal static class NativeMethods
{
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
