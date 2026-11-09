using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinLight.Core;

namespace WinLight.App.Native;

internal static class ResultActionExecutor
{
    public static async Task ExecuteAsync(ResultAction action, bool asAdmin)
    {
        switch (action)
        {
            case ResultAction.LaunchApp launch:
                AppLauncher.Launch(launch.Item, asAdmin || launch.RunAsAdmin);
                return;
            case ResultAction.OpenFile file:
                AppLauncher.OpenFile(file.Path, file.IsDirectory, asAdmin);
                return;
            case ResultAction.CopyText copy:
                var content = new DataPackage();
                content.SetText(copy.Text);
                for (int attempt = 0; ; attempt++)
                {
                    try { Clipboard.SetContent(content); return; }
                    catch (COMException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 2)
                    {
                        await Task.Delay(40);
                    }
                }
            default:
                throw new NotSupportedException($"Unsupported result action: {action.GetType().Name}");
        }
    }
}
