using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Draftly.Services;

public enum ThemeMode { System, Light, Dark }
public enum InterfaceStyle { Ribbon, Classic }
public enum WrapMode { NoWrap, WrapToWindow, WrapToRuler }
public enum MeasureUnit { Inches, Centimeters, Points, Picas }
public enum TxtEncoding { Utf8, Utf8Bom, Ansi, Utf16 }
public enum DefaultSaveFormat { Rtf, Txt }

/// <summary>All user preferences. Saved as JSON in %APPDATA%\Draftly\settings.json.</summary>
public sealed class AppSettings
{
    // ---- Appearance ----
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    /// <summary>Ribbon (Home/View tabs) or Classic (menu bar + toolbars).</summary>
    public InterfaceStyle Interface { get; set; } = InterfaceStyle.Ribbon;
    public string Accent { get; set; } = "Indigo";
    public bool DarkPageInDarkMode { get; set; } = true;
    public bool ShowRibbonLabels { get; set; } = true;
    public double UiScale { get; set; } = 1.0;

    // ---- Editor ----
    public string DefaultFontFamily { get; set; } = "Calibri";
    public double DefaultFontSize { get; set; } = 11;
    public WrapMode WordWrap { get; set; } = WrapMode.WrapToRuler;
    public bool ShowRuler { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool ShowFormattingMarks { get; set; } = false;
    public bool SpellCheck { get; set; } = true;
    public int DefaultZoom { get; set; } = 100;
    public MeasureUnit Units { get; set; } = MeasureUnit.Inches;
    public bool SmoothScrolling { get; set; } = true;
    public bool SelectWholeWords { get; set; } = false;
    public bool HighlightCurrentLine { get; set; } = false;
    /// <summary>Line numbers in the page's left margin (on screen).</summary>
    public bool ShowLineNumbers { get; set; } = false;
    /// <summary>Typing "# ", "## ", "- ", "1. ", "> " at the start of a line applies a heading, list or quote.</summary>
    public bool MarkdownShortcuts { get; set; } = true;

    // ---- Tabs & sessions (like Windows 11 Notepad) ----
    /// <summary>Open several documents as tabs in one window. Off = one document at a time, like WordPad.</summary>
    public bool UseTabs { get; set; } = true;
    /// <summary>Files opened from File Explorer go to a new tab in the window that's already open.</summary>
    public bool OpenInExistingWindow { get; set; } = true;
    /// <summary>Reopen the tabs that were open when Draftly was last closed.</summary>
    public bool RestoreTabs { get; set; } = true;
    /// <summary>Close without asking; unsaved changes are kept and come back next time (Notepad style).</summary>
    public bool KeepUnsavedOnClose { get; set; } = false;
    /// <summary>Offer once to make Draftly the default app for .rtf files.</summary>
    public bool AskDefaultApp { get; set; } = true;

    // ---- Files ----
    public DefaultSaveFormat SaveFormat { get; set; } = DefaultSaveFormat.Rtf;
    public TxtEncoding TextEncoding { get; set; } = TxtEncoding.Utf8;
    public bool AutoRecovery { get; set; } = true;
    public int AutoRecoveryMinutes { get; set; } = 2;
    public bool KeepBackupOnSave { get; set; } = false;
    public int RecentFilesMax { get; set; } = 10;
    public bool ReopenLastDocument { get; set; } = false;
    public List<string> RecentFiles { get; set; } = new();
    public string LastFolder { get; set; } = "";

    // ---- Page (defaults for new documents) ----
    public string PaperSize { get; set; } = "Letter";
    public bool Landscape { get; set; } = false;
    public double MarginLeftIn { get; set; } = 1.25;
    public double MarginRightIn { get; set; } = 1.25;
    public double MarginTopIn { get; set; } = 1.0;
    public double MarginBottomIn { get; set; } = 1.0;
    public bool PrintPageNumbers { get; set; } = true;

    // ---- Window ----
    public bool RememberWindow { get; set; } = true;
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 1180;
    public double WinHeight { get; set; } = 780;
    public bool WinMaximized { get; set; } = false;
    public string LastSessionFile { get; set; } = "";
}

public static class SettingsService
{
    /// <summary>%APPDATA%\Draftly. The self-test points this at a temporary folder.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Draftly");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static event Action Changed;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s != null) Current = s;
            }
        }
        catch
        {
            // Corrupt settings file: keep defaults, but don't lose the old file.
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            Current = new AppSettings();
        }
        Sanitize(Current);
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, FilePath, true);
        }
        catch { /* settings are best-effort */ }
    }

    /// <summary>Save and tell the open window to re-apply settings.</summary>
    public static void Commit()
    {
        Sanitize(Current);
        Save();
        Changed?.Invoke();
    }

    public static void Reset()
    {
        var recent = Current.RecentFiles;
        Current = new AppSettings { RecentFiles = recent };
        Commit();
    }

    public static void AddRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var list = Current.RecentFiles;
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        while (list.Count > Math.Max(0, Current.RecentFilesMax)) list.RemoveAt(list.Count - 1);
        Save();
    }

    private static void Sanitize(AppSettings s)
    {
        s.RecentFiles ??= new();
        s.DefaultZoom = Math.Clamp(s.DefaultZoom, 10, 500);
        s.DefaultFontSize = Math.Clamp(s.DefaultFontSize, 1, 1638);
        s.AutoRecoveryMinutes = Math.Clamp(s.AutoRecoveryMinutes, 1, 60);
        s.RecentFilesMax = Math.Clamp(s.RecentFilesMax, 0, 30);
        s.UiScale = Math.Clamp(s.UiScale, 0.8, 1.5);
        if (string.IsNullOrWhiteSpace(s.DefaultFontFamily)) s.DefaultFontFamily = "Calibri";
        foreach (var m in new[] { s.MarginLeftIn, s.MarginRightIn, s.MarginTopIn, s.MarginBottomIn })
            if (double.IsNaN(m) || m < 0 || m > 5) { s.MarginLeftIn = s.MarginRightIn = 1.25; s.MarginTopIn = s.MarginBottomIn = 1; break; }
        if (s.WinWidth < 600 || double.IsNaN(s.WinWidth)) s.WinWidth = 1180;
        if (s.WinHeight < 400 || double.IsNaN(s.WinHeight)) s.WinHeight = 780;
    }
}
