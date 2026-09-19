using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Bingyu;

internal sealed class MainWindow : Window
{
    private readonly StateStore _store;
    internal SavedState State { get; }
    internal Workspace Workspace => State.Workspace;
    private Preferences Prefs => State.Preferences;
    internal readonly BrowserService Browsers = new();
    internal readonly Dictionary<Guid, BrowserTabView> Views = [];
    private readonly StackPanel _sidebar = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _main = new();
    private readonly TextBlock _status = Ui.Text("", 11, "Muted");
    private readonly Dictionary<int, Button> _layoutButtons = [];
    private readonly Dictionary<int, Border> _paneBorders = [];
    private RowDefinition _titleRow = null!;
    private RowDefinition _tabRowDefinition = null!;
    private UIElement _titleBar = null!;
    private UIElement _tabBar = null!;
    private Button _topBarToggle = null!;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly HotkeyService _hotkey;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private bool _quitting;
    private bool _homeVisible;
    private bool _rendering;
    internal bool IsHome => _homeVisible;
    internal bool AreTopBarsHidden => Prefs.HideTopBars;
    private readonly UserPreferenceChangedEventHandler _themeChanged;

    public MainWindow(StateStore store, SavedState state)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        _store = store; State = state;
        Title = "并语 · AI 多屏工作台";
        Icon = LoadWindowIcon(32);
        Width = double.IsFinite(Prefs.Width) ? Math.Clamp(Prefs.Width, 920, SystemParameters.WorkArea.Width) : 1360;
        Height = double.IsFinite(Prefs.Height) ? Math.Clamp(Prefs.Height, 600, SystemParameters.WorkArea.Height) : 900;
        MinWidth = 920; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(10), GlassFrameThickness = new Thickness(0) });
        _hotkey = new HotkeyService(this, ToggleVisibility);
        _homeVisible = Workspace.Tabs.Count == 0;
        Content = BuildShell();
        BuildSidebar();
        RefreshTabs();
        SourceInitialized += (_, _) =>
        {
            Icon = LoadWindowIcon(32 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            WindowBounds.Attach(this);
            if (!App.SmokeTest && !_hotkey.Register(Prefs.Hotkey, out var error)) SetStatus(error + " 可在设置中修改。");
            if (!App.SmokeTest) CreateTray();
        };
        Loaded += (_, _) =>
        {
            if (Prefs.Maximized && !App.SmokeTest) WindowState = WindowState.Maximized;
            RenderWorkspace();
            if (store.RecoveryMessage != null) SetStatus(store.RecoveryMessage);
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsAppShortcut(key, Keyboard.Modifiers)) { e.Handled = true; HandleShortcut(key, Keyboard.Modifiers); }
        };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _status.Text = ""; };
        _themeChanged = (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Dispatcher.BeginInvoke(() => { if (Prefs.Theme == "System") ApplyTheme(); });
        };
        SystemEvents.UserPreferenceChanged += _themeChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= _themeChanged;
            _saveTimer.Stop(); _statusTimer.Stop();
            _hotkey.Dispose(); Browsers.ClosePopups();
            foreach (var view in Views.Values) view.Dispose();
            _tray?.Dispose(); _trayIcon?.Dispose();
        };
    }

    private static ImageSource LoadWindowIcon(double pixelSize)
    {
        var uri = new Uri("pack://application:,,,/Assets/Bingyu.ico");
        var frames = System.Windows.Media.Imaging.BitmapDecoder.Create(uri,
            System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames;
        return frames.OrderBy(frame => Math.Abs(frame.PixelWidth - pixelSize)).First();
    }

    private UIElement BuildShell()
    {
        var outer = new Border { BorderThickness = new Thickness(1) };
        outer.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var root = new Grid();
        _titleRow = new RowDefinition { Height = new GridLength(50) };
        root.RowDefinitions.Add(_titleRow);
        root.RowDefinitions.Add(new RowDefinition());
        var title = new Grid { Margin = new Thickness(16, 0, 7, 0), Background = Brushes.Transparent };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.MouseLeftButtonDown += (_, e) =>
        {
            for (DependencyObject? source = e.OriginalSource as DependencyObject; source != null; source = VisualTreeHelper.GetParent(source))
                if (source is Button) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = BrandIcon.Create(27); logo.Margin = new Thickness(0, 0, 9, 0);
        brand.Children.Add(logo);
        var name = Ui.Text("并语", 15); name.FontWeight = FontWeights.SemiBold; brand.Children.Add(name);
        _status.Margin = new Thickness(18, 0, 0, 0);
        _status.MaxWidth = 430;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        brand.Children.Add(_status);
        title.Children.Add(brand);
        var layouts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 13, 0) };
        var layoutLabel = Ui.Text("布局", 11, "Muted"); layoutLabel.Margin = new Thickness(0, 0, 8, 0); layouts.Children.Add(layoutLabel);
        for (int i = 1; i <= 4; i++)
        {
            int n = i;
            var b = Ui.Button("", $"{n} 屏视图（Ctrl+Alt+{n}）", () => SetLayout(n));
            b.Content = LayoutIcon(n); b.Width = 34; b.Height = 30; b.Padding = new Thickness(7, 6, 7, 6); b.Margin = new Thickness(1, 0, 1, 0);
            layouts.Children.Add(b); _layoutButtons[n] = b;
        }
        Grid.SetColumn(layouts, 1); title.Children.Add(layouts);
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        windowButtons.Children.Add(Ui.Button("\uE921", "最小化", () => WindowState = WindowState.Minimized, true));
        windowButtons.Children.Add(Ui.Button("\uE922", "最大化 / 还原", ToggleMaximize, true));
        windowButtons.Children.Add(Ui.Button("\uE8BB", "关闭窗口", Close, true));
        Grid.SetColumn(windowButtons, 2); title.Children.Add(windowButtons);
        _titleBar = title; root.Children.Add(title);
        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        middle.ColumnDefinitions.Add(new ColumnDefinition());
        var rail = new DockPanel { Margin = new Thickness(8, 4, 8, 5) };
        var bottom = new StackPanel();
        var settings = Ui.Button("\uE713", "设置（Ctrl+,）", OpenSettings, true); settings.Height = 38; bottom.Children.Add(settings);
        _topBarToggle = Ui.Button("▤", "隐藏标题栏和标签栏（F10）", ToggleTopBars);
        _topBarToggle.Height = 38; _topBarToggle.FontSize = 17; bottom.Children.Add(_topBarToggle);
        DockPanel.SetDock(bottom, Dock.Bottom); rail.Children.Add(bottom);
        rail.Children.Add(new ScrollViewer { Content = _sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var railFrame = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
        railFrame.SetResourceReference(Border.BackgroundProperty, "Surface");
        railFrame.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        middle.Children.Add(railFrame);
        var work = new Grid();
        _tabRowDefinition = new RowDefinition { Height = new GridLength(46) };
        work.RowDefinitions.Add(_tabRowDefinition); work.RowDefinitions.Add(new RowDefinition());
        var tabRow = new DockPanel { Margin = new Thickness(12, 0, 14, 0) };
        tabRow.SetResourceReference(DockPanel.BackgroundProperty, "Bg");
        var add = Ui.Button("\uE710", "打开新页面（Ctrl+T）", ShowHome, true);
        add.Width = 34; add.Height = 32; add.VerticalAlignment = VerticalAlignment.Center; add.Margin = new Thickness(3, 0, 2, 0);
        DockPanel.SetDock(add, Dock.Right); tabRow.Children.Add(add);
        var all = Ui.Button("全部页面  ⌄", "展开所有页面", ShowAllTabs);
        all.Width = 100; all.Height = 32; all.FontSize = 11; all.VerticalAlignment = VerticalAlignment.Center; all.Margin = new Thickness(8, 0, 3, 0);
        DockPanel.SetDock(all, Dock.Right); tabRow.Children.Add(all);
        var tabScroll = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        tabScroll.PreviewMouseWheel += (_, e) => { tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset - e.Delta); e.Handled = true; };
        tabRow.Children.Add(tabScroll); _tabBar = tabRow; work.Children.Add(tabRow);
        _main.Margin = new Thickness(10, 4, 12, 4); Grid.SetRow(_main, 1); work.Children.Add(_main);
        Grid.SetColumn(work, 1); middle.Children.Add(work); Grid.SetRow(middle, 1); root.Children.Add(middle);
        outer.Child = root;
        ApplyTopBarVisibility();
        return outer;
    }

    private void ApplyTopBarVisibility()
    {
        bool hidden = Prefs.HideTopBars;
        _titleRow.Height = new GridLength(hidden ? 0 : 50);
        _tabRowDefinition.Height = new GridLength(hidden ? 0 : 46);
        _titleBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        _tabBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        string hint = hidden ? "显示标题栏和标签栏（F10）" : "隐藏标题栏和标签栏（F10）";
        _topBarToggle.ToolTip = hint;
        System.Windows.Automation.AutomationProperties.SetName(_topBarToggle, hint);
        Ui.Background(_topBarToggle, hidden ? "AccentSoft" : "Surface");
    }

    internal void ToggleTopBars()
    {
        Prefs.HideTopBars = !Prefs.HideTopBars;
        ApplyTopBarVisibility();
        BuildSidebar();
        ScheduleSave();
        SetStatus(Prefs.HideTopBars ? "已隐藏顶栏 · 按 F10 或点击左侧底部按钮恢复" : "已显示顶栏");
    }

    private static UIElement LayoutIcon(int count)
    {
        var canvas = new System.Windows.Controls.Canvas { Width = 19, Height = 14 };
        void Rect(double x, double y, double w, double h)
        {
            var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, RadiusX = 1.5, RadiusY = 1.5, StrokeThickness = 1 };
            r.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Text");
            System.Windows.Controls.Canvas.SetLeft(r, x); System.Windows.Controls.Canvas.SetTop(r, y); canvas.Children.Add(r);
        }
        if (count == 1) Rect(0, 0, 19, 14);
        else if (count == 2) { Rect(0, 0, 8, 14); Rect(11, 0, 8, 14); }
        else if (count == 3) { Rect(0, 0, 8, 14); Rect(11, 0, 8, 5.5); Rect(11, 8.5, 8, 5.5); }
        else { Rect(0, 0, 8, 5.5); Rect(11, 0, 8, 5.5); Rect(0, 8.5, 8, 5.5); Rect(11, 8.5, 8, 5.5); }
        return canvas;
    }

    internal void BuildSidebar()
    {
        _sidebar.Children.Clear();
        if (Prefs.HideTopBars)
        {
            var drag = new Border { Height = 15, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "拖动窗口" };
            drag.MouseLeftButtonDown += (_, _) => { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            _sidebar.Children.Add(drag);
        }
        var home = Ui.Button("\uE80F", "工作台首页", ShowHome, true); home.Height = 40; home.Margin = new Thickness(0, 2, 0, 8); _sidebar.Children.Add(home);
        foreach (var site in Prefs.Sites)
        {
            var button = Ui.Button("", $"{site.Name} · 点击切换，Ctrl+点击多开", () => OpenSite(site, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
            button.Content = Ui.Badge(site, 31); button.Padding = new Thickness(4); button.Margin = new Thickness(0, 2, 0, 2);
            var menu = new ContextMenu();
            var create = new MenuItem { Header = "新开一个 " + site.Name }; create.Click += (_, _) => OpenSite(site, true); menu.Items.Add(create);
            var icon = new MenuItem { Header = "更换图标…" }; icon.Click += (_, _) => ChooseSiteIcon(site); menu.Items.Add(icon);
            if (SiteIconStore.HasCustom(site.Id))
            {
                var reset = new MenuItem { Header = "恢复默认图标" }; reset.Click += (_, _) => ResetSiteIcon(site); menu.Items.Add(reset);
            }
            if (site.Id.StartsWith("custom-", StringComparison.Ordinal))
            {
                var remove = new MenuItem { Header = "从工具栏移除" }; remove.Click += (_, _) => { Prefs.Sites.Remove(site); BuildSidebar(); if (_homeVisible) RenderWorkspace(); ScheduleSave(); }; menu.Items.Add(remove);
            }
            button.ContextMenu = menu; _sidebar.Children.Add(button);
        }
        var line = Ui.Line(); line.Margin = new Thickness(9, 12, 9, 7); _sidebar.Children.Add(line);
        var add = Ui.Button("\uE710", "添加自定义网站", AddSite, true); add.Height = 38; _sidebar.Children.Add(add);
    }

    internal void ShowHome() { _homeVisible = true; RenderWorkspace(); }
    internal void OpenSite(SiteDefinition site, bool create = false)
    {
        var existing = Workspace.Tabs.LastOrDefault(t => t.SiteId == site.Id);
        if (!create && existing != null) Workspace.FocusTab(existing.Id);
        else Workspace.Open(site);
        _homeVisible = false; RenderWorkspace(); ScheduleSave();
    }
    internal void Duplicate(TabState tab)
    {
        // A duplicate starts a fresh conversation at the provider's home page.
        OpenSite(FindSite(tab), true);
    }
    private void ChooseSiteIcon(SiteDefinition site)
    {
        var dialog = new OpenFileDialog { Title = "为 " + site.Name + " 选择图标", Filter = "图片文件|*.png;*.jpg;*.jpeg;*.ico;*.bmp|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { SiteIconStore.Import(site.Id, dialog.FileName); BuildSidebar(); RenderWorkspace(); SetStatus("已更新 " + site.Name + " 的图标"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileFormatException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { App.Log(ex); MessageBox.Show(this, "无法使用这张图片：" + ex.Message, "更换图标", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ResetSiteIcon(SiteDefinition site)
    {
        try { SiteIconStore.Reset(site.Id); BuildSidebar(); RenderWorkspace(); SetStatus("已恢复 " + site.Name + " 的默认图标"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { App.Log(ex); MessageBox.Show(this, "无法恢复图标：" + ex.Message, "恢复图标", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal void ToggleZen(TabState tab)
    {
        tab.Zen = !tab.Zen;
        if (Views.TryGetValue(tab.Id, out var view)) view.SetZen(tab.Zen);
        RenderWorkspace(); ScheduleSave();
        SetStatus(tab.Zen ? "已开启专注模式 · 按 F11 或右键标签退出" : "已退出专注模式");
    }
    internal SiteDefinition FindSite(TabState tab) => Prefs.Sites.FirstOrDefault(s => s.Id == tab.SiteId) ?? new SiteDefinition(tab.SiteId, new Uri(tab.Url).Host, tab.Url, "↗", "#6D9480", "自定义网站");
    internal void SetLayout(int count) { Workspace.SetLayout(count); _homeVisible = false; RenderWorkspace(); ScheduleSave(); }
    private void SelectTab(Guid id)
    {
        Workspace.FocusTab(id); _homeVisible = false; RenderWorkspace(); ScheduleSave();
    }
    internal void FocusTabPane(Guid id)
    {
        int index = Workspace.Slots.IndexOf(id);
        if (index < 0 || Workspace.FocusedSlot == index) return;
        Workspace.SelectSlot(index); UpdatePaneBorders(); RefreshTabs(); ScheduleSave();
    }
    internal void ExpandTab(Guid id)
    {
        int index = Workspace.Slots.IndexOf(id);
        if (index < 0) return;
        Workspace.ToggleExpand(index); RenderWorkspace(); ScheduleSave();
    }
    internal void CloseTab(Guid id)
    {
        if (Views.Remove(id, out var view)) view.Dispose();
        Workspace.Close(id); if (Workspace.Tabs.Count == 0 && Workspace.Slots.Count == 1) _homeVisible = true;
        RenderWorkspace(); ScheduleSave();
    }

    internal void RefreshTabs()
    {
        _tabs.Children.Clear();
        var home = Ui.Button("工作台", "工作台首页", ShowHome); home.Margin = new Thickness(0, 5, 5, 5);
        home.Padding = new Thickness(12, 6, 12, 6);
        if (_homeVisible) Ui.Background(home, "AccentSoft"); _tabs.Children.Add(home);
        foreach (var tab in Workspace.Tabs)
        {
            var border = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 5, 5, 5), BorderThickness = new Thickness(0) };
            bool active = !_homeVisible && Workspace.ActiveTab?.Id == tab.Id;
            border.SetResourceReference(Border.BackgroundProperty, active ? "AccentSoft" : "Bg");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var site = FindSite(tab);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = tab.Sleeping ? Ui.Text("☾", 14, "Muted") : Ui.SiteGlyph(site, 15);
            mark.Margin = new Thickness(0, 0, 7, 0); content.Children.Add(mark);
            var text = Ui.Text(tab.Title, 12); text.MaxWidth = 132; text.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(text);
            var select = Ui.Button("", tab.Title + "\n" + tab.Url, () => SelectTab(tab.Id)); select.Content = content; select.Padding = new Thickness(10, 6, 3, 6);
            select.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseTab(tab.Id); e.Handled = true; } };
            var menu = new ContextMenu();
            var duplicate = new MenuItem { Header = "新开一个 " + site.Name }; duplicate.Click += (_, _) => Duplicate(tab); menu.Items.Add(duplicate);
            var sleep = new MenuItem { Header = tab.Sleeping ? "唤醒" : "休眠" }; sleep.Click += async (_, _) =>
            {
                if (Views.TryGetValue(tab.Id, out var browser)) { if (tab.Sleeping) await browser.WakeAsync(); else await browser.SleepAsync(); }
                else { tab.Sleeping = !tab.Sleeping; RefreshTabs(); ScheduleSave(); }
            }; menu.Items.Add(sleep);
            var zen = new MenuItem { Header = "专注模式（F11）", IsCheckable = true, IsChecked = tab.Zen };
            zen.Click += (_, _) => ToggleZen(tab); menu.Items.Add(zen);
            select.ContextMenu = menu;
            row.Children.Add(select);
            var close = Ui.Button("\uE8BB", "关闭 " + tab.Title, () => CloseTab(tab.Id), true); close.FontSize = 9; close.Padding = new Thickness(7, 6, 7, 6); close.Margin = new Thickness(0, 2, 3, 2); row.Children.Add(close);
            border.Child = row; _tabs.Children.Add(border);
        }
    }

    private void ShowAllTabs()
    {
        var menu = new ContextMenu();
        foreach (var tab in Workspace.Tabs)
        {
            var item = new MenuItem { Header = (tab.Sleeping ? "☾  " : "") + tab.Title, ToolTip = tab.Url };
            item.Click += (_, _) => SelectTab(tab.Id); menu.Items.Add(item);
        }
        if (Workspace.Tabs.Count == 0) menu.Items.Add(new MenuItem { Header = "还没有打开的页面", IsEnabled = false });
        menu.IsOpen = true;
    }

    internal void RenderWorkspace()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            foreach (var view in Views.Values) if (view.Parent is Panel parent) parent.Children.Remove(view);
            _main.Children.Clear(); _paneBorders.Clear();
            foreach (var (number, button) in _layoutButtons) Ui.Background(button, !_homeVisible && Workspace.Slots.Count == number ? "AccentSoft" : "Bg");
            if (_homeVisible) _main.Children.Add(BuildHome());
            else if (Workspace.ExpandedSlot is int expanded) _main.Children.Add(BuildPane(expanded));
            else if (Workspace.Slots.Count == 1) _main.Children.Add(BuildPane(0));
            else if (Workspace.Slots.Count == 2) _main.Children.Add(Split("2", true, BuildPane(0), BuildPane(1)));
            else if (Workspace.Slots.Count == 3) _main.Children.Add(Split("3", true, BuildPane(0), Split("3r", false, BuildPane(1), BuildPane(2))));
            else _main.Children.Add(Split("4", false, Split("4t", true, BuildPane(0), BuildPane(1)), Split("4b", true, BuildPane(2), BuildPane(3))));
            UpdatePaneBorders(); RefreshTabs();
        }
        finally { _rendering = false; }
    }

    private UIElement Split(string key, bool horizontal, UIElement first, UIElement second)
    {
        var grid = new Grid();
        var ratio = Prefs.SplitRatios.GetValueOrDefault(key);
        double a = ratio is { Length: 2 } && double.IsFinite(ratio[0]) ? Math.Clamp(ratio[0], .2, .8) : .5;
        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(a, GridUnitType.Star), MinWidth = 240 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - a, GridUnitType.Star), MinWidth = 240 });
            Grid.SetColumn(second, 2);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(a, GridUnitType.Star), MinHeight = 150 });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - a, GridUnitType.Star), MinHeight = 150 });
            Grid.SetRow(second, 2);
        }
        grid.Children.Add(first); grid.Children.Add(second);
        var splitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = false,
            Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            ToolTip = horizontal ? "左右拖动，调整分屏宽度" : "上下拖动，调整分屏高度"
        };
        splitter.SetResourceReference(BackgroundProperty, "Bg");
        if (horizontal) Grid.SetColumn(splitter, 1); else Grid.SetRow(splitter, 1);
        splitter.DragCompleted += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            // GridSplitter updates star lengths first; read ActualWidth after the next layout pass.
            double x = horizontal ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight;
            double y = horizontal ? grid.ColumnDefinitions[2].ActualWidth : grid.RowDefinitions[2].ActualHeight;
            if (x + y > 0) Prefs.SplitRatios[key] = [x / (x + y), y / (x + y)];
            ScheduleSave();
        }, DispatcherPriority.Loaded);
        grid.Children.Add(splitter);
        var grip = new Border
        {
            Width = horizontal ? 2 : 36, Height = horizontal ? 36 : 2,
            CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        grip.SetResourceReference(Border.BackgroundProperty, "Stroke");
        splitter.MouseEnter += (_, _) => grip.SetResourceReference(Border.BackgroundProperty, "Accent");
        splitter.MouseLeave += (_, _) => grip.SetResourceReference(Border.BackgroundProperty, "Stroke");
        if (horizontal) Grid.SetColumn(grip, 1); else Grid.SetRow(grip, 1);
        grid.Children.Add(grip);
        return grid;
    }

    private UIElement BuildPane(int index)
    {
        var tab = Workspace.Tabs.FirstOrDefault(t => t.Id == Workspace.Slots[index]);
        var border = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        string surface = tab?.SiteId switch { "gemini" => "GeminiSurface", "chatgpt" => "ChatGptSurface", _ => "Surface" };
        border.SetResourceReference(Border.BackgroundProperty, surface); _paneBorders[index] = border;
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tab?.Zen == true ? 0 : 30) }); grid.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Margin = new Thickness(11, 2, 5, 0), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, _) => { Workspace.SelectSlot(index); UpdatePaneBorders(); RefreshTabs(); };
        var newButton = Ui.Button("\uE710", "在此屏新开页面", () => { Workspace.SelectSlot(index); ChooseSite(index); }, true); newButton.FontSize = 11; newButton.Padding = new Thickness(5); DockPanel.SetDock(newButton, Dock.Right); header.Children.Add(newButton);
        var label = Ui.Text($"0{index + 1}   /   " + (tab == null ? "选择一个工具" : FindSite(tab).Name), 10, "Muted"); header.Children.Add(label);
        header.Visibility = tab?.Zen == true ? Visibility.Collapsed : Visibility.Visible; grid.Children.Add(header);
        // WebView2 owns a native child window. Keep its rectangle inside the pane so
        // website text, menus and fixed footers cannot sit under our chrome.
        var body = new Grid { Margin = tab?.Zen == true ? new Thickness(4, 5, 4, 4) : new Thickness(3, 0, 3, 3) };
        body.SetResourceReference(BackgroundProperty, surface);
        Grid.SetRow(body, 1); grid.Children.Add(body);
        if (tab == null) body.Children.Add(BuildPicker(index));
        else
        {
            if (!Views.TryGetValue(tab.Id, out var view)) { view = new BrowserTabView(tab, this, Browsers); Views[tab.Id] = view; }
            body.Children.Add(view); _ = view.EnsureLoadedAsync();
        }
        border.Child = grid; return border;
    }
    private void UpdatePaneBorders()
    {
        foreach (var (index, border) in _paneBorders) border.SetResourceReference(Border.BorderBrushProperty, Workspace.FocusedSlot == index ? "FocusedStroke" : "Stroke");
    }

    private UIElement BuildPicker(int index)
    {
        var stack = new StackPanel { MaxWidth = 290, Margin = new Thickness(22), VerticalAlignment = VerticalAlignment.Center };
        var icon = Ui.Text("＋", 26, "Accent"); icon.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(icon);
        var title = Ui.Text("把灵感放在一起", 18); title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 10, 0, 8); stack.Children.Add(title);
        var hint = Ui.Text("选择工具，在这一屏打开新页面", 11, "Muted"); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Margin = new Thickness(0, 0, 0, 15); stack.Children.Add(hint);
        var tools = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var site in Prefs.Sites)
        {
            var b = Ui.Button("", "在此屏新开 " + site.Name, () => { Workspace.SelectSlot(index); OpenSite(site, true); });
            b.Margin = new Thickness(3); b.FontSize = 12; Ui.Background(b, "Raised"); tools.Children.Add(b);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var siteIcon = Ui.SiteGlyph(site, 16); siteIcon.Margin = new Thickness(0, 0, 7, 0); row.Children.Add(siteIcon);
            row.Children.Add(Ui.Text(site.Name, 12)); b.Content = row;
        }
        stack.Children.Add(tools);
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private void ChooseSite(int index)
    {
        var menu = new ContextMenu();
        foreach (var site in Prefs.Sites)
        {
            var item = new MenuItem { Header = site.Name }; item.Click += (_, _) => { Workspace.SelectSlot(index); OpenSite(site, true); }; menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private UIElement BuildHome()
    {
        var border = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var stack = new StackPanel { MaxWidth = 950, Margin = new Thickness(48, 45, 48, 28), HorizontalAlignment = HorizontalAlignment.Stretch };
        var eyebrow = Ui.Text("Y O U R   A I   W O R K S P A C E", 10, "Accent"); eyebrow.FontWeight = FontWeights.SemiBold; stack.Children.Add(eyebrow);
        var title = Ui.Text("让不同想法，并肩发生。", 33); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 14, 0, 12); stack.Children.Add(title);
        var subtitle = Ui.Text("多个 AI 网页并排使用，自由切换；同站多开，共用登录。", 13, "Muted"); subtitle.Margin = new Thickness(0, 0, 0, 34); stack.Children.Add(subtitle);
        var section = new DockPanel { Margin = new Thickness(2, 0, 2, 14) };
        var add = Ui.Button("＋ 添加网站", "添加自定义网站", AddSite); add.FontSize = 11; add.Padding = new Thickness(8, 3, 8, 3); DockPanel.SetDock(add, Dock.Right); section.Children.Add(add);
        var label = Ui.Text("选择你的 AI 工具", 13); label.FontWeight = FontWeights.SemiBold; section.Children.Add(label); stack.Children.Add(section);
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var site in Prefs.Sites)
        {
            var button = Ui.Button("", "打开 " + site.Name, () => OpenSite(site, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
            button.Margin = new Thickness(5); button.Padding = new Thickness(20, 18, 18, 17); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.SetResourceReference(BorderBrushProperty, "Stroke");
            var content = new StackPanel();
            var cardTop = new DockPanel(); var arrow = Ui.Text("↗", 17, "Muted"); arrow.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(arrow, Dock.Right); cardTop.Children.Add(arrow);
            var badge = Ui.Badge(site, 42); badge.HorizontalAlignment = HorizontalAlignment.Left; cardTop.Children.Add(badge); content.Children.Add(cardTop);
            var name = Ui.Text(site.Name, 16); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 14, 0, 7); content.Children.Add(name);
            var description = Ui.Text(site.Description, 11, "Muted"); description.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(description);
            button.Content = content; cards.Children.Add(button);
        }
        stack.Children.Add(cards);
        var splitBox = new Border { Margin = new Thickness(5, 28, 5, 0), Padding = new Thickness(22, 18, 22, 18), CornerRadius = new CornerRadius(10) };
        splitBox.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var splitRow = new DockPanel();
        var startSplit = Ui.Button("开启分屏  →", "开启双屏工作区", () => SetLayout(2)); startSplit.VerticalAlignment = VerticalAlignment.Center; startSplit.Margin = new Thickness(20, 0, 0, 0); Ui.Background(startSplit, "Surface"); DockPanel.SetDock(startSplit, Dock.Right); splitRow.Children.Add(startSplit);
        var splitText = new StackPanel(); var splitTitle = Ui.Text("多一个视角，多一点灵感。", 14); splitTitle.FontWeight = FontWeights.SemiBold; splitText.Children.Add(splitTitle);
        var splitHint = Ui.Text("2 / 3 / 4 屏并排思考，也可以同时打开多个相同工具。", 11, "Muted"); splitHint.TextWrapping = TextWrapping.Wrap; splitHint.Margin = new Thickness(0, 8, 0, 0); splitText.Children.Add(splitHint); splitRow.Children.Add(splitText); splitBox.Child = splitRow; stack.Children.Add(splitBox);
        var tips = Ui.Text("Ctrl + T  新页面       ·       Ctrl + 点击工具  同站多开       ·       " + Prefs.Hotkey + "  随时唤出", 11, "Muted"); tips.HorizontalAlignment = HorizontalAlignment.Center; tips.Margin = new Thickness(0, 28, 0, 4); stack.Children.Add(tips);
        border.Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; return border;
    }

    private void AddSite()
    {
        var dialog = new AddSiteWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Site == null) return;
        Prefs.Sites.Add(dialog.Site); BuildSidebar(); if (_homeVisible) RenderWorkspace(); ScheduleSave(); SetStatus("已添加 " + dialog.Site.Name);
    }
    private void OpenSettings()
    {
        var dialog = new SettingsWindow(Prefs, (shortcut) => _hotkey.Register(shortcut, out var error) ? null : error) { Owner = this };
        if (dialog.ShowDialog() == true) { ApplyTheme(); ScheduleSave(); SetStatus("设置已保存"); }
    }
    private void ApplyTheme() { Theme.Apply(Prefs.Theme); foreach (var view in Views.Values) view.UpdateTheme(); if (_homeVisible) RenderWorkspace(); }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    internal void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized) { Browsers.HidePopups(); Hide(); SaveNow(); }
        else RestoreWindow();
    }
    internal void RestoreWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Browsers.ShowPopups(); }
    private void CreateTray()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Bingyu.ico"));
        using (resource.Stream) _trayIcon = new System.Drawing.Icon(resource.Stream);
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "并语 · AI 多屏工作台", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏并语", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => { RestoreWindow(); OpenSettings(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出并语", null, (_, _) => Dispatcher.Invoke(Quit));
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
    }
    internal void Quit() { _quitting = true; Close(); Application.Current.Shutdown(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveNow();
        if (!_quitting && Prefs.CloseToTray && _tray != null) { e.Cancel = true; Browsers.HidePopups(); Hide(); }
        else { _quitting = true; Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown()); }
    }
    internal void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    internal void SaveNow()
    {
        try
        {
            if (WindowState == WindowState.Normal) { Prefs.Width = Width; Prefs.Height = Height; }
            Prefs.Maximized = WindowState == WindowState.Maximized;
            _store.Save(State);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); SetStatus("设置暂时无法保存，请检查磁盘空间和权限。"); }
    }
    internal void SetStatus(string text) { _status.Text = text; _statusTimer.Stop(); _statusTimer.Start(); }

    internal bool IsAppShortcut(Key key, ModifierKeys mods) =>
        (mods == ModifierKeys.Control && key is Key.T or Key.W or Key.L or Key.Tab or Key.OemComma or Key.D) ||
        (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Tab) ||
        (mods == (ModifierKeys.Control | ModifierKeys.Alt) && key is >= Key.D1 and <= Key.D4) ||
        (mods == ModifierKeys.None && key is Key.F5 or Key.F10 or Key.F11) ||
        (mods == ModifierKeys.None && key == Key.Escape && Workspace.ExpandedSlot != null);
    internal void HandleShortcut(Key key, ModifierKeys mods)
    {
        if (mods == (ModifierKeys.Control | ModifierKeys.Alt)) { SetLayout(key - Key.D1 + 1); return; }
        var active = Workspace.ActiveTab;
        if (key == Key.T) ShowHome();
        else if (key == Key.W && !_homeVisible && active != null) CloseTab(active.Id);
        else if (key == Key.L && !_homeVisible && active != null && Views.TryGetValue(active.Id, out var view)) view.FocusAddress();
        else if (key == Key.F5 && !_homeVisible && active != null && Views.TryGetValue(active.Id, out var reload)) reload.Reload();
        else if (key == Key.F10) ToggleTopBars();
        else if (key == Key.F11 && !_homeVisible && active != null) ToggleZen(active);
        else if (key == Key.D && active != null) Duplicate(active);
        else if (key == Key.OemComma) OpenSettings();
        else if (key == Key.Escape) { Workspace.ExpandedSlot = null; RenderWorkspace(); }
        else if (key == Key.Tab && Workspace.Tabs.Count > 0)
        {
            int current = active == null ? -1 : Workspace.Tabs.IndexOf(active);
            int next = (current + (mods.HasFlag(ModifierKeys.Shift) ? -1 : 1) + Workspace.Tabs.Count) % Workspace.Tabs.Count;
            SelectTab(Workspace.Tabs[next].Id);
        }
    }
}
