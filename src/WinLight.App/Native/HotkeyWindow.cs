using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WinLight.Core;

namespace WinLight.App.Native;

internal sealed unsafe class HotkeyWindow : IDisposable
{
    private const int HotkeyId = 1;
    private const uint WmHotkey = 0x0312;
    private static HotkeyWindow? s_instance;
    private readonly Action<long> _toggle;
    private HWND _handle;
    private readonly WNDPROC _windowProc;
    private bool _registered;
    private HotkeyBinding _binding = HotkeyBinding.Default;

    public HotkeyWindow(Action<long> toggle)
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

    public HotkeyBinding Binding => _binding;
    public bool IsRegistered => _registered;

    public bool Register(HotkeyBinding binding)
    {
        if (!binding.IsValid(out _)) return false;
        if (_registered) PInvoke.UnregisterHotKey(_handle, HotkeyId);
        _registered = PInvoke.RegisterHotKey(_handle, HotkeyId,
            ToNative(binding.Modifiers) | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)binding.KeyCode);
        if (_registered) _binding = binding;
        return _registered;
    }

    public bool TryChange(HotkeyBinding binding)
    {
        HotkeyBinding old = _binding;
        if (Register(binding)) return true;
        Register(old);
        return false;
    }

    public bool Restore() => Register(_binding);

    private static HOT_KEY_MODIFIERS ToNative(HotkeyModifiers modifiers)
    {
        HOT_KEY_MODIFIERS result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) result |= HOT_KEY_MODIFIERS.MOD_WIN;
        return result;
    }

    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WmHotkey && (int)wParam.Value == HotkeyId)
        {
            try
            {
                long stamp = PerfLog.Enabled ? Stopwatch.GetTimestamp() : 0;
                s_instance?._toggle(stamp);
            }
            catch (Exception error) { AppLog.Error("Hotkey callback", error); }
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
