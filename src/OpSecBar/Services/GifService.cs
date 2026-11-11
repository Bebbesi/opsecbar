using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Fotogrammi già composti di una GIF animata, ridotti alla dimensione in cui verranno mostrati.</summary>
public sealed class GifAnimation
{
    public required BitmapSource[] Frames { get; init; }
    public required int[] DelaysMs { get; init; }
    public double Aspect => Frames.Length > 0 && Frames[0].PixelHeight > 0 ? (double)Frames[0].PixelWidth / Frames[0].PixelHeight : 1;
}

/// <summary>
/// Gestione delle GIF dell'utente: copia nella cartella dei dati, decodifica in background con composizione
/// corretta dei fotogrammi (offset, trasparenza, metodi di "disposal") e cache condivisa tra le barre.
/// WPF da solo non anima le GIF: i fotogrammi pronti vengono poi fatti scorrere da <see cref="Controls.GifImage"/>.
/// </summary>
public static class GifService
{
    private const int MaxFrames = 400;
    private static readonly Dictionary<string, Task<GifAnimation?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string FullPath(GifItem item) => Path.Combine(AppPaths.GifsDir, item.File);

    /// <summary>Copia un file .gif nella cartella dell'app e restituisce la voce da salvare nelle impostazioni.</summary>
    public static GifItem? Import(string source)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(source), ".gif", StringComparison.OrdinalIgnoreCase) || !File.Exists(source)) return null;
            string name = Path.GetFileNameWithoutExtension(source);
            name = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            if (name.Length == 0) name = "gif";
            string target = Path.Combine(AppPaths.GifsDir, name + ".gif");
            for (int i = 2; File.Exists(target); i++) target = Path.Combine(AppPaths.GifsDir, $"{name} ({i}).gif");
            File.Copy(source, target);
            return new GifItem { File = Path.GetFileName(target) };
        }
        catch (Exception ex)
        {
            Log.Error("Importazione GIF", ex);
            return null;
        }
    }

    public static void Delete(GifItem item)
    {
        try { File.Delete(FullPath(item)); } catch (Exception ex) { Log.Warn("Eliminazione GIF: " + ex.Message); }
        lock (Cache)
            foreach (string key in Cache.Keys.Where(k => k.StartsWith(FullPath(item) + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                Cache.Remove(key);
    }

    /// <summary>Fotogrammi della GIF con altezza massima <paramref name="heightPx"/> (pixel fisici). Condivisi e congelati.</summary>
    public static Task<GifAnimation?> LoadAsync(string path, int heightPx)
    {
        string key = path + "|" + heightPx;
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var task))
            {
                task = Task.Run(() => Decode(path, heightPx));
                Cache[key] = task;
            }
            return task;
        }
    }

    private static GifAnimation? Decode(string path, int heightPx)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            int width = Meta<ushort>(decoder.Metadata, "/logscrdesc/Width", 0);
            int height = Meta<ushort>(decoder.Metadata, "/logscrdesc/Height", 0);
            if (width <= 0 || height <= 0)
            {
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }

            int stride = width * 4;
            byte[] canvas = new byte[stride * height];
            double scale = heightPx > 0 && height > heightPx ? (double)heightPx / height : 1;
            var frames = new List<BitmapSource>();
            var delays = new List<int>();

            foreach (BitmapFrame frame in decoder.Frames.Take(MaxFrames))
            {
                var meta = frame.Metadata as BitmapMetadata;
                int left = Meta<ushort>(meta, "/imgdesc/Left", 0);
                int top = Meta<ushort>(meta, "/imgdesc/Top", 0);
                int delay = Meta<ushort>(meta, "/grctlext/Delay", 10) * 10;
                int disposal = Meta<byte>(meta, "/grctlext/Disposal", 0);
                // Come i browser: ritardi quasi nulli diventano 100 ms.
                if (delay < 20) delay = 100;

                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int fw = converted.PixelWidth, fh = converted.PixelHeight;
                byte[] pixels = new byte[fw * fh * 4];
                converted.CopyPixels(pixels, fw * 4, 0);

                byte[]? previous = disposal == 3 ? (byte[])canvas.Clone() : null;

                for (int y = 0; y < fh; y++)
                {
                    int cy = top + y;
                    if (cy < 0 || cy >= height) continue;
                    for (int x = 0; x < fw; x++)
                    {
                        int cx = left + x;
                        if (cx < 0 || cx >= width) continue;
                        int s = (y * fw + x) * 4;
                        if (pixels[s + 3] == 0) continue; // pixel trasparente: resta quello sotto
                        Buffer.BlockCopy(pixels, s, canvas, cy * stride + cx * 4, 4);
                    }
                }

                BitmapSource full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, stride);
                BitmapSource output = scale < 1
                    ? new WriteableBitmap(new TransformedBitmap(full, new ScaleTransform(scale, scale)))
                    : full;
                output.Freeze();
                frames.Add(output);
                delays.Add(delay);

                if (disposal == 2)
                {
                    // Torna allo sfondo (trasparente) nell'area del fotogramma.
                    for (int y = Math.Max(0, top); y < Math.Min(height, top + fh); y++)
                        Array.Clear(canvas, y * stride + Math.Max(0, left) * 4, (Math.Min(width, left + fw) - Math.Max(0, left)) * 4);
                }
                else if (previous != null) canvas = previous;
            }

            return frames.Count == 0 ? null : new GifAnimation { Frames = frames.ToArray(), DelaysMs = delays.ToArray() };
        }
        catch (Exception ex)
        {
            Log.Error($"GIF non leggibile: {path}", ex);
            return null;
        }
    }

    private static T Meta<T>(BitmapMetadata? meta, string query, T fallback)
    {
        try
        {
            if (meta != null && meta.ContainsQuery(query) && meta.GetQuery(query) is T value) return value;
        }
        catch { }
        return fallback;
    }
}
