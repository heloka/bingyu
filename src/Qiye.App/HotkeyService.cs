using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Qiye;

internal sealed class HotkeyService(Window window, Action toggle) : IDisposable
{
    private HwndSource? _source;
    private int _id = 4200;
    private bool _registered;
    public string? Current { get; private set; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public bool Register(string text, out string error)
    {
        error = "";
        if (Current == text) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) { error = "请使用组合键，例如 Alt+Space 或 Ctrl+Shift+Q。"; return false; }
        uint mods = 0;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 2; break;
                case "alt": mods |= 1; break;
                case "shift": mods |= 4; break;
                default: error = "修饰键支持 Ctrl、Alt 和 Shift。"; return false;
            }
        }
        if ((mods & 3) == 0) { error = "快捷键至少需要包含 Ctrl 或 Alt。"; return false; }
        Key key;
        try { key = (Key)(new KeyConverter().ConvertFromString(parts[^1]) ?? Key.None); }
        catch (NotSupportedException) { error = "无法识别按键名称。"; return false; }
        if (key is Key.None or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift) { error = "请选择普通按键。"; return false; }
        _source ??= HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (_source == null) { error = "窗口尚未准备好。"; return false; }
        int next = _id + 1;
        if (!RegisterHotKey(_source.Handle, next, mods | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(key)))
        { error = "这个快捷键已被其他程序占用，请换一个。"; return false; }
        if (_registered) UnregisterHotKey(_source.Handle, _id);
        else _source.AddHook(Hook);
        _id = next; _registered = true; Current = text;
        return true;
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == _id) { toggle(); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_source == null) return;
        if (_registered) UnregisterHotKey(_source.Handle, _id);
        _source.RemoveHook(Hook);
    }
}
