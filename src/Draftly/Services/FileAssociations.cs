using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Draftly.Services;

/// <summary>
/// Registers Draftly with Windows for .rtf, .txt and .md files (for the current user, no admin needed),
/// and helps the user make it the default app.
///
/// Windows 10 and 11 deliberately don't let apps silently make themselves the default (the choice is
/// protected by a hash only Windows can write). So Draftly registers itself — after which it's listed
/// under "Open with" and in Settings › Apps › Default apps — and then opens that Settings page for
/// Draftly, where one click sets it.
/// </summary>
public static class FileAssociations
{
    public const string AppName = "Draftly";
    private const string CapabilitiesKey = @"Software\Bytehawk Studio\Draftly\Capabilities";

    public static readonly (string Ext, string ProgId, string Description)[] Types =
    {
        (".rtf", "Draftly.rtf", "Rich Text Document"),
        (".txt", "Draftly.txt", "Text Document"),
        (".md", "Draftly.md", "Markdown Document"),
    };

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;

    /// <summary>Registers Draftly for the current user. Safe to call repeatedly (also fixes a moved exe).</summary>
    public static void Register()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return;
        string open = $"\"{exe}\" \"%1\"";
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        foreach (var (ext, progId, desc) in Types)
        {
            using (var p = classes.CreateSubKey(progId))
            {
                p.SetValue("", desc);
                p.SetValue("FriendlyTypeName", desc);
                using (var icon = p.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
                using (var cmd = p.CreateSubKey(@"shell\open\command")) cmd.SetValue("", open);
            }
            using (var owp = classes.CreateSubKey(ext + @"\OpenWithProgids")) owp.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var app = classes.CreateSubKey(@"Applications\Draftly.exe"))
        {
            app.SetValue("FriendlyAppName", AppName);
            using (var cmd = app.CreateSubKey(@"shell\open\command")) cmd.SetValue("", open);
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var (ext, _, _) in Types) types.SetValue(ext, "");
            types.SetValue(".docx", "");
        }

        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "A modern rich text word processor");
            caps.SetValue("ApplicationIcon", $"\"{exe}\",0");
            using var fa = caps.CreateSubKey("FileAssociations");
            foreach (var (ext, progId, _) in Types) fa.SetValue(ext, progId);
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue(AppName, CapabilitiesKey);

        NotifyShell();
    }

    /// <summary>Removes everything <see cref="Register"/> added (used by the uninstaller).</summary>
    public static void Unregister()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
            if (classes != null)
            {
                foreach (var (ext, progId, _) in Types)
                {
                    try { classes.DeleteSubKeyTree(progId, false); } catch { }
                    try { using var owp = classes.OpenSubKey(ext + @"\OpenWithProgids", true); owp?.DeleteValue(progId, false); } catch { }
                }
                try { classes.DeleteSubKeyTree(@"Applications\Draftly.exe", false); } catch { }
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Bytehawk Studio\Draftly\Capabilities", false); } catch { }
            try { using var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true); reg?.DeleteValue(AppName, false); } catch { }
        }
        catch { }
        NotifyShell();
    }

    /// <summary>Is Draftly registered for this file type for the current user?</summary>
    public static bool IsRegistered(string ext)
    {
        var (_, progId, _) = Types.First(t => t.Ext == ext);
        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{progId}\shell\open\command");
        return cmd?.GetValue("") is string s && s.Contains(ExePath ?? "\0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Is Draftly the app Windows uses to open this file type?</summary>
    public static bool IsDefault(string ext)
    {
        var progId = Types.FirstOrDefault(t => t.Ext == ext).ProgId;
        foreach (var sub in new[] { "UserChoiceLatest", "UserChoice" })
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\{sub}");
                var choice = k?.GetValue("ProgId") as string;
                if (k is null || string.IsNullOrEmpty(choice))
                {
                    // Newer Windows 11 builds keep the value under UserChoiceLatest\ProgId
                    using var k2 = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\{sub}\ProgId");
                    choice = k2?.GetValue("ProgId") as string;
                }
                if (string.IsNullOrEmpty(choice)) continue;
                return choice.Equals(progId, StringComparison.OrdinalIgnoreCase) ||
                       choice.EndsWith("Draftly.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }
        // No user choice: check the per-user/machine class default
        try
        {
            using var k = Registry.ClassesRoot.OpenSubKey(ext);
            return (k?.GetValue("") as string)?.Equals(progId, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return false; }
    }

    /// <summary>Registers, then opens Windows Settings at Draftly's Default apps page.</summary>
    public static void OpenDefaultAppsSettings()
    {
        Register();
        foreach (var uri in new[] { $"ms-settings:defaultapps?registeredAppUser={AppName}", "ms-settings:defaultapps" })
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return; }
            catch { }
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell()
    {
        try { SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
    }
}
