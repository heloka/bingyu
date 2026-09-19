using System.Text.Json;

namespace Bingyu.Core;

public sealed class StateStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    public string FilePath { get; } = Path.Combine(directory, "settings.json");
    public string? RecoveryMessage { get; private set; }

    public SavedState Load()
    {
        if (!File.Exists(FilePath)) return new();
        foreach (string path in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path), Options);
                if (state?.Preferences == null || state.Workspace == null) continue;
                state.Workspace.Repair();
                state.Preferences.Sites ??= SiteDefinition.Defaults();
                state.Preferences.Sites = state.Preferences.Sites.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id) && UrlPolicy.TryNormalize(s.Url, out _)).DistinctBy(s => s.Id).ToList();
                foreach (var builtIn in SiteDefinition.Defaults())
                    if (!state.Preferences.Sites.Any(s => s.Id == builtIn.Id)) state.Preferences.Sites.Add(builtIn);
                state.Preferences.SplitRatios ??= [];
                state.Preferences.Hotkey ??= "Alt+Space";
                if (path.EndsWith(".bak", StringComparison.Ordinal)) RecoveryMessage = "设置文件异常，已从备份恢复。";
                return state;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        RecoveryMessage = "设置文件无法读取，已使用默认设置；原文件保留为 .damaged。";
        try { File.Copy(FilePath, FilePath + ".damaged", true); } catch (IOException) { }
        return new();
    }

    public void Save(SavedState state)
    {
        Directory.CreateDirectory(directory);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
    }
}
