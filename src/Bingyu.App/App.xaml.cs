using System.Threading;
using System.Security.Cryptography;

namespace Bingyu;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _wake;
    private RegisteredWaitHandle? _wakeRegistration;
    private const string ProductId = "Bingyu";
    private const string LegacyProductId = "Qiye";
    // Existing installations keep their cookies and settings in the legacy profile.
    // Fresh installations use the current product name.
    internal static string DataDirectory { get; private set; } = ResolveDefaultDataDirectory();
    internal static bool SmokeTest { get; private set; }
    internal static bool GeminiCheck { get; private set; }
    internal static string? SmokeOutput { get; private set; }
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length) DataDirectory = Path.GetFullPath(e.Args[dataIndex + 1]);
        int smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
        SmokeTest = smokeIndex >= 0;
        GeminiCheck = Array.IndexOf(e.Args, "--gemini-check") >= 0;
        if (SmokeTest && smokeIndex + 1 < e.Args.Length) SmokeOutput = Path.GetFullPath(e.Args[smokeIndex + 1]);
        Directory.CreateDirectory(DataDirectory);
        var identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserName + DataDirectory)))[..20];
        var instanceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(DataDirectory))
            .Equals(LegacyProductId, StringComparison.OrdinalIgnoreCase) ? LegacyProductId : ProductId;
        _mutex = new Mutex(true, @"Local\" + instanceName + "." + identity, out _ownsMutex);
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + instanceName + ".Wake." + identity);
        if (!_ownsMutex) { _wake.Set(); Shutdown(); return; }
        var store = new StateStore(DataDirectory);
        var state = store.Load();
        Theme.Apply(state.Preferences.Theme);
        if (!state.Preferences.RestoreSession) state.Workspace = new Workspace();
        var window = new MainWindow(store, state);
        MainWindow = window;
        _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => Dispatcher.BeginInvoke(() => window.RestoreWindow()), null, Timeout.Infinite, false);
        if (SmokeTest) window.Loaded += async (_, _) => await SmokeChecks.RunAsync(window, SmokeOutput ?? Path.Combine(DataDirectory, "smoke"));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wakeRegistration?.Unregister(null);
        _wake?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    internal static void Log(Exception exception)
    {
        try { File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), $"{DateTimeOffset.Now:O} {exception}\n"); }
        catch (IOException) { }
    }

    private static string ResolveDefaultDataDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string current = Path.Combine(root, ProductId);
        string legacy = Path.Combine(root, LegacyProductId);
        return Directory.Exists(current) || !Directory.Exists(legacy) ? current : legacy;
    }
}
