using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WinLight.App.ViewModels;

namespace WinLight.App.Native;

internal sealed class ShellIconLoader : IDisposable
{
    private readonly BlockingCollection<AppResultViewModel> _queue = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Thread _worker;

    public ShellIconLoader(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _worker = new Thread(Work) { IsBackground = true, Name = "WinLight shell icons" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public void Queue(AppResultViewModel result)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(result);
    }

    private void Work()
    {
        foreach (AppResultViewModel result in _queue.GetConsumingEnumerable())
        {
            try
            {
                byte[]? png = ExtractPng(result.Item.LaunchPath);
                if (png is not null) _dispatcher.TryEnqueue(() => _ = SetIconAsync(result, png));
            }
            catch (Exception error) { AppLog.Error($"Extract icon for {result.Name}", error); }
        }
    }

    private static unsafe byte[]? ExtractPng(string parsingPath)
    {
        PInvoke.SHCreateItemFromParsingName<IShellItemImageFactory>(parsingPath, null, out var factory)
            .ThrowOnFailure();
        try
        {
            HBITMAP bitmap = default;
            factory.GetImage(new SIZE { cx = 32, cy = 32 }, SIIGBF.SIIGBF_ICONONLY, &bitmap);
            if (bitmap.IsNull) return null;
            try
            {
                using var image = System.Drawing.Image.FromHbitmap((nint)bitmap.Value);
                using var stream = new MemoryStream();
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
            finally { PInvoke.DeleteObject(new HGDIOBJ(bitmap.Value)); }
        }
        finally { Marshal.FinalReleaseComObject(factory); }
    }

    private static async Task SetIconAsync(AppResultViewModel result, byte[] png)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            result.Icon = bitmap;
        }
        catch (Exception error) { AppLog.Error($"Decode icon for {result.Name}", error); }
    }

    public void Dispose() => _queue.CompleteAdding();
}
