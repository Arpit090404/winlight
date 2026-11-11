using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using WinLight.Core;

namespace WinLight.App.Native;

internal enum ShellLaunchOutcome { ProcessCreated, ShellAccepted }

internal static unsafe class AppLauncher
{
    public static ShellLaunchOutcome OpenFile(string path, bool isDirectory, bool asAdmin)
    {
        if (isDirectory ? !Directory.Exists(path) : !File.Exists(path))
            throw new FileNotFoundException("The indexed item no longer exists.", path);
        string target = isDirectory ? "explorer.exe" : path;
        string? parameters = isDirectory ? $"\"{path}\"" : null;
        return Execute(target, parameters, asAdmin);
    }

    public static ShellLaunchOutcome Launch(AppItem item, bool asAdmin)
    {
        // A cached AppsFolder entry may have a temporarily unavailable target
        // while its shell identity remains valid. Let the shell resolve it.
        if (item.HasMissingSnapshotTarget())
            throw new FileNotFoundException("The indexed app shortcut no longer exists.");
        return Execute(item.LaunchPath, null, asAdmin);
    }

    private static ShellLaunchOutcome Execute(string target, string? parameters, bool asAdmin)
    {
        fixed (char* path = target)
        fixed (char* args = parameters)
        fixed (char* verb = asAdmin ? "runas" : null)
        {
            var execute = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                fMask = 0x00000440, // FLAG_NO_UI | NOCLOSEPROCESS
                lpFile = path,
                lpParameters = args,
                lpVerb = verb,
                nShow = 1
            };
            bool accepted = PInvoke.ShellExecuteEx(ref execute);
            int error = accepted ? 0 : Marshal.GetLastPInvokeError();
            if (!accepted && error == 0 && (nint)execute.hInstApp.Value is > 0 and <= 32)
                error = (int)execute.hInstApp.Value;
            try
            {
                if (!accepted) throw new Win32Exception(error);
                return execute.hProcess.IsNull ? ShellLaunchOutcome.ShellAccepted : ShellLaunchOutcome.ProcessCreated;
            }
            finally
            {
                if (!execute.hProcess.IsNull && !PInvoke.CloseHandle(execute.hProcess))
                    AppLog.Error("Close shell launch process handle", new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }
    }
}
