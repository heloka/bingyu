using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Threading;

namespace Bingyu;

internal sealed class BrowserService
{
    private Task<CoreWebView2Environment>? _environment;
    private readonly List<Window> _popups = [];
    public async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDirectory, "WebView2"));
        try { return await _environment; }
        catch { _environment = null; throw; }
    }

    public void Configure(CoreWebView2 core, Window owner)
    {
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Profile.PreferredColorScheme = Theme.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        core.NavigationStarting += (sender, e) =>
        {
            // Never launch arbitrary local protocol handlers from untrusted web content.
            if (e.Uri != "about:blank" && !UrlPolicy.TryNormalize(e.Uri, out _)) e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => OpenPopup(e, owner);
        core.PermissionRequested += async (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
            {
                e.State = CoreWebView2PermissionState.Allow;
                e.SavesInProfile = true;
                return;
            }
            using var deferral = e.GetDeferral();
            try
            {
                var answer = await owner.Dispatcher.InvokeAsync(() => MessageBox.Show(owner,
                    $"{e.Uri}\n\n请求使用：{PermissionName(e.PermissionKind)}\n\n是否允许这个网站使用？", "网站权限", MessageBoxButton.YesNo, MessageBoxImage.Question), DispatcherPriority.Normal);
                e.State = answer == MessageBoxResult.Yes ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                e.SavesInProfile = true;
            }
            catch { e.State = CoreWebView2PermissionState.Deny; }
        };
    }

    private static string PermissionName(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => "麦克风",
        CoreWebView2PermissionKind.Camera => "摄像头",
        CoreWebView2PermissionKind.ClipboardRead => "读取剪贴板",
        CoreWebView2PermissionKind.Geolocation => "位置",
        CoreWebView2PermissionKind.Notifications => "桌面通知",
        _ => kind.ToString()
    };

    private async void OpenPopup(CoreWebView2NewWindowRequestedEventArgs e, Window owner)
    {
        using var deferral = e.GetDeferral();
        e.Handled = true;
        if (e.Uri != "about:blank" && !UrlPolicy.TryNormalize(e.Uri, out _)) return;
        await Task.Yield();
        var popup = new Window { Title = "网页窗口 · 并语", Width = 580, Height = 760, MinWidth = 420, MinHeight = 480, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel();
        var address = new TextBox { Text = e.Uri, IsReadOnly = true, Margin = new Thickness(10) };
        DockPanel.SetDock(address, Dock.Top); panel.Children.Add(address);
        var browser = new WebView2(); panel.Children.Add(browser); popup.Content = panel;
        _popups.Add(popup);
        bool closed = false;
        popup.Closed += (_, _) => { closed = true; _popups.Remove(popup); browser.Dispose(); };
        try
        {
            popup.Show();
            await browser.EnsureCoreWebView2Async(await EnvironmentAsync());
            if (closed) return;
            Configure(browser.CoreWebView2, popup);
            browser.CoreWebView2.SourceChanged += (_, _) => address.Text = browser.CoreWebView2.Source;
            browser.CoreWebView2.DocumentTitleChanged += (_, _) => popup.Title = browser.CoreWebView2.DocumentTitle + " · 并语";
            browser.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
            e.NewWindow = browser.CoreWebView2;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!closed) { popup.Close(); MessageBox.Show(owner, "网页窗口未能打开，请重试。", "并语"); }
        }
    }
    public void HidePopups() { foreach (var p in _popups) p.Hide(); }
    public void ShowPopups() { foreach (var p in _popups) p.Show(); }
    public void ClosePopups() { foreach (var p in _popups.ToArray()) p.Close(); }
}
