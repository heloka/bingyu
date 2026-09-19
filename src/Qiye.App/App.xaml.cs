using System.Threading;
using System.Security.Cryptography;

namespace Qiye;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _wake;
    private RegisteredWaitHandle? _wakeRegistration;
    // Keep the original profile path and single-instance identity across the product rename.
    // Existing cookies, custom icons and workspace settings then work without migration.
    internal static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qiye");
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
        _mutex = new Mutex(true, @"Local\Qiye." + identity, out _ownsMutex);
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Qiye.Wake." + identity);
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
}
