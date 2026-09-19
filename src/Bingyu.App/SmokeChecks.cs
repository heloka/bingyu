using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using Microsoft.Web.WebView2.Core;

namespace Bingyu;

// Runs only with --smoke-test and an isolated --data-dir. No third-party account is used.
internal static class SmokeChecks
{
    internal static async Task RunAsync(MainWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        int failures = 0;
        async Task Check(string name, Func<Task> action)
        {
            var clock = Stopwatch.StartNew();
            try { await action(); results.Add(new { name, passed = true, ms = clock.ElapsedMilliseconds }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); }
            File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            double startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            long nativeMemoryBytes = Process.GetCurrentProcess().WorkingSet64;
            await Check("Native homepage starts without creating a browser", async () =>
            {
                Assert(window.Views.Count == 0, "Browser was loaded before requested");
                Assert((window.Content as Border)?.Child is Grid shell && shell.RowDefinitions.Count == 2,
                    "A persistent footer still reserves webpage space");
                Assert(Descendants(window).OfType<Button>().Count(b => AutomationProperties.GetName(b) == "隐藏标题栏和标签栏（F10）") == 1,
                    "Top-bar toggle should have one fixed location");
                Theme.Apply("Light"); window.ShowHome(); await Snapshot(window, Path.Combine(output, "home-light.png"));
                Theme.Apply("Dark"); window.ShowHome(); await Snapshot(window, Path.Combine(output, "home-dark.png"));
                Theme.Apply("Light"); window.ShowHome();
            });
            await Check("2 / 3 / 4 native split layouts render", async () =>
            {
                foreach (int n in new[] { 2, 3, 4 })
                {
                    window.SetLayout(n); Assert(window.Workspace.Slots.Count == n, "Wrong slot count");
                    await Snapshot(window, Path.Combine(output, $"split-{n}.png"));
                }
            });
            await Check("All-pages control has a clear fixed position above panes", async () =>
            {
                window.SetLayout(2);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var button = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "展开所有页面");
                var pane = Descendants(window).OfType<Border>().First(b => b.Child is Grid g && g.RowDefinitions.Count == 2 && g.RowDefinitions[0].Height.Value == 30);
                double buttonBottom = button.TranslatePoint(new Point(0, button.ActualHeight), window).Y;
                double paneTop = pane.TranslatePoint(new Point(0, 0), window).Y;
                Assert(button.ActualHeight >= 32 && paneTop - buttonBottom >= 5, "All-pages button overlaps the pane");
            });
            await Check("Maximized window stays above the Windows taskbar", async () =>
            {
                window.WindowState = WindowState.Maximized;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert(WindowBounds.TryGetVisibleWindowAndWorkArea(window, out var visible, out var work), "Could not read window bounds");
                Assert(visible.Bottom <= work.Bottom + 2, $"Window bottom {visible.Bottom} extends below work area {work.Bottom}");
                Assert(visible.Top >= work.Top - 2, $"Window top {visible.Top} extends above work area {work.Top}");
                window.WindowState = WindowState.Normal;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            });
            using var server = new FixtureServer();
            var site = new SiteDefinition("test", "Local test fixture", server.Url, "T", "#477A62", "Integration test only");
            TabState? first = null;
            TabState? second = null;
            await Check("WebView2 initializes and loads a real HTTP page", async () =>
            {
                window.SetLayout(1); window.OpenSite(site, true); first = window.Workspace.ActiveTab!;
                await Wait(() => window.Views[first.Id].Ready, "WebView2 initialization");
                await WaitScript(window.Views[first.Id], "document.title", "Bingyu fixture");
            });
            await Check("Website notifications are allowed without a prompt and saved", async () =>
            {
                var view = window.Views[first!.Id];
                await view.Browser!.ExecuteScriptAsync("window.permissionResult='pending'; Notification.requestPermission().then(value=>window.permissionResult=value)");
                await WaitScript(view, "window.permissionResult", "granted");
                var permissions = await view.Browser.CoreWebView2.Profile.GetNonDefaultPermissionSettingsAsync();
                Assert(permissions.Any(p => p.PermissionKind == CoreWebView2PermissionKind.Notifications &&
                    p.PermissionState == CoreWebView2PermissionState.Allow && p.PermissionOrigin.Contains("127.0.0.1")),
                    "Notification approval was not retained in the shared profile");
            });
            await Check("Two independent pages share cookies and local storage", async () =>
            {
                Assert(first != null, "First tab was not created");
                var a = window.Views[first!.Id];
                await a.Browser!.ExecuteScriptAsync("document.cookie='bingyuShared=works; SameSite=Lax; path=/'; localStorage.setItem('shared','yes'); document.querySelector('input').value='first-only'");
                window.SetLayout(2); window.Workspace.SelectSlot(1); window.OpenSite(site, true); second = window.Workspace.ActiveTab!;
                await Wait(() => window.Views[second.Id].Ready, "Second browser initialization");
                var b = window.Views[second.Id]; await WaitScript(b, "document.title", "Bingyu fixture");
                Assert((await b.Browser!.ExecuteScriptAsync("document.cookie")).Contains("bingyuShared=works"), "Cookie not shared");
                Assert((await b.Browser.ExecuteScriptAsync("localStorage.getItem('shared')")) == "\"yes\"", "Storage not shared");
                Assert((await b.Browser.ExecuteScriptAsync("document.querySelector('input').value")) == "\"\"", "DOM state leaked between tabs");
            });
            await Check("Zen mode fills only its tab's pane without reloading either page", async () =>
            {
                var a = window.Views[first!.Id]; var b = window.Views[second!.Id];
                var original = a.Browser;
                window.ToggleZen(first);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert(first.Zen && a.RowDefinitions[0].Height.Value == 0 && a.RowDefinitions[1].Height.Value == 0, "Zen toolbar was not removed");
                Assert(!second.Zen && b.RowDefinitions[0].Height.Value == 43, "Other pane changed");
                Assert(ReferenceEquals(original, a.Browser), "Zen reloaded the browser");
                Assert(await a.Browser!.ExecuteScriptAsync("document.querySelector('input').value") == "\"first-only\"", "Zen reset page content");
                window.UpdateLayout();
                var pane = (a.Parent as Grid)?.Parent is Grid paneGrid ? paneGrid.Parent as Border : null;
                Assert(pane != null, "Zen pane structure changed");
                var webTopLeft = a.Browser.TranslatePoint(new Point(0, 0), window);
                var webBottomRight = a.Browser.TranslatePoint(new Point(a.Browser.ActualWidth, a.Browser.ActualHeight), window);
                var paneTopLeft = pane!.TranslatePoint(new Point(0, 0), window);
                var paneBottomRight = pane.TranslatePoint(new Point(pane.ActualWidth, pane.ActualHeight), window);
                Assert(webTopLeft.X >= paneTopLeft.X + 4 && webTopLeft.Y >= paneTopLeft.Y + 5,
                    "Webpage touches the pane top or left chrome");
                Assert(webBottomRight.X <= paneBottomRight.X - 4 && webBottomRight.Y <= paneBottomRight.Y - 4,
                    "Webpage extends under the pane bottom or right chrome");
                window.WindowState = WindowState.Maximized;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                webTopLeft = a.Browser.TranslatePoint(new Point(0, 0), window);
                webBottomRight = a.Browser.TranslatePoint(new Point(a.Browser.ActualWidth, a.Browser.ActualHeight), window);
                paneTopLeft = pane.TranslatePoint(new Point(0, 0), window);
                paneBottomRight = pane.TranslatePoint(new Point(pane.ActualWidth, pane.ActualHeight), window);
                Assert(webTopLeft.Y >= paneTopLeft.Y + 5 && webBottomRight.Y <= paneBottomRight.Y - 4,
                    "Maximized Zen webpage overlaps the pane chrome");
                window.WindowState = WindowState.Normal;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.SaveNow();
                Assert(new StateStore(App.DataDirectory).Load().Workspace.Tabs.Single(t => t.Id == first.Id).Zen, "Zen state was not saved");
                window.ToggleZen(first);
                Assert(!first.Zen && a.RowDefinitions[0].Height.Value == 43, "Zen toolbar did not return");
            });
            await Check("Hiding the top bars expands both panes and preserves their pages", async () =>
            {
                var a = window.Views[first!.Id]; var b = window.Views[second!.Id];
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                double before = a.ActualHeight;
                var firstBrowser = a.Browser; var secondBrowser = b.Browser;
                window.ToggleTopBars();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert(window.AreTopBarsHidden && a.ActualHeight > before + 85, "The hidden bars did not give space to the panes");
                Assert(ReferenceEquals(firstBrowser, a.Browser) && ReferenceEquals(secondBrowser, b.Browser), "Hiding the bars reloaded a page");
                Assert(Descendants(window).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "显示标题栏和标签栏（F10）"), "Restore control is missing");
                await Snapshot(window, Path.Combine(output, "top-hidden.png"));
                window.SaveNow();
                Assert(new StateStore(App.DataDirectory).Load().Preferences.HideTopBars, "Hidden top bars were not saved");
                window.ToggleTopBars();
                Assert(!window.AreTopBarsHidden, "The top bars did not return");
            });
            await Check("A local image overrides and restores a site icon", () =>
            {
                var iconPath = Path.Combine(output, "icon-source.png");
                var pixels = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray();
                var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(iconPath)) encoder.Save(stream);
                SiteIconStore.Import("test", iconPath);
                Assert(SiteIconStore.HasCustom("test") && SiteIconStore.Load("test")?.PixelWidth == 16, "Icon override failed");
                var invalidPath = Path.Combine(output, "invalid-icon.png");
                File.WriteAllText(invalidPath, "not an image");
                try { SiteIconStore.Import("test", invalidPath); throw new InvalidOperationException("Invalid image was accepted"); }
                catch (Exception ex) when (ex is FileFormatException or NotSupportedException) { }
                Assert(SiteIconStore.Load("test")?.PixelWidth == 16, "Bad replacement damaged the previous icon");
                SiteIconStore.Reset("test");
                Assert(!SiteIconStore.HasCustom("test") && SiteIconStore.Load("test") == null, "Icon reset failed");
                return Task.CompletedTask;
            });
            await Check("Visible splitter drags and resizes the live browser viewport", async () =>
            {
                var split = Descendants(window).OfType<Grid>().First(g => g.ColumnDefinitions.Count == 3 && g.Children.OfType<GridSplitter>().Any());
                var handle = split.Children.OfType<GridSplitter>().Single();
                Assert(handle.Cursor == Cursors.SizeWE && handle.ToolTip != null, "The drag affordance is missing");
                var beforeWidth = split.ColumnDefinitions[0].ActualWidth;
                var beforeCss = await window.Views[first!.Id].Browser!.ExecuteScriptAsync("window.innerWidth");
                handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
                handle.RaiseEvent(new DragDeltaEventArgs(90, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                handle.RaiseEvent(new DragCompletedEventArgs(90, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert(split.ColumnDefinitions[0].ActualWidth > beforeWidth + 45, "Splitter did not move");
                Assert(window.State.Preferences.SplitRatios.TryGetValue("2", out var saved) && saved[0] > .53, "Moved ratio was not saved");
                string afterCss = await window.Views[first.Id].Browser!.ExecuteScriptAsync("window.innerWidth");
                Assert(int.Parse(afterCss) > int.Parse(beforeCss) + 40, "Web content viewport did not resize");
            });
            await Check("Changing layouts retains live DOM state", async () =>
            {
                Assert(first != null && second != null, "Missing fixture tabs");
                window.SetLayout(4); window.SetLayout(3); window.SetLayout(2);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert(await window.Views[first!.Id].Browser!.ExecuteScriptAsync("document.querySelector('input').value") == "\"first-only\"", "Layout change reset the page");
                window.ExpandTab(first.Id); Assert(window.Workspace.ExpandedSlot != null, "Not expanded");
                window.ExpandTab(first.Id); Assert(window.Workspace.ExpandedSlot == null, "Not restored");
            });
            await Check("Sleep / wake preserves the current webpage", async () =>
            {
                var a = window.Views[first!.Id]; await a.SleepAsync(); Assert(first.Sleeping, "Suspend did not succeed");
                await a.WakeAsync(); Assert(!first.Sleeping, "Did not wake");
                Assert(await a.Browser!.ExecuteScriptAsync("document.querySelector('input').value") == "\"first-only\"", "Wake reset DOM state");
            });
            await Check("A real popup shares the profile and preserves window.opener", async () =>
            {
                var a = window.Views[first!.Id];
                await a.Browser!.ExecuteScriptAsync("window.popupResult=''; window.addEventListener('message', e=>window.popupResult=e.data); window.open('/popup','fixturePopup')");
                await WaitScript(a, "window.popupResult", "bingyuShared=works");
            });
            await Check("Hide / show preserves initialized controls", async () =>
            {
                window.ToggleVisibility(); Assert(!window.IsVisible, "Not hidden");
                window.RestoreWindow(); Assert(window.IsVisible, "Not shown");
                Assert(await window.Views[first!.Id].Browser!.ExecuteScriptAsync("document.querySelector('input').value") == "\"first-only\"", "Hide reset DOM state");
            });
            await Check("Hotkey registration detects a real conflict and retains the previous binding", () =>
            {
                using var primary = new HotkeyService(window, () => { });
                using var other = new HotkeyService(window, () => { });
                Assert(primary.Register("Ctrl+Alt+F11", out var error), error);
                // Use a second HWND, because Windows permits a same-window hotkey to replace its ID.
                var temporary = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Opacity = 0 };
                temporary.Show();
                try
                {
                    using var conflicting = new HotkeyService(temporary, () => { });
                    Assert(!conflicting.Register("Ctrl+Alt+F11", out _), "An occupied hotkey was accepted");
                    Assert(primary.Current == "Ctrl+Alt+F11", "Previous binding lost");
                    Assert(!primary.Register("Shift+X", out _), "Unsafe unmodified shortcut accepted");
                }
                finally { temporary.Close(); }
                return Task.CompletedTask;
            });
            await Check("Settings restore tabs and split layout", () =>
            {
                window.SaveNow(); var loaded = new StateStore(App.DataDirectory).Load();
                Assert(loaded.Workspace.Tabs.Count == window.Workspace.Tabs.Count && loaded.Workspace.Slots.SequenceEqual(window.Workspace.Slots), "Session did not round-trip");
                return Task.CompletedTask;
            });
            await Check("Closing a tab disposes exactly its browser", () =>
            {
                window.CloseTab(second!.Id); Assert(!window.Views.ContainsKey(second.Id) && window.Views[first!.Id].Ready, "Incorrect browser disposed");
                return Task.CompletedTask;
            });
            await Check("Fixed Gemini zoom survives split changes and session restore", async () =>
            {
                window.SetLayout(2); window.Workspace.SelectSlot(1);
                var geminiFixture = new SiteDefinition("gemini", "Gemini fixture", server.Url, "G", "#6081DD", "Local test");
                window.OpenSite(geminiFixture, true);
                var tab = window.Workspace.ActiveTab!;
                await Wait(() => window.Views[tab.Id].Ready, "Gemini zoom fixture");
                var view = window.Views[tab.Id];
                var pane = (view.Parent as Grid)?.Parent is Grid paneGrid ? paneGrid.Parent as Border : null;
                Assert(pane?.Background is SolidColorBrush lightEdge && lightEdge.Color == Color.FromRgb(250, 249, 249),
                    "Gemini pane edge does not match its light webpage background");
                Assert(view.Browser!.DefaultBackgroundColor == System.Drawing.Color.FromArgb(250, 249, 249),
                    "Gemini loading background does not match the pane");
                Theme.Apply("Dark"); view.UpdateTheme();
                Assert(pane!.Background is SolidColorBrush darkEdge && darkEdge.Color == Color.FromRgb(32, 33, 36),
                    "Gemini pane edge did not follow dark theme");
                Theme.Apply("Light"); view.UpdateTheme();
                view.SetZoom(1);
                window.SetLayout(1);
                window.SetLayout(2);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert(view.Browser!.ZoomFactor == 1 && tab.ZoomFactor == 1, "Fixed zoom changed with the split");
                window.SaveNow();
                Assert(new StateStore(App.DataDirectory).Load().Workspace.Tabs.Single(t => t.Id == tab.Id).ZoomFactor == 1, "Fixed zoom was not saved");
                window.CloseTab(tab.Id);
            });
            if (App.GeminiCheck)
            {
                await Check("Gemini loads in a dual pane and uses responsive zoom", async () =>
                {
                    window.SetLayout(2);
                    window.Workspace.SelectSlot(0);
                    window.OpenSite(SiteDefinition.Defaults().Single(s => s.Id == "gemini"), true);
                    var gemini = window.Workspace.ActiveTab!;
                    await Wait(() => window.Views[gemini.Id].Ready, "Gemini browser initialization");
                    var view = window.Views[gemini.Id];
                    await Task.Delay(5000);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var browser = view.Browser!;
                    Assert(browser.ZoomFactor < 1, "Narrow Gemini pane was not fitted");
                    using var preview = File.Create(Path.Combine(output, "gemini-dual-page.png"));
                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
                    string viewport = await browser.ExecuteScriptAsync("JSON.stringify({width:innerWidth,height:innerHeight,bodyHeight:document.body.scrollHeight,origin:location.origin,ready:document.readyState})");
                    File.WriteAllText(Path.Combine(output, "gemini-viewport.json"), viewport);
                    window.SetLayout(1);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(browser.ZoomFactor == 1, "Full-width Gemini did not restore 100% zoom");
                });
            }
            var report = new { failures, startupMs, nativeMemoryBytes, runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(), checks = results };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { failures++; File.WriteAllText(Path.Combine(output, "fatal.txt"), ex.ToString()); }
        finally { Environment.ExitCode = failures == 0 ? 0 : 1; window.Quit(); }
    }

    private static async Task Wait(Func<bool> check, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!check()) { if (watch.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException(label); await Task.Delay(100); }
    }
    private static async Task WaitScript(BrowserTabView view, string expression, string expected)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            string value = await view.Browser!.ExecuteScriptAsync(expression);
            if (value.Contains(expected, StringComparison.Ordinal)) return;
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(expression + " returned " + value);
            await Task.Delay(100);
        }
    }
    private static async Task Snapshot(Window window, string path)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class FixtureServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new();
        internal string Url { get; }
        internal FixtureServer()
        {
            _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                    _ = Respond(client);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        private static async Task Respond(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var line = await reader.ReadLineAsync(); bool popup = line?.Contains("/popup", StringComparison.Ordinal) == true;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    string html = popup
                        ? "<title>Fixture popup</title><script>window.opener.postMessage(document.cookie,'*'); setTimeout(()=>window.close(),200);</script>Popup fixture"
                        : "<!doctype html><meta charset='utf-8'><title>Bingyu fixture</title><style>body{font:20px system-ui;padding:50px;background:#f6f5f2;color:#272b2a}input{padding:14px;border:1px solid #bbb}</style><h1>Bingyu integration fixture</h1><p>This local page tests independent DOM state and shared storage.</p><input aria-label='Independent page state'>";
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header); await stream.WriteAsync(body);
                }
                catch (IOException) { }
            }
        }
        public void Dispose() { _cancel.Cancel(); _listener.Stop(); _cancel.Dispose(); }
    }
}
