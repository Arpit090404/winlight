using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinLight.App.Native;

// Broadcasts do not reach HWND_MESSAGE, so this separate hidden top-level HWND listens for shell/session events.
internal sealed unsafe class SystemEventWindow : IDisposable
{
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmSessionChange = 0x02B1;
    private const uint PowerResumeSuspend = 0x0007;
    private const uint PowerResumeAutomatic = 0x0012;
    private const uint SessionConsoleConnect = 0x0001;
    private const uint SessionRemoteConnect = 0x0003;
    private const uint SessionUnlock = 0x0008;
    private static SystemEventWindow? s_instance;
    private readonly WNDPROC _windowProc;
    private readonly Action _taskbarCreated;
    private readonly Action<string> _sessionResumed;
    private readonly uint _taskbarCreatedMessage;
    private HWND _handle;
    private bool _sessionRegistered;
    private bool _testHotkeyHeld;
    private bool _testReplacementHeld;

    public SystemEventWindow(Action taskbarCreated, Action<string> sessionResumed)
    {
        _taskbarCreated = taskbarCreated;
        _sessionResumed = sessionResumed;
        s_instance = this;
        _windowProc = WindowProc;
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        fixed (char* name = "WinLight.SystemEvents")
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
            _handle = PInvoke.CreateWindowEx(0, "WinLight.SystemEvents", "", 0,
                0, 0, 0, 0, default, default, null, null);
            if (_handle.IsNull) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _sessionRegistered = PInvoke.WTSRegisterSessionNotification(_handle, 0);
        if (!_sessionRegistered) AppLog.Error("Register session notifications", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    internal bool HoldDefaultHotkeyForTest()
    {
        _testHotkeyHeld = PInvoke.RegisterHotKey(_handle, 900,
            Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_ALT, 0x20);
        return _testHotkeyHeld;
    }

    internal bool HoldReplacementHotkeyForTest()
    {
        _testReplacementHeld = PInvoke.RegisterHotKey(_handle, 901,
            Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_CONTROL |
            Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_ALT, 0x4B);
        return _testReplacementHeld;
    }

    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        SystemEventWindow? current = s_instance;
        if (current is not null)
        {
            try
            {
                if (message == current._taskbarCreatedMessage) current._taskbarCreated();
                else if (message == WmPowerBroadcast &&
                    (wParam.Value == PowerResumeSuspend || wParam.Value == PowerResumeAutomatic))
                    current._sessionResumed(wParam.Value == PowerResumeSuspend ? "power_resume_suspend" : "power_resume_automatic");
                else if (message == WmSessionChange &&
                    (wParam.Value == SessionConsoleConnect || wParam.Value == SessionRemoteConnect ||
                     wParam.Value == SessionUnlock)) current._sessionResumed(
                        wParam.Value == SessionUnlock ? "session_unlock" :
                        wParam.Value == SessionConsoleConnect ? "console_connect" : "remote_connect");
            }
            catch (Exception error) { AppLog.Error("System event callback", error); }
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_testHotkeyHeld) PInvoke.UnregisterHotKey(_handle, 900);
        if (_testReplacementHeld) PInvoke.UnregisterHotKey(_handle, 901);
        if (_sessionRegistered) PInvoke.WTSUnRegisterSessionNotification(_handle);
        if (!_handle.IsNull) PInvoke.DestroyWindow(_handle);
        _handle = default;
        s_instance = null;
    }
}
