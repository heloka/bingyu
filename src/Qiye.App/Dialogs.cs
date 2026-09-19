namespace Qiye;

internal sealed class AddSiteWindow : Window
{
    public SiteDefinition? Site { get; private set; }
    public AddSiteWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "添加网站 · 并语"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(28) };
        var title = Ui.Text("你的工具，也有位置。", 23); title.FontWeight = FontWeights.SemiBold; stack.Children.Add(title);
        var hint = Ui.Text("添加任意 AI 网页，使用网站原有的账号登录。", 12, "Muted"); hint.Margin = new Thickness(0, 10, 0, 25); stack.Children.Add(hint);
        stack.Children.Add(Ui.Text("网站名称", 12));
        var name = new TextBox { Margin = new Thickness(0, 8, 0, 18), MaxLength = 30 }; stack.Children.Add(name);
        stack.Children.Add(Ui.Text("网站地址", 12));
        var url = new TextBox { Margin = new Thickness(0, 8, 0, 8), ToolTip = "例如 https://example.com" }; stack.Children.Add(url);
        var example = Ui.Text("支持 http / https，省略协议时自动使用 https。", 11, "Muted"); stack.Children.Add(example);
        var error = Ui.Text("", 12); error.Foreground = Theme.Brush("#C26953"); error.TextWrapping = TextWrapping.Wrap; error.Margin = new Thickness(0, 15, 0, 8); stack.Children.Add(error);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", "取消", () => DialogResult = false); cancel.IsCancel = true; row.Children.Add(cancel);
        var add = Ui.Button("添加网站", "添加网站", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "给网站起个名字吧。"; name.Focus(); return; }
            if (!UrlPolicy.TryNormalize(url.Text, out var safe)) { error.Text = "请输入有效的网址，例如 https://example.com"; url.Focus(); return; }
            Site = new SiteDefinition("custom-" + Guid.NewGuid().ToString("N"), name.Text.Trim(), safe, System.Globalization.StringInfo.GetNextTextElement(name.Text.Trim()), "#758A68", new Uri(safe).Host);
            DialogResult = true;
        });
        add.IsDefault = true; add.Margin = new Thickness(10, 0, 0, 0); Ui.Background(add, "AccentSoft"); row.Children.Add(add); stack.Children.Add(row);
        Content = stack; Loaded += (_, _) => name.Focus();
    }
}

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(Preferences prefs, Func<string, string?> registerHotkey)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "偏好设置 · 并语"; Width = 540; SizeToContent = SizeToContent.Height; MaxHeight = 800; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
        var title = Ui.Text("按你的习惯来。", 25); title.FontWeight = FontWeights.SemiBold; stack.Children.Add(title);
        var subtitle = Ui.Text("少一点打扰，多一点专注。", 12, "Muted"); subtitle.Margin = new Thickness(0, 10, 0, 24); stack.Children.Add(subtitle);
        stack.Children.Add(Ui.Text("外观", 13));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 24) };
        var radios = new Dictionary<string, RadioButton>();
        foreach (var (id, label) in new[] { ("System", "跟随系统"), ("Light", "浅色"), ("Dark", "深色") })
        {
            var radio = new RadioButton { Content = label, GroupName = "Theme", IsChecked = prefs.Theme == id, Margin = new Thickness(0, 0, 32, 0) };
            radio.SetResourceReference(ForegroundProperty, "Text"); radios[id] = radio; themeRow.Children.Add(radio);
        }
        stack.Children.Add(themeRow); stack.Children.Add(Ui.Line());
        var hotkeyTitle = Ui.Text("随时唤出 / 隐藏", 13); hotkeyTitle.Margin = new Thickness(0, 22, 0, 10); stack.Children.Add(hotkeyTitle);
        var hotkey = new TextBox { Text = prefs.Hotkey }; stack.Children.Add(hotkey);
        hotkey.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.Tab) return;
            if (mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt))
            {
                hotkey.Text = (mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + key;
                e.Handled = true;
            }
        };
        var shortcutHint = Ui.Text("点击输入框按下组合键，或输入 Ctrl+Shift+Q。\n如果已被其他程序占用，保存时会提示。", 11, "Muted"); shortcutHint.Margin = new Thickness(0, 9, 0, 18); stack.Children.Add(shortcutHint);
        var close = new CheckBox { Content = "关闭窗口后留在系统托盘", IsChecked = prefs.CloseToTray }; stack.Children.Add(close);
        var restore = new CheckBox { Content = "启动时恢复页面与分屏布局", IsChecked = prefs.RestoreSession }; stack.Children.Add(restore);
        var restoreHint = Ui.Text("后台标签按需加载。已打开的页面保持运行，可手动休眠。", 11, "Muted"); restoreHint.Margin = new Thickness(0, 8, 0, 22); stack.Children.Add(restoreHint); stack.Children.Add(Ui.Line());
        var version = Ui.Text("并语 BINGYU  0.1.9", 12); version.FontWeight = FontWeights.SemiBold; version.Margin = new Thickness(0, 20, 0, 8); stack.Children.Add(version);
        var about = Ui.Text("使用系统 WebView2。无需 API Key，直接登录官方网站。\n各网站可用性、登录方式和订阅权限由对应平台决定。\n默认浏览器拥有独立的登录状态。", 11, "Muted"); about.TextWrapping = TextWrapping.Wrap; stack.Children.Add(about);
        var error = Ui.Text("", 12); error.TextWrapping = TextWrapping.Wrap; error.Foreground = Theme.Brush("#C26953"); error.Margin = new Thickness(0, 15, 0, 10); stack.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", "取消", () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        var save = Ui.Button("保存设置", "保存设置", () =>
        {
            string normalized = hotkey.Text.Trim();
            string? problem = registerHotkey(normalized);
            if (problem != null) { error.Text = problem; return; }
            prefs.Hotkey = normalized; prefs.Theme = radios.FirstOrDefault(r => r.Value.IsChecked == true).Key ?? "System";
            prefs.CloseToTray = close.IsChecked == true; prefs.RestoreSession = restore.IsChecked == true;
            DialogResult = true;
        });
        Ui.Background(save, "AccentSoft"); save.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(save); stack.Children.Add(buttons);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
