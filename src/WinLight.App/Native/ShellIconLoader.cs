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
                using var image = CopyWithAlpha(bitmap);
                using var stream = new MemoryStream();
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
            finally { PInvoke.DeleteObject(new HGDIOBJ(bitmap.Value)); }
        }
        finally { Marshal.FinalReleaseComObject(factory); }
    }

    private static unsafe System.Drawing.Bitmap CopyWithAlpha(HBITMAP source)
    {
        const int size = 32;
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        byte[] pixels = new byte[size * size * 4];
        HDC dc = PInvoke.CreateCompatibleDC(default);
        if (dc.IsNull) throw new InvalidOperationException("Could not create icon device context.");
        try
        {
            fixed (byte* data = pixels)
            {
                if (PInvoke.GetDIBits(dc, source, 0, size, data, &info, DIB_USAGE.DIB_RGB_COLORS) == 0)
                    throw new InvalidOperationException("Could not read icon pixels.");
            }
        }
        finally { PInvoke.DeleteDC(dc); }

        var image = new System.Drawing.Bitmap(size, size, PixelFormat.Format32bppPArgb);
        var bounds = new System.Drawing.Rectangle(0, 0, size, size);
        BitmapData bits = image.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { image.UnlockBits(bits); }
        return image;
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
