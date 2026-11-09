using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using WinLight.Core;

namespace WinLight.App.Native;

internal static unsafe class AppLauncher
{
    public static void OpenFile(string path, bool isDirectory, bool asAdmin)
    {
        if (isDirectory ? !Directory.Exists(path) : !File.Exists(path))
            throw new FileNotFoundException("The indexed item no longer exists.", path);
        string target = isDirectory ? "explorer.exe" : path;
        string? parameters = isDirectory ? $"\"{path}\"" : null;
        fixed (char* file = target)
        fixed (char* args = parameters)
        fixed (char* verb = asAdmin ? "runas" : "open")
        {
            var execute = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                lpFile = file,
                lpParameters = args,
                lpVerb = verb,
                nShow = 1
            };
            if (!PInvoke.ShellExecuteEx(ref execute))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void Launch(AppItem item, bool asAdmin)
    {
        if (item.HasMissingSnapshotTarget())
            throw new FileNotFoundException("The indexed app target no longer exists.",
                item.TargetPath ?? item.LaunchPath);
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
