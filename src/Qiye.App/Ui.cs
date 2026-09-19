using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace Qiye;

internal static class Ui
{
    private static readonly Dictionary<string, ImageSource> SiteImages = [];
    private static readonly Dictionary<string, string> BuiltInFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chatgpt"] = "chatgpt.ico", ["claude"] = "claude.ico", ["gemini"] = "gemini.png",
        ["deepseek"] = "deepseek.png", ["doubao"] = "doubao.png", ["qwen"] = "qwen.png",
        ["zhipu"] = "zhipu.png"
    };

    public static TextBlock Text(string text, double size = 13, string brush = "Text")
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
    public static Button Button(string text, string hint, Action action, bool icon = false)
    {
        var b = new Button { Content = text, ToolTip = hint };
        AutomationProperties.SetName(b, hint);
        if (icon) { b.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); b.FontSize = 15; b.Padding = new Thickness(9); }
        b.Click += (_, _) => action();
        return b;
    }
    public static void Background(Control control, string key) => control.SetResourceReference(Control.BackgroundProperty, key);
    public static Border Badge(SiteDefinition site, double size = 42)
    {
        var icon = SiteGlyph(site, size * .68);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * .25), Background = Brushes.White, BorderBrush = Theme.Brush("#EEEEEB"), BorderThickness = new Thickness(1), Child = icon };
        return badge;
    }
    public static FrameworkElement SiteGlyph(SiteDefinition site, double size)
    {
        var custom = SiteIconStore.Load(site.Id);
        if (custom != null)
            return new Image { Source = custom, Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        if (BuiltInFiles.TryGetValue(site.Id, out var file))
        {
            try
            {
                if (!SiteImages.TryGetValue(file, out var source))
                {
                    var uri = new Uri("pack://application:,,,/Assets/Sites/" + file);
                    source = BitmapDecoder.Create(uri, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad)
                        .Frames.OrderByDescending(f => f.PixelWidth).First();
                    if (source.CanFreeze) source.Freeze();
                    SiteImages[file] = source;
                }
                return new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                App.Log(ex);
            }
        }
        var mark = Text(site.Mark, size * .78);
        mark.Foreground = Theme.Brush(site.Color);
        return mark;
    }
    public static Border Line() { var b = new Border { Height = 1 }; b.SetResourceReference(Border.BackgroundProperty, "Stroke"); return b; }
    public static void OpenExternal(string url)
    {
        if (!UrlPolicy.TryNormalize(url, out var safe)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(safe) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show("无法打开默认浏览器，请检查系统设置。", "并语"); }
    }
}
