namespace Bingyu.Core;

public sealed record SiteDefinition(string Id, string Name, string Url, string Mark, string Color, string Description)
{
    public static List<SiteDefinition> Defaults() =>
    [
        new("chatgpt", "ChatGPT", "https://chatgpt.com/", "◎", "#25866F", "想法、写作与日常灵感"),
        new("claude", "Claude", "https://claude.ai/", "✳", "#C87957", "深入思考，让表达更进一步"),
        new("gemini", "Gemini", "https://gemini.google.com/", "✦", "#6081DD", "探索知识，连接更多可能"),
        new("deepseek", "DeepSeek", "https://chat.deepseek.com/", "≈", "#527BE1", "推理、编程与问题求解"),
        new("doubao", "豆包", "https://www.doubao.com/chat/", "豆", "#529CAD", "随时聊聊，轻松获得帮助"),
        new("qwen", "千问", "https://www.qianwen.com/", "Q", "#8C70CC", "工作学习，多一位好帮手"),
        new("zhipu", "智谱清言", "https://chatglm.cn/", "智", "#3654BD", "对话、创作与智能助手")
    ];
}

public sealed class TabState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SiteId { get; set; } = "";
    public string Title { get; set; } = "新页面";
    public string Url { get; set; } = "";
    public bool Sleeping { get; set; }
    public bool Zen { get; set; }
    public double? ZoomFactor { get; set; }
}

public sealed class Preferences
{
    public string Theme { get; set; } = "System";
    public string Hotkey { get; set; } = "Alt+Space";
    public bool CloseToTray { get; set; } = true;
    public bool RestoreSession { get; set; } = true;
    public bool HideTopBars { get; set; }
    public double Width { get; set; } = 1360;
    public double Height { get; set; } = 900;
    public bool Maximized { get; set; }
    public List<SiteDefinition> Sites { get; set; } = SiteDefinition.Defaults();
    public Dictionary<string, double[]> SplitRatios { get; set; } = [];
}

public sealed class SavedState
{
    public int Version { get; set; } = 1;
    public Preferences Preferences { get; set; } = new();
    public Workspace Workspace { get; set; } = new();
}

public static class UrlPolicy
{
    public static bool TryNormalize(string? input, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        if (input.Any(char.IsWhiteSpace)) return false;
        if (!input.Contains("://") && !input.Contains(':')) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        url = uri.AbsoluteUri;
        return true;
    }
}
