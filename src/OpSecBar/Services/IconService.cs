using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Estrae icone ad alta risoluzione dalla shell (eseguibili, collegamenti, app UWP in shell:AppsFolder).
/// Lavora su un thread STA dedicato e mantiene una cache in memoria.
/// </summary>
public static class IconService
{
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Guid ImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private static Thread? _worker;

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    public static Task<ImageSource?> GetAsync(string key, int size = 48)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<ImageSource?>(null);
        return Cache.GetOrAdd($"{key}|{size}", _ =>
        {
            EnsureWorker();
            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() => tcs.SetResult(Load(key, size)));
            return tcs.Task;
        });
    }

    private static void EnsureWorker()
    {
        if (_worker != null) return;
        lock (Queue)
        {
            if (_worker != null) return;
            _worker = new Thread(() =>
            {
                foreach (Action job in Queue.GetConsumingEnumerable())
                {
                    try { job(); } catch (Exception ex) { Log.Error("IconService", ex); }
                }
            }) { IsBackground = true, Name = "OpSecBar.Icons" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
    }

    private static ImageSource? Load(string key, int size)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            Guid iid = ImageFactoryGuid;
            int hr = Native.SHCreateItemFromParsingName(key, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory == null) return null;

            hr = factory.GetImage(new Native.SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hBitmap);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(factory);
            if (hr != 0 || hBitmap == IntPtr.Zero) return null;

            BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex)
        {
            Log.Error($"Icona non caricabile: {key}", ex);
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
        }
    }
}
