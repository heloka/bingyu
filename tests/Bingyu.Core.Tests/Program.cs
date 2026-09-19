using Bingyu.Core;

int passed = 0;
int failed = 0;
void Test(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Invariants(Workspace w)
{
    Check(w.Slots.Count is >= 1 and <= 4);
    Check(w.FocusedSlot >= 0 && w.FocusedSlot < w.Slots.Count);
    var visible = w.Slots.Where(s => s != null).ToList();
    Check(visible.Distinct().Count() == visible.Count, "A browser cannot appear in two slots");
    Check(visible.All(id => w.Tabs.Any(t => t.Id == id)), "Visible tab must exist");
}
var sites = SiteDefinition.Defaults();
Test("Multi-open creates independent tab IDs with the same provider", () =>
{
    var w = new Workspace(); var a = w.Open(sites[0]); var b = w.Open(sites[0]);
    Check(a.Id != b.Id && a.SiteId == b.SiteId && w.Tabs.Count == 2);
});
Test("Clicking a visible tab focuses its existing pane without duplicating it", () =>
{
    var w = new Workspace(); var a = w.Open(sites[0]); w.SetLayout(2); w.SelectSlot(1); var b = w.Open(sites[1]);
    w.FocusTab(a.Id); Check(w.FocusedSlot == 0 && w.Slots[1] == b.Id); Invariants(w);
});
Test("Reducing the split count preserves the focused tab and all hidden tabs", () =>
{
    var w = new Workspace(); w.SetLayout(4);
    for (int i = 0; i < 4; i++) { w.SelectSlot(i); w.Open(sites[i]); }
    var active = w.ActiveTab!.Id; w.SetLayout(1);
    Check(w.ActiveTab?.Id == active && w.Tabs.Count == 4); Invariants(w);
});
Test("Increasing the split count reuses existing hidden tabs", () =>
{
    var w = new Workspace(); w.Open(sites[0]); w.Open(sites[1]); w.Open(sites[2]); w.SetLayout(3);
    Check(w.Slots.All(s => s != null) && w.Tabs.Count == 3); Invariants(w);
});
Test("Closing a visible tab chooses an unused remaining tab", () =>
{
    var w = new Workspace(); var a = w.Open(sites[0]); var b = w.Open(sites[1]); var c = w.Open(sites[2]);
    w.SetLayout(2); w.Close(c.Id); Check(w.Tabs.Count == 2 && w.Slots.Contains(a.Id) && w.Slots.Contains(b.Id)); Invariants(w);
});
Test("Expand and restore do not change the underlying split layout", () =>
{
    var w = new Workspace(); w.SetLayout(4); w.SelectSlot(2); w.Open(sites[0]); var slots = w.Slots.ToArray();
    w.ToggleExpand(2); Check(w.ExpandedSlot == 2); w.ToggleExpand(2); Check(w.ExpandedSlot == null && w.Slots.SequenceEqual(slots));
});
Test("Closing all tabs leaves valid empty slots", () =>
{
    var w = new Workspace(); var a = w.Open(sites[0]); w.SetLayout(4); w.Close(a.Id);
    Check(w.Slots.All(s => s == null)); Invariants(w);
});
Test("Invalid split count is rejected", () =>
{
    foreach (int count in new[] { 0, 5, -1 })
    {
        try { new Workspace().SetLayout(count); throw new Exception("Accepted " + count); }
        catch (ArgumentOutOfRangeException) { }
    }
});
Test("Unsafe and ambiguous URLs are rejected", () =>
{
    foreach (var url in new[] { "javascript:alert(1)", "file:///C:/windows", "data:text/html,hello", "ms-settings://", "https://user:pass@example.com", "https://", "", "hello world", "about:blank" })
        Check(!UrlPolicy.TryNormalize(url, out _), "Accepted " + url);
});
Test("HTTPS default, international hosts, local development URLs, and query strings work", () =>
{
    foreach (var url in new[] { "chatgpt.com", "https://例子.测试/路径", "http://127.0.0.1:1234/test", "https://example.com/?q=a%20b", " https://claude.ai/ " })
        Check(UrlPolicy.TryNormalize(url, out _), "Rejected " + url);
});
Test("Corrupt workspace references are repaired without opening unsafe URLs", () =>
{
    var w = new Workspace(); var a = w.Open(sites[0]); w.Tabs.Add(new TabState { Url = "file:///secret" });
    w.Slots = [a.Id, a.Id, Guid.NewGuid()]; w.FocusedSlot = 99; w.Repair();
    Check(w.Tabs.Count == 1 && w.Slots[1] == null && w.Slots[2] == null); Invariants(w);
});
string temp = Path.Combine(Path.GetTempPath(), "Bingyu.Core.Tests-" + Guid.NewGuid().ToString("N"));
try
{
    Test("Atomic settings save restores custom sites, tab display modes, and split proportions", () =>
    {
        var store = new StateStore(temp); var state = new SavedState();
        var tab = state.Workspace.Open(sites[0]); tab.Sleeping = true; tab.Zen = true; tab.ZoomFactor = .9; state.Workspace.SetLayout(3);
        state.Preferences.HideTopBars = true;
        state.Preferences.SplitRatios["3"] = [.4, .6];
        state.Preferences.Sites.Add(new("custom-1", "Example", "https://example.com", "E", "#888888", "test"));
        store.Save(state); var loaded = store.Load();
        Check(loaded.Workspace.Tabs[0].Sleeping && loaded.Workspace.Tabs[0].Zen && loaded.Workspace.Tabs[0].ZoomFactor == .9 && loaded.Preferences.HideTopBars && loaded.Workspace.Slots.Count == 3 && loaded.Preferences.Sites.Count == 8);
        Check(loaded.Preferences.SplitRatios["3"][0] == .4);
    });
    Test("A damaged primary settings file recovers the last good backup", () =>
    {
        var store = new StateStore(temp); var state = new SavedState(); state.Preferences.Hotkey = "Ctrl+Shift+Q";
        store.Save(state); store.Save(state); File.WriteAllText(store.FilePath, "{ broken");
        var loaded = store.Load(); Check(loaded.Preferences.Hotkey == "Ctrl+Shift+Q" && store.RecoveryMessage != null);
    });
    Test("Damaged settings with no usable backup preserve forensic copy and use defaults", () =>
    {
        var dir = Path.Combine(temp, "damaged"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "settings.json"), "garbage");
        var store = new StateStore(dir); Check(store.Load().Preferences.Sites.Count == 7 && File.Exists(store.FilePath + ".damaged"));
    });
    Test("Existing site lists gain 智谱清言 without losing order or custom sites", () =>
    {
        var dir = Path.Combine(temp, "migration");
        var store = new StateStore(dir); var state = new SavedState();
        state.Preferences.Sites.RemoveAll(s => s.Id == "zhipu");
        state.Preferences.Sites.Add(new("custom-one", "Example", "https://example.com", "E", "#888888", "test"));
        store.Save(state);
        var loaded = store.Load();
        Check(loaded.Preferences.Sites.Count == 8 && loaded.Preferences.Sites[^1].Id == "zhipu");
        Check(loaded.Preferences.Sites[^2].Id == "custom-one");
    });
}
finally
{
    var resolved = Path.GetFullPath(temp);
    var allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("Bingyu.Core.Tests-", StringComparison.Ordinal) && Directory.Exists(resolved))
        Directory.Delete(resolved, true);
}
Test("Randomized tab operations maintain workspace invariants (10,000 operations)", () =>
{
    var w = new Workspace(); var random = new Random(1209);
    for (int i = 0; i < 10_000; i++)
    {
        switch (random.Next(6))
        {
            case 0: if (w.Tabs.Count < 100) w.Open(sites[random.Next(sites.Count)]); break;
            case 1: w.SetLayout(random.Next(1, 5)); break;
            case 2: if (w.Tabs.Count > 0) w.FocusTab(w.Tabs[random.Next(w.Tabs.Count)].Id); break;
            case 3: if (w.Tabs.Count > 0) w.Close(w.Tabs[random.Next(w.Tabs.Count)].Id); break;
            case 4: w.SelectSlot(random.Next(w.Slots.Count)); break;
            case 5: w.ToggleExpand(random.Next(w.Slots.Count)); break;
        }
        Invariants(w);
    }
});
Console.WriteLine($"\n{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
