using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hamyar;

public partial class App : Application
{
    static Mutex _mutex;
    static bool _started;
    static bool _showingError;
    static int _errorCount;

    // Created when a start begins and deleted once the app has been alive for a few seconds.
    // If it is still there at the next start, the previous start hung or crashed -> safe mode.
    static string MarkerPath => System.IO.Path.Combine(Db.Dir, "startup.marker");

    public App()
    {
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("FATAL: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, e) => { Log.Write("Task: " + e.Exception); e.SetObserved(); };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one instance (otherwise every alarm would fire twice)
        _mutex = new Mutex(true, "Hamyar.SingleInstance.Mutex", out bool created);
        if (!created)
        {
            MessageBox.Show(
                "Hamyar is already running.\nLook for it in the taskbar, or end Hamyar.exe in Task Manager.\n\n" +
                "همیار از قبل در حال اجراست. آن را در نوار وظیفه پیدا کنید یا Hamyar.exe را در Task Manager ببندید.",
                "Hamyar", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            Directory.CreateDirectory(Db.Dir);
            bool crashedLastTime = File.Exists(MarkerPath);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("o"));

            SmoothScroll.Install();
            Db.Init();
            try { Backups.AutoIfNeeded(); } catch (Exception ex) { Log.Write("Auto backup: " + ex.Message); }
            if (crashedLastTime) Cfg.SafeMode = true;
            Loc.I.SetLang(Cfg.Lang);
            Resources["AppFont"] = LoadFont();
            ThemeManager.Apply(Cfg.Theme, Cfg.Accent, Cfg.Backdrop);

            var w = new MainWindow();
            if (e.Args.Contains("/minimized")) w.WindowState = WindowState.Minimized;
            w.Show();

            // alive for 4 seconds = the start worked (if the UI is frozen this timer never fires)
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (s, a) => { t.Stop(); MarkStarted(); };
            t.Start();

            if (crashedLastTime)
                w.Loaded += (s, a) => MessageBox.Show(Loc.Get("safe_notice"), "Hamyar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            ShowError("Hamyar could not start.", ex);
            Shutdown(1);
        }
    }

    public static void MarkStarted()
    {
        _started = true;
        try { File.Delete(MarkerPath); } catch { }
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("UI exception: " + e.Exception);
        e.Handled = true;
        if (!_started)
        {
            ShowError("Hamyar hit an error while starting.", e.Exception);
            Shutdown(1);
            return;
        }
        if (_showingError || ++_errorCount > 5) return;   // never flood the user with dialogs
        _showingError = true;
        try { ShowError("Something went wrong (the app keeps running).", e.Exception); }
        finally { _showingError = false; }
    }

    static void ShowError(string title, Exception ex)
    {
        try
        {
            MessageBox.Show(
                title + "\n\n" + ex.GetType().Name + ": " + ex.Message + "\n\nDetails were saved to:\n" + Log.FilePath,
                "Hamyar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }

    /// <summary>
    /// Vazirmatn (SIL OFL 1.1), all weights. The .ttf files in the Fonts folder are EMBEDDED in the program (so they are also inside a
    /// published exe). At start they are copied to %LocalAppData%\TaskAlarm\fonts and WPF loads them from there, using the real family
    /// name read from the font itself; FontWeight then picks Thin ... Black. Falls back to Segoe UI if the fonts are missing (or in safe mode).
    /// </summary>
    static FontFamily LoadFont()
    {
        const string fallback = ", Segoe UI, Segoe UI Emoji";
        if (Cfg.SafeMode) return new FontFamily("Segoe UI, Segoe UI Emoji");
        try
        {
            var dir = System.IO.Path.Combine(Cfg.LocalDir, "fonts");
            Directory.CreateDirectory(dir);
            try { File.Delete(System.IO.Path.Combine(dir, "iransansx.ttf")); } catch { }     // the old font of earlier versions

            // 1) embedded inside the program
            var asm = typeof(App).Assembly;
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("Fonts/", StringComparison.OrdinalIgnoreCase) || !res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                var target = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(res));
                using var s = asm.GetManifestResourceStream(res);
                if (s != null && (!File.Exists(target) || new FileInfo(target).Length != s.Length))
                {
                    using var fs = File.Create(target);
                    s.CopyTo(fs);
                }
            }
            // 2) or simply lying in a Fonts folder next to the exe
            var beside = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts");
            if (Directory.Exists(beside))
                foreach (var f in Directory.GetFiles(beside, "*.ttf"))
                {
                    var target = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f));
                    if (!File.Exists(target)) File.Copy(f, target, true);
                }

            var regular = System.IO.Path.Combine(dir, "Vazirmatn-Regular.ttf");
            if (File.Exists(regular))
            {
                var gt = new GlyphTypeface(new Uri(regular));
                string name = null;
                foreach (var kv in gt.FamilyNames)
                {
                    name = kv.Value;
                    if (kv.Key.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) break;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    var baseUri = new Uri(dir + System.IO.Path.DirectorySeparatorChar);
                    return new FontFamily(baseUri, "./#" + name + fallback);
                }
            }
            Log.Write("Font: Vazirmatn not found (not embedded and not in a Fonts folder next to the exe) - using Segoe UI");
        }
        catch (Exception ex) { Log.Write("Font: " + ex.Message); }
        return new FontFamily("Segoe UI, Segoe UI Emoji");
    }
}
