using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Draftly.Controls;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

/// <summary>Self-tests for the built-in updater and the print preview icon.</summary>
public partial class MainWindow
{
    internal async Task RunUpdateTestsAsync()
    {
        await Test("Updater reads GitHub releases", UpdaterParses);
        await Test("Updater checks downloads", UpdaterVerifies);
        await Test("Updater replaces a running Draftly.exe", UpdaterAppliesPortable);
        await Test("Update available bar and dialog", UpdaterUi);
        await Test("Update check against GitHub", UpdaterLiveCheck);
        await Test("Print preview icon", PrintPreviewIconWorks);
    }

    private static string FakeRelease(string tag, bool draft = false, string sha = "abc123") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": false,
          "html_url": "https://github.com/adamglassner/draftly/releases/tag/{{tag}}",
          "body": "## {{tag}}\n### Added\n- **Tabs** for documents\n- Line `numbers`\n",
          "assets": [
            { "name": "Draftly-9.9.9-Setup.exe", "size": 1000, "digest": "sha256:{{sha}}",
              "browser_download_url": "https://github.com/adamglassner/draftly/releases/download/{{tag}}/Draftly-9.9.9-Setup.exe" },
            { "name": "Draftly-9.9.9-win-x64.zip", "size": 2000,
              "browser_download_url": "https://github.com/adamglassner/draftly/releases/download/{{tag}}/Draftly-9.9.9-win-x64.zip" }
          ]
        }
        """;

    private Task UpdaterParses()
    {
        var info = UpdateService.Parse(FakeRelease("v99.1.2"));
        Assert(info != null && info.Version == new Version(99, 1, 2) && info.Display == "99.1.2", $"parsed {info?.Version}");
        Assert(info.Version > UpdateService.Current, "a newer version wasn't seen as newer");
        Assert(info.Setup?.Name == "Draftly-9.9.9-Setup.exe" && info.Setup.Sha256 == "abc123" && info.Setup.Size == 1000, "setup download not found");
        Assert(info.Zip?.Name == "Draftly-9.9.9-win-x64.zip" && info.Zip.Sha256 == null, "zip download not found");
        Assert(UpdateService.Parse(FakeRelease("v99.0.0", draft: true)) == null, "a draft release was offered");
        Assert(UpdateService.Parse(FakeRelease("v1.0.0")).Version < UpdateService.Current, "an older version was seen as newer");
        Assert(UpdateService.TryParseVersion("v1.2", out var v) && v == new Version(1, 2, 0), "v1.2 not understood");
        var notes = UpdateService.ShortNotes(info.Notes);
        Assert(notes.Contains("•  Tabs for documents") && !notes.Contains("**") && !notes.Contains("##"), $"notes: {notes}");
        Assert(UpdateService.Current >= new Version(1, 3, 0), $"running version is {UpdateService.Current}");
        return Task.CompletedTask;
    }

    private Task UpdaterVerifies()
    {
        var path = Path.Combine(_testDir, "download.bin");
        var bytes = Encoding.UTF8.GetBytes("pretend this is Draftly");
        File.WriteAllBytes(path, bytes);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        UpdateService.Verify(path, new UpdateService.Asset("x", "https://github.com/x", bytes.Length, sha));   // must not throw
        bool threw = false;
        try { UpdateService.Verify(path, new UpdateService.Asset("x", "https://github.com/x", bytes.Length, new string('0', 64))); }
        catch (InvalidDataException) { threw = true; }
        Assert(threw && !File.Exists(path), "a download with the wrong SHA-256 was accepted");
        File.WriteAllBytes(path, bytes);
        threw = false;
        try { UpdateService.Verify(path, new UpdateService.Asset("x", "https://github.com/x", bytes.Length + 5, null)); }
        catch (InvalidDataException) { threw = true; }
        Assert(threw, "an incomplete download was accepted");

        // Only GitHub downloads.
        threw = false;
        try { UpdateService.DownloadAsync(new UpdateService.Asset("evil.exe", "https://example.com/evil.exe", 1, null), null).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { threw = true; }
        Assert(threw, "a download from outside GitHub was allowed");
        return Task.CompletedTask;
    }

    private Task UpdaterAppliesPortable()
    {
        var folder = Path.Combine(_testDir, "portable");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "Draftly.exe");
        File.WriteAllText(exe, "old version");
        File.WriteAllText(Path.Combine(folder, "notes.old"), "someone else's file");
        Assert(UpdateService.DetectKind(folder) == UpdateService.InstallKind.Portable, "an unzipped Draftly was seen as installed");
        Assert(UpdateService.CanWriteTo(folder), "can't write to a temp folder");

        var zip = Path.Combine(_testDir, "update.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("Draftly.exe").Open()))
            w.Write("new version");

        // Hold the old exe open the way Windows does for a running program (it can be renamed, not overwritten).
        using (var running = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            UpdateService.ApplyPortable(zip, folder);
            Assert(File.ReadAllText(exe) == "new version", "Draftly.exe wasn't replaced");
            Assert(File.Exists(exe + ".old"), "the running exe wasn't moved aside");
        }
        UpdateService.CleanUp(folder);
        Assert(!File.Exists(exe + ".old"), "the old exe wasn't cleaned up");
        Assert(File.Exists(Path.Combine(folder, "notes.old")), "clean-up removed a file Draftly didn't make");

        File.WriteAllText(Path.Combine(folder, "unins000.exe"), "");
        Assert(UpdateService.DetectKind(folder) == UpdateService.InstallKind.Setup, "an installed Draftly wasn't recognised");
        return Task.CompletedTask;
    }

    private async Task UpdaterUi()
    {
        var info = UpdateService.Parse(FakeRelease("v99.1.2"));
        HideInfo();
        ShowUpdateAvailable(info);
        await Settle();
        Assert(InfoBar.IsVisible && InfoText.Text.Contains("99.1.2") && InfoText.Text.Contains(UpdateService.CurrentDisplay), $"info bar says \"{InfoText.Text}\"");

        var oldSkip = S.SkippedUpdate;
        string dialogText = "";
        ClickWhenOpen<UpdateDialog>("Later", d =>
        {
            dialogText = string.Join(" ", FindVisuals<TextBlock>(d).Select(t => t.Text));
            if (_shotDir != null) SnapshotWindow(d, "dialog-update-light");
        });
        OfferUpdate(info);
        Assert(dialogText.Contains("Draftly 99.1.2 is available") && dialogText.Contains("Tabs for documents"), $"the dialog shows \"{dialogText}\"");
        Assert(InfoBar.IsVisible, "Later shouldn't dismiss the update bar");
        ClickWhenOpen<UpdateDialog>("Skip this version");
        OfferUpdate(info);
        Assert(S.SkippedUpdate == "v99.1.2" && !InfoBar.IsVisible, "Skip this version didn't skip it");
        S.SkippedUpdate = oldSkip;
        SettingsService.Save();
    }

    private async Task UpdaterLiveCheck()
    {
        // The real GitHub API: the latest release must not be newer than the build being tested.
        try
        {
            var info = await UpdateService.CheckAsync();
            Assert(info == null, $"GitHub reports {info?.Display}, newer than this build ({UpdateService.CurrentDisplay})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // No network or rate-limited on the build machine: nothing to check.
        }
    }

    private async Task PrintPreviewIconWorks()
    {
        var icon = Icons.PrintPreview(32);
        var host = new Border { Child = icon, Width = 40, Height = 40 };
        host.SetResourceReference(TextElement.ForegroundProperty, "Text");
        host.Measure(new Size(40, 40));
        host.Arrange(new Rect(0, 0, 40, 40));
        host.UpdateLayout();
        var paths = FindVisuals<System.Windows.Shapes.Path>(host).ToList();
        Assert(paths.Count >= 8, $"icon has {paths.Count} shapes");
        Assert(paths.All(p => p.Stroke != null), "icon shapes don't follow the text colour");
        Assert(Math.Abs(icon.ActualWidth - 32) < 0.5, $"icon is {icon.ActualWidth} wide");
        await Settle();
    }
}
