using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Qiye;

internal static class SiteIconStore
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.Ordinal);
    private static readonly Regex SafeId = new("^[a-z0-9-]{1,80}$", RegexOptions.Compiled);

    private static string PathFor(string siteId)
    {
        if (!SafeId.IsMatch(siteId)) throw new ArgumentException("无效的网站标识。", nameof(siteId));
        return Path.Combine(App.DataDirectory, "Icons", siteId + ".png");
    }

    public static BitmapSource? Load(string siteId)
    {
        if (!SafeId.IsMatch(siteId)) return null;
        if (Cache.TryGetValue(siteId, out var cached)) return cached;
        var path = PathFor(siteId);
        if (!File.Exists(path)) return null;
        try
        {
            using var input = File.OpenRead(path);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            Cache[siteId] = frame;
            return frame;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileFormatException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            App.Log(ex);
            return null;
        }
    }

    public static void Import(string siteId, string sourcePath)
    {
        var destination = PathFor(siteId);
        var info = new FileInfo(sourcePath);
        if (info.Length > 10_000_000) throw new InvalidDataException("图片不能超过 10 MB。");
        using var input = File.OpenRead(sourcePath);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > 4096 || frame.PixelHeight > 4096)
            throw new InvalidDataException("图片尺寸需在 1–4096 像素之间。");
        double scale = Math.Min(1, 128.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
        var resized = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) encoder.Save(output);
            File.Move(temporary, destination, true);
            Cache.Remove(siteId);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool HasCustom(string siteId) => SafeId.IsMatch(siteId) && File.Exists(PathFor(siteId));

    public static void Reset(string siteId)
    {
        var path = PathFor(siteId);
        if (File.Exists(path)) File.Delete(path);
        Cache.Remove(siteId);
    }
}
