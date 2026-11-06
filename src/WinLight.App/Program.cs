using System.IO.Pipes;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WinLight.App;

internal static class Program
{
    private const string MutexName = @"Local\WinLight.Singleton";
    internal const string PipeName = "WinLight.Show";

    [STAThread]
    private static void Main()
    {
        try
        {
            using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                SignalFirstInstance();
                return;
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new App();
            });
        }
        catch (Exception error)
        {
            AppLog.Error("Application startup", error);
        }
    }

    private static void SignalFirstInstance()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(500);
                pipe.WriteByte(1);
                return;
            }
            catch (Exception error) when (error is IOException or TimeoutException)
            {
                lastError = error;
                Thread.Sleep(100);
            }
        }

        if (lastError is not null)
            AppLog.Error("Signal first instance", lastError);
    }
}
