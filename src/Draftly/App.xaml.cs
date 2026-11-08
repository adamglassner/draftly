using System.IO;
using System.Windows;
using System.Windows.Threading;
using Draftly.Services;

namespace Draftly;

public partial class App : Application
{
    /// <summary>Set by "--self-test &lt;output.txt&gt; [screenshot folder]" (used by the build to test Draftly).</summary>
    public static string SelfTestOutput { get; private set; }
    public static string SelfTestShots { get; private set; }
    public static bool IsSelfTest => SelfTestOutput != null;
    internal static string MainWindowTestName = "(starting)";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandled;

        int st = Array.FindIndex(e.Args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        if (st >= 0)
        {
            SelfTestOutput = st + 1 < e.Args.Length ? Path.GetFullPath(e.Args[st + 1]) : Path.Combine(Path.GetTempPath(), "draftly-selftest.txt");
            SelfTestShots = st + 2 < e.Args.Length ? Path.GetFullPath(e.Args[st + 2]) : null;
            // Never touch the real settings or recovery files while testing.
            var temp = Path.Combine(Path.GetTempPath(), "DraftlySelfTest-" + Guid.NewGuid().ToString("N")[..8]);
            SettingsService.Folder = Path.Combine(temp, "settings");
            RecoveryService.Folder = Path.Combine(temp, "recovery");
        }

        SettingsService.Load();
        ThemeManager.Apply(SettingsService.Current);
        ApplyUiPreferences();
        SettingsService.Changed += () =>
        {
            ThemeManager.Apply(SettingsService.Current);
            ApplyUiPreferences();
        };

        string fileToOpen = null;
        foreach (var arg in e.Args)
        {
            if (!arg.StartsWith("/") && !arg.StartsWith("-") && File.Exists(arg)) { fileToOpen = Path.GetFullPath(arg); break; }
        }

        var win = new MainWindow(fileToOpen);
        MainWindow = win;
        win.Show();
    }

    public static void ApplyUiPreferences()
    {
        var s = SettingsService.Current;
        Current.Resources["RibbonLabelVisibility"] = s.ShowRibbonLabels ? Visibility.Visible : Visibility.Collapsed;
        Current.Resources["UiFontSize"] = 14.0 * s.UiScale;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // WPF reports a missing style/brush lookup through this event even though it recovers by itself.
        // Never bother the user with that; just note it.
        if (e.Exception is ResourceReferenceKeyNotFoundException rk)
        {
            try
            {
                Directory.CreateDirectory(SettingsService.Folder);
                File.AppendAllText(Path.Combine(SettingsService.Folder, "resource-warnings.log"), $"[{DateTime.Now:u}] {rk.Key}\r\n{Environment.StackTrace}\r\n\r\n");
                if (IsSelfTest) File.AppendAllText(SelfTestOutput, $"FAIL missing resource '{rk.Key}' during \"{MainWindowTestName}\": {Environment.StackTrace.Replace("\r\n", " | ")}\r\n");
            }
            catch { }
            e.Handled = true;
            return;
        }
        if (IsSelfTest)
        {
            try { File.AppendAllText(SelfTestOutput, $"FAIL unhandled exception during \"{MainWindowTestName}\": {e.Exception}\r\n"); } catch { }
            e.Handled = true;
            Environment.Exit(99);
            return;
        }
        // Try to save the user's work before reporting the problem.
        try { (MainWindow as MainWindow)?.EmergencySave(); } catch { }
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            File.AppendAllText(Path.Combine(SettingsService.Folder, "error.log"),
                $"[{DateTime.Now:u}] {e.Exception}\r\n\r\n");
        }
        catch { }
        Dialogs.MessageDialog.Show(MainWindow, "Something went wrong",
            "Draftly hit an unexpected error. Your document was saved to the recovery folder and will be offered next time you start Draftly.\n\n" +
            e.Exception.Message, Dialogs.MessageDialog.Kind.Error);
        e.Handled = true;
    }
}
