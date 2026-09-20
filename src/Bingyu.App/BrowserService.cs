using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
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
        var openLinkItem = core.Environment.CreateContextMenuItem(
            "在默认浏览器中打开链接", null!, CoreWebView2ContextMenuItemKind.Command);
        string? contextLink = null;
        openLinkItem.CustomItemSelected += (_, _) =>
        {
            string? url = contextLink;
            if (url != null) owner.Dispatcher.BeginInvoke(() => Ui.OpenExternal(url));
        };
        core.ContextMenuRequested += (_, e) =>
        {
            contextLink = null;
            if (!e.ContextMenuTarget.HasLinkUri ||
                !UrlPolicy.TryNormalize(e.ContextMenuTarget.LinkUri, out var link)) return;
            contextLink = link;
            e.MenuItems.Insert(0, openLinkItem);
        };
        core.DownloadStarting += (_, e) => SaveDownload(e, owner);
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

    private static void SaveDownload(CoreWebView2DownloadStartingEventArgs e, Window owner)
    {
        try
        {
            string name = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(name)) name = "下载文件";
            string path;
            if (App.SmokeTest)
            {
                string directory = Path.Combine(App.DataDirectory, "smoke-downloads");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, name);
            }
            else
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string? suggestedDirectory = Path.GetDirectoryName(e.ResultFilePath);
                var dialog = new SaveFileDialog
                {
                    Title = "保存网页下载 · 并语",
                    FileName = name,
                    InitialDirectory = suggestedDirectory != null && Directory.Exists(suggestedDirectory) ? suggestedDirectory : downloads,
                    Filter = "所有文件 (*.*)|*.*",
                    AddExtension = false,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog(owner) != true) { e.Cancel = true; return; }
                path = dialog.FileName;
            }
            e.ResultFilePath = path;
            e.Handled = true;
            var mainWindow = owner as MainWindow ?? owner.Owner as MainWindow;
            mainWindow?.SetStatus("正在下载：" + name);
            var download = e.DownloadOperation;
            download.StateChanged += (_, _) =>
            {
                if (owner.Dispatcher.HasShutdownStarted) return;
                owner.Dispatcher.BeginInvoke(() =>
                {
                    if (download.State == CoreWebView2DownloadState.Completed)
                        mainWindow?.SetStatus("已保存下载：" + Path.GetFileName(download.ResultFilePath));
                    else if (download.State == CoreWebView2DownloadState.Interrupted &&
                             download.InterruptReason != CoreWebView2DownloadInterruptReason.UserCanceled)
                        mainWindow?.SetStatus("下载未完成：" + download.InterruptReason);
                });
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            e.Cancel = true;
            App.Log(ex);
            if (!App.SmokeTest) MessageBox.Show(owner, "无法保存下载文件，请检查保存位置后重试。", "并语");
        }
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
