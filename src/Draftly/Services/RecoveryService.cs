using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace Draftly.Services;

/// <summary>
/// Auto-recovery: while a document has unsaved changes, a copy is written every few minutes to
/// %LOCALAPPDATA%\Draftly\Recovery. It's deleted on a clean save or close. If Draftly crashes or
/// the PC loses power, the copy is offered the next time Draftly starts.
/// </summary>
public static class RecoveryService
{
    public sealed class Info
    {
        public int ProcessId { get; set; }
        public DateTime ProcessStart { get; set; }
        public string OriginalPath { get; set; }
        public string DisplayName { get; set; }
        public DateTime SavedAt { get; set; }
    }

    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Draftly", "Recovery");

    private static readonly string Id = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
    private static string RtfPath => Path.Combine(Folder, Id + ".rtf");
    private static string InfoPath => Path.Combine(Folder, Id + ".json");

    public static void Write(FlowDocument doc, string originalPath, string displayName)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = RtfPath + ".tmp";
            string rtf = null;
            DocumentService.WithBlackText(doc, () => rtf = RtfBaseline.ToRtf(new TextRange(doc.ContentStart, doc.ContentEnd), doc));
            File.WriteAllBytes(tmp, System.Text.Encoding.Latin1.GetBytes(rtf));
            File.Move(tmp, RtfPath, true);
            var info = new Info
            {
                ProcessId = Environment.ProcessId,
                ProcessStart = Process.GetCurrentProcess().StartTime,
                OriginalPath = originalPath,
                DisplayName = displayName,
                SavedAt = DateTime.Now,
            };
            File.WriteAllText(InfoPath, JsonSerializer.Serialize(info));
        }
        catch { /* recovery is best-effort */ }
    }

    public static void Clear()
    {
        try { File.Delete(RtfPath); } catch { }
        try { File.Delete(InfoPath); } catch { }
    }

    /// <summary>Recovery files left behind by Draftly windows that are no longer running.</summary>
    public static List<(string RtfPath, Info Info)> FindOrphans()
    {
        var result = new List<(string, Info)>();
        try
        {
            if (!Directory.Exists(Folder)) return result;
            foreach (var json in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var info = JsonSerializer.Deserialize<Info>(File.ReadAllText(json));
                    var rtf = Path.ChangeExtension(json, ".rtf");
                    if (info == null || !File.Exists(rtf)) { File.Delete(json); continue; }
                    if (IsAlive(info)) continue;
                    result.Add((rtf, info));
                }
                catch { }
            }
        }
        catch { }
        return result.OrderByDescending(r => ((Info)r.Item2).SavedAt).ToList();
    }

    public static void Discard(string rtfPath)
    {
        try { File.Delete(rtfPath); } catch { }
        try { File.Delete(Path.ChangeExtension(rtfPath, ".json")); } catch { }
    }

    private static bool IsAlive(Info info)
    {
        try
        {
            var p = Process.GetProcessById(info.ProcessId);
            return Math.Abs((p.StartTime - info.ProcessStart).TotalSeconds) < 2 && !p.HasExited;
        }
        catch { return false; }
    }
}
