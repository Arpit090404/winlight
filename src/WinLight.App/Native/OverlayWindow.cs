using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinLight.App.Native;

internal static unsafe class OverlayWindow
{
    public static void Configure(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        // The native tool-window style below also hides this surface; some desktop
        // sessions reject the WinAppSDK switcher setter during startup.
        try { window.AppWindow.IsShownInSwitchers = false; }
        catch (NotImplementedException error) { AppLog.Error("Hide launcher from task switcher", error); }
        HWND hwnd = new(WinRT.Interop.WindowNative.GetWindowHandle(window));
        nint style = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~(nint)0x00C40000; // WS_CAPTION | WS_THICKFRAME
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);
        nint extended = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        extended = (extended | (nint)0x00000080) & ~(nint)0x00040000; // TOOLWINDOW, no APPWINDOW
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, extended);
        PInvoke.SetWindowPos(hwnd, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        int preference = 2; // DWMWCP_ROUND
        int result = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(int));
        if (result < 0) AppLog.Error("Set rounded corners", Marshal.GetExceptionForHR(result) ?? new Win32Exception(result));
        int noBorder = unchecked((int)0xFFFFFFFE);
        result = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &noBorder, sizeof(int));
        if (result < 0) AppLog.Error("Remove DWM border", Marshal.GetExceptionForHR(result) ?? new Win32Exception(result));
        window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
    }

    public static void Place(Window window, int logicalHeight)
    {
        HWND hwnd = new(WinRT.Interop.WindowNative.GetWindowHandle(window));
        DisplayArea area;
        if (PInvoke.GetCursorPos(out var cursor))
            area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        else
        {
            AppLog.Error("Get cursor position", new Win32Exception(Marshal.GetLastWin32Error()));
            area = DisplayArea.Primary;
        }
        uint dpi = PInvoke.GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1 : dpi / 96.0;
        int width = (int)Math.Round(640 * scale);
        int height = (int)Math.Round(logicalHeight * scale);
        RectInt32 work = area.WorkArea;
        int x = work.X + (work.Width - width) / 2;
        int y = work.Y + work.Height / 5;
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    public static void RestoreBackdrop(Window window)
    {
        // Hiding the pre-created window can leave its backdrop disconnected.
        var backdrop = window.SystemBackdrop;
        window.SystemBackdrop = null;
        window.SystemBackdrop = backdrop;
    }

    public static void ResizeHeight(Window window, int logicalHeight)
    {
        HWND hwnd = new(WinRT.Interop.WindowNative.GetWindowHandle(window));
        uint dpi = PInvoke.GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1 : dpi / 96.0;
        int height = (int)Math.Round(logicalHeight * scale);
        window.AppWindow.Resize(new SizeInt32(window.AppWindow.Size.Width, height));
    }

    public static void BringToForeground(Window window)
    {
        HWND hwnd = new(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (!PInvoke.SetWindowPos(hwnd, new HWND(-1), 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE))
            AppLog.Error("Raise launcher above windows", new Win32Exception(Marshal.GetLastWin32Error()));

        HWND foreground = PInvoke.GetForegroundWindow();
        uint processId = 0;
        uint foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, &processId);
        uint currentThread = PInvoke.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            PInvoke.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            bool activated = PInvoke.SetForegroundWindow(hwnd);
            if (DiagnosticLog.Enabled)
                DiagnosticLog.Write($"SetForegroundWindow: {activated}, foreground: {(nint)PInvoke.GetForegroundWindow().Value}");
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
        }
        window.Activate();
    }
}
