using Microsoft.Win32;

namespace Qiye;

internal static class Theme
{
    public static bool IsDark { get; private set; }
    public static void Apply(string choice)
    {
        bool light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (System.Security.SecurityException) { }
        IsDark = choice == "Dark" || (choice != "Light" && !light);
        var values = IsDark
            ? new[] { "#1F201F", "#262827", "#202124", "#202020", "#333634", "#383B38", "#555B56", "#ECEEEC", "#A6ADA7", "#CDD6CF", "#343A35" }
            : new[] { "#F7F7F5", "#FFFFFF", "#FAF9F9", "#FCFCFC", "#F0F0EE", "#E8E8E5", "#D5D7D3", "#242624", "#7C817D", "#454E49", "#E9ECE9" };
        string[] names = ["Bg", "Surface", "GeminiSurface", "ChatGptSurface", "Raised", "Stroke", "FocusedStroke", "Text", "Muted", "Accent", "AccentSoft"];
        for (int i = 0; i < names.Length; i++) Application.Current.Resources[names[i]] = Brush(values[i]);
    }
    public static SolidColorBrush Brush(string color)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch (FormatException) { return new SolidColorBrush(Colors.SlateGray); }
    }
    public static Brush Resource(string name) => (Brush)Application.Current.FindResource(name);
}
