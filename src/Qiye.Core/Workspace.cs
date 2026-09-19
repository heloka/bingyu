namespace Qiye.Core;

// A tab owns its browser. Slots only select which browsers are visible.
public sealed class Workspace
{
    public List<TabState> Tabs { get; set; } = [];
    public List<Guid?> Slots { get; set; } = [null];
    public int FocusedSlot { get; set; }
    public int? ExpandedSlot { get; set; }

    public TabState? ActiveTab => Tabs.FirstOrDefault(t => t.Id == Slots[FocusedSlot]);

    public TabState Open(SiteDefinition site, string? url = null)
    {
        if (!UrlPolicy.TryNormalize(url ?? site.Url, out var normalized)) throw new ArgumentException("请输入有效的 http 或 https 网址。");
        var tab = new TabState { SiteId = site.Id, Title = site.Name, Url = normalized };
        Tabs.Add(tab);
        Slots[FocusedSlot] = tab.Id;
        return tab;
    }

    public void FocusTab(Guid id)
    {
        if (!Tabs.Any(t => t.Id == id)) return;
        int existing = Slots.IndexOf(id);
        if (existing >= 0) FocusedSlot = existing;
        else Slots[FocusedSlot] = id;
        if (ExpandedSlot != null) ExpandedSlot = FocusedSlot;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= Slots.Count) return;
        FocusedSlot = index;
        if (ExpandedSlot != null) ExpandedSlot = index;
    }

    public void SetLayout(int count)
    {
        if (count is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(count));
        ExpandedSlot = null;
        if (Slots.Count > count)
        {
            Guid? active = Slots[FocusedSlot];
            Slots = Slots.Take(count).ToList();
            if (FocusedSlot >= count)
            {
                FocusedSlot = count - 1;
                Slots[FocusedSlot] = active;
            }
        }
        while (Slots.Count < count)
            Slots.Add(Tabs.FirstOrDefault(t => !Slots.Contains(t.Id))?.Id);
    }

    public void Close(Guid id)
    {
        Tabs.RemoveAll(t => t.Id == id);
        for (int i = 0; i < Slots.Count; i++)
            if (Slots[i] == id) Slots[i] = Tabs.LastOrDefault(t => !Slots.Contains(t.Id))?.Id;
    }

    public void ToggleExpand(int index)
    {
        SelectSlot(index);
        ExpandedSlot = ExpandedSlot == index ? null : index;
    }

    public void Repair()
    {
        Tabs ??= [];
        Tabs = Tabs.Where(t => t != null && UrlPolicy.TryNormalize(t.Url, out _)).DistinctBy(t => t.Id).Take(60).ToList();
        foreach (var tab in Tabs)
            if (tab.ZoomFactor is double zoom && (!double.IsFinite(zoom) || zoom < .5 || zoom > 2)) tab.ZoomFactor = null;
        Slots ??= [null];
        if (Slots.Count is < 1 or > 4) Slots = [null];
        var seen = new HashSet<Guid>();
        for (int i = 0; i < Slots.Count; i++)
            if (Slots[i] is Guid id && (!Tabs.Any(t => t.Id == id) || !seen.Add(id))) Slots[i] = null;
        FocusedSlot = Math.Clamp(FocusedSlot, 0, Slots.Count - 1);
        ExpandedSlot = null;
    }
}
