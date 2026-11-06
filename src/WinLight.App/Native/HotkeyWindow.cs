using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinLight.App.Native;

internal sealed unsafe class HotkeyWindow : IDisposable
{
    private const int HotkeyId = 1;
    private const uint WmHotkey = 0x0312;
    private static HotkeyWindow? s_instance;
    private readonly Action _toggle;
    private HWND _handle;
    private readonly WNDPROC _windowProc;
    private bool _registered;

    public HotkeyWindow(Action toggle)
    {
        _toggle = toggle;
        s_instance = this;
        _windowProc = WindowProc;
        fixed (char* name = "WinLight.HotkeyWindow")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProc,
                hInstance = PInvoke.GetModuleHandle((char*)null),
                lpszClassName = name
            };
            if (PInvoke.RegisterClassEx(in windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _handle = PInvoke.CreateWindowEx(0, "WinLight.HotkeyWindow", "", 0, 0, 0, 0, 0,
                new HWND(-3), default, null, null);
            if (_handle.IsNull)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public bool RegisterAltSpace()
    {
        _registered = PInvoke.RegisterHotKey(_handle, HotkeyId,
            HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, 0x20);
        return _registered;
    }

    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WmHotkey && (int)wParam.Value == HotkeyId)
        {
            s_instance?._toggle();
            return default;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_registered) PInvoke.UnregisterHotKey(_handle, HotkeyId);
        if (!_handle.IsNull) PInvoke.DestroyWindow(_handle);
        _handle = default;
        s_instance = null;
    }
}
