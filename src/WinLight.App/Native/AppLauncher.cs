using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using WinLight.Core;

namespace WinLight.App.Native;

internal static unsafe class AppLauncher
{
    public static void Launch(AppItem item, bool asAdmin)
    {
        fixed (char* path = item.LaunchPath)
        fixed (char* verb = asAdmin ? "runas" : "open")
        {
            var execute = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                lpFile = path,
                lpVerb = verb,
                nShow = 1
            };
            if (!PInvoke.ShellExecuteEx(ref execute))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
