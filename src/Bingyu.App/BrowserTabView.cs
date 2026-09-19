using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Threading;

namespace Bingyu;

internal sealed class BrowserTabView : Grid, IDisposable
{
    private readonly MainWindow _owner;
    private readonly BrowserService _service;
    private readonly Grid _body = new();
    private readonly TextBox _address;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly Button _sleep;
    private readonly ProgressBar _progress;
    private readonly Grid _toolbar;
    private WebView2? _browser;
    private Task? _initializing;
    private bool _disposed;
    private bool _suspended;
    public TabState Tab { get; }
    public WebView2? Browser => _browser;
    public bool Ready => _browser?.CoreWebView2 != null;
    public BrowserTabView(TabState tab, MainWindow owner, BrowserService service)
    {
        Tab = tab; _owner = owner; _service = service;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        RowDefinitions.Add(new RowDefinition());
        _toolbar = new Grid { Margin = new Thickness(5, 3, 5, 3) };
        var toolbar = _toolbar;
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _back = Ui.Button("\uE72B", "后退", () => { if (_browser?.CanGoBack == true) _browser.GoBack(); }, true);
        _forward = Ui.Button("\uE72A", "前进", () => { if (_browser?.CanGoForward == true) _browser.GoForward(); }, true);
        _reload = Ui.Button("\uE72C", "刷新当前页面", Reload, true);
        _back.IsEnabled = _forward.IsEnabled = false;
        left.Children.Add(_back); left.Children.Add(_forward); left.Children.Add(_reload); toolbar.Children.Add(left);
        _address = new TextBox { Text = tab.Url, FontSize = 11, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0), Margin = new Thickness(4, 0, 4, 0), MinWidth = 35, ToolTip = "输入网址后按 Enter；Ctrl+L 聚焦" };
        _address.SetResourceReference(BackgroundProperty, "Raised");
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Navigate(_address.Text); e.Handled = true; } };
        _address.GotKeyboardFocus += (_, _) => _address.SelectAll();
        Grid.SetColumn(_address, 1); toolbar.Children.Add(_address);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        _sleep = Ui.Button("\uE708", "休眠此页面（暂停网页活动，保留当前页面）", async () => await SleepAsync(), true);
        right.Children.Add(_sleep);
        right.Children.Add(Ui.Button("专注", "专注模式（F11）：隐藏此屏的浏览器工具栏", () => owner.ToggleZen(Tab)));
        right.Children.Add(Ui.Button("\uE740", "放大此屏 / 还原分屏", () => owner.ExpandTab(Tab.Id), true));
        var menu = new ContextMenu();
        void Item(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("新开一个相同工具", () => owner.Duplicate(Tab));
        Item("切换专注模式（F11）", () => owner.ToggleZen(Tab));
        Item("在默认浏览器打开", () => Ui.OpenExternal(Tab.Url));
        Item("复制网址", () => { try { Clipboard.SetText(Tab.Url); } catch (System.Runtime.InteropServices.COMException) { owner.SetStatus("剪贴板正忙，请重试。"); } });
        var zoomItems = new List<(MenuItem Item, double? Factor)>();
        void ZoomItem(string name, double? factor)
        {
            var item = new MenuItem { Header = name, IsCheckable = true };
            item.Click += (_, _) => SetZoom(factor);
            zoomItems.Add((item, factor)); menu.Items.Add(item);
        }
        ZoomItem("随分屏自动适配（Gemini 默认）", null);
        ZoomItem("固定 100%（拖动时不缩放）", 1);
        ZoomItem("固定 90%", .9);
        ZoomItem("固定 80%", .8);
        menu.Opened += (_, _) =>
        {
            foreach (var (item, factor) in zoomItems)
                item.IsChecked = factor == null ? Tab.ZoomFactor == null : Tab.ZoomFactor == factor;
        };
        var more = Ui.Button("\uE712", "页面菜单", () => { }, true);
        more.ContextMenu = menu;
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
        right.Children.Add(more); Grid.SetColumn(right, 2); toolbar.Children.Add(right);
        Children.Add(toolbar);
        _progress = new ProgressBar { IsIndeterminate = true, Height = 2, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed };
        _progress.SetResourceReference(ProgressBar.ForegroundProperty, "Accent");
        Grid.SetRow(_progress, 1); Children.Add(_progress);
        Grid.SetRow(_body, 2); Children.Add(_body);
        SizeChanged += (_, _) => ApplyZoom();
        PreviewMouseDown += (_, _) => owner.FocusTabPane(Tab.Id);
        SetZen(Tab.Zen);
        ShowMessage(Tab.Sleeping ? "页面已休眠" : "正在准备网页", Tab.Sleeping ? "登录状态和页面都在，点击即可继续。" : "第一次打开需要初始化系统网页组件。", Tab.Sleeping);
    }

    public void SetZen(bool enabled)
    {
        RowDefinitions[0].Height = new GridLength(enabled ? 0 : 43);
        RowDefinitions[1].Height = new GridLength(enabled ? 0 : 2);
        _toolbar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (enabled) _progress.Visibility = Visibility.Collapsed;
        ApplyZoom();
    }

    public Task EnsureLoadedAsync()
    {
        if (_disposed || Tab.Sleeping || Ready) return Task.CompletedTask;
        return _initializing ??= InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var env = await _service.EnvironmentAsync();
            if (_disposed) return;
            _browser = new WebView2 { DefaultBackgroundColor = BrowserBackground() };
            // WebView2 handles its own GotFocus event and does not bubble native
            // webpage clicks through the parent WPF PreviewMouseDown handler.
            _browser.AddHandler(GotFocusEvent,
                new RoutedEventHandler((_, _) => _owner.FocusTabPane(Tab.Id)), true);
            _browser.GotKeyboardFocus += (_, _) => _owner.FocusTabPane(Tab.Id);
            _body.Children.Add(_browser);
            await _browser.EnsureCoreWebView2Async(env);
            if (_disposed) return;
            var core = _browser.CoreWebView2;
            _service.Configure(core, _owner);
            ApplyZoom();
            core.NavigationStarting += (_, _) => { if (!Tab.Zen) _progress.Visibility = Visibility.Visible; };
            core.NavigationCompleted += (_, e) =>
            {
                _progress.Visibility = Visibility.Collapsed;
                if (e.IsSuccess) ApplyZoom();
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    _browser.Visibility = Visibility.Hidden;
                    ShowMessage("网页暂时没有连上", $"{e.WebErrorStatus}\n请检查网络或代理，然后重试。", false, true);
                }
            };
            core.SourceChanged += (_, _) =>
            {
                if (UrlPolicy.TryNormalize(core.Source, out var url)) { Tab.Url = url; if (!_address.IsKeyboardFocused) _address.Text = url; _owner.ScheduleSave(); }
            };
            core.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.DocumentTitle)) { Tab.Title = core.DocumentTitle; _owner.RefreshTabs(); _owner.ScheduleSave(); }
            };
            core.HistoryChanged += (_, _) => { _back.IsEnabled = core.CanGoBack; _forward.IsEnabled = core.CanGoForward; };
            core.ProcessFailed += (_, e) =>
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    _browser.Visibility = Visibility.Hidden;
                    ShowMessage("页面需要重新载入", "网页进程暂时不可用，登录数据仍保留。", false, true);
                    _initializing = null;
                }
            };
            RemoveMessage();
            core.Navigate(Tab.Url);
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            App.Log(ex);
            _browser?.Dispose(); _browser = null; _body.Children.Clear();
            ShowMessage("网页组件未能启动", ex is WebView2RuntimeNotFoundException ? "需要 Microsoft Edge WebView2 Runtime。可从微软官网安装后重试。" : "可以重试，或在默认浏览器中打开。", false, true);
        }
        finally { _initializing = null; }
    }

    public void Navigate(string url)
    {
        if (!UrlPolicy.TryNormalize(url, out var safe)) { _owner.SetStatus("请输入有效的 http 或 https 网址。"); return; }
        Tab.Url = safe; _address.Text = safe;
        if (Tab.Sleeping) { _ = WakeAsync(); return; }
        RemoveMessage();
        if (Ready) { _browser!.Visibility = Visibility.Visible; _browser.CoreWebView2.Navigate(safe); }
        else _ = EnsureLoadedAsync();
    }
    public void Reload()
    {
        if (Tab.Sleeping) { _ = WakeAsync(); return; }
        // Recreate only this control after a crash; the shared profile retains authentication.
        if (_body.Children.OfType<Border>().Any() && Ready)
        { _body.Children.Remove(_browser!); _browser!.Dispose(); _browser = null; }
        if (Ready) { RemoveMessage(); _browser!.Visibility = Visibility.Visible; _browser.Reload(); }
        else _ = EnsureLoadedAsync();
    }
    public async Task SleepAsync()
    {
        if (Tab.Sleeping) { await WakeAsync(); return; }
        if (_initializing != null) { _owner.SetStatus("页面正在初始化，请稍后再休眠。"); return; }
        bool success = true;
        if (Ready)
        {
            _browser!.Visibility = Visibility.Hidden;
            try { success = await _browser.CoreWebView2.TrySuspendAsync(); }
            catch (Exception ex) { App.Log(ex); success = false; }
            if (!success) { _browser.Visibility = Visibility.Visible; _owner.SetStatus("页面暂时无法休眠，可能仍有音视频或其他活动。"); return; }
            _suspended = true;
        }
        Tab.Sleeping = true; _progress.Visibility = Visibility.Collapsed;
        ShowMessage("让这个页面休息一下", "网页活动已暂停。登录状态和当前页面都保留。", true);
        _owner.RefreshTabs(); _owner.ScheduleSave();
    }
    public async Task WakeAsync()
    {
        Tab.Sleeping = false;
        if (_suspended && Ready) { _browser!.CoreWebView2.Resume(); _suspended = false; }
        RemoveMessage();
        if (_browser != null) _browser.Visibility = Visibility.Visible;
        await EnsureLoadedAsync();
        _owner.RefreshTabs(); _owner.ScheduleSave();
    }
    public void FocusAddress()
    {
        if (Tab.Zen) _owner.ToggleZen(Tab);
        Dispatcher.BeginInvoke(() => { _address.Focus(); _address.SelectAll(); });
    }
    internal void SetZoom(double? zoom)
    {
        Tab.ZoomFactor = zoom;
        ApplyZoom(); _owner.ScheduleSave();
        _owner.SetStatus(zoom == null ? "当前页面随分屏自动适配" : $"当前页面固定缩放为 {zoom:P0} · 拖动分屏时不会自动缩放");
    }
    private void ApplyZoom()
    {
        if (!Ready || _browser == null) return;
        // Gemini's composer and footer need more CSS viewport space in narrow split panes.
        double fit = Tab.SiteId == "gemini"
            ? _body.ActualWidth < 550 || _body.ActualHeight < 480 ? .8
              : _body.ActualWidth < 820 || _body.ActualHeight < 660 ? .9 : 1
            : 1;
        double target = Tab.ZoomFactor ?? fit;
        if (Math.Abs(_browser.ZoomFactor - target) > .001) _browser.ZoomFactor = target;
    }
    private System.Drawing.Color BrowserBackground() => Tab.SiteId switch
    {
        "gemini" => Theme.IsDark ? System.Drawing.Color.FromArgb(32, 33, 36) : System.Drawing.Color.FromArgb(250, 249, 249),
        "chatgpt" => Theme.IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.FromArgb(252, 252, 252),
        _ => Theme.IsDark ? System.Drawing.Color.FromArgb(38, 40, 39) : System.Drawing.Color.White
    };
    public void UpdateTheme()
    {
        if (!Ready) return;
        _browser!.DefaultBackgroundColor = BrowserBackground();
        _browser.CoreWebView2.Profile.PreferredColorScheme = Theme.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }
    private void RemoveMessage() { foreach (var child in _body.Children.OfType<Border>().ToArray()) _body.Children.Remove(child); }
    private void ShowMessage(string title, string detail, bool wake, bool retry = false)
    {
        RemoveMessage();
        var stack = new StackPanel { MaxWidth = 380, Margin = new Thickness(22), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = Ui.Text(wake ? "☾" : retry ? "↻" : "◌", 40, "Accent"); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.Margin = new Thickness(0, 0, 0, 20); stack.Children.Add(glyph);
        var heading = Ui.Text(title, 20); heading.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(heading);
        var info = Ui.Text(detail, 12, "Muted"); info.TextWrapping = TextWrapping.Wrap; info.TextAlignment = TextAlignment.Center; info.Margin = new Thickness(0, 12, 0, 22); stack.Children.Add(info);
        if (wake || retry)
        {
            var button = Ui.Button(wake ? "唤醒页面" : "重新加载", wake ? "唤醒页面" : "重新加载", () => { if (wake) _ = WakeAsync(); else Reload(); });
            Ui.Background(button, "AccentSoft"); stack.Children.Add(button);
            if (retry) stack.Children.Add(Ui.Button("在浏览器中打开 ↗", "在默认浏览器中打开", () => Ui.OpenExternal(Tab.Url)));
        }
        var border = new Border { Child = stack }; border.SetResourceReference(Border.BackgroundProperty, "Surface");
        _body.Children.Add(border);
    }
    public void Dispose() { _disposed = true; _browser?.Dispose(); _browser = null; }
}
