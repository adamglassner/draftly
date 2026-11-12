using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Draftly.Services;

/// <summary>
/// Keeps Draftly up to date from its GitHub releases.
/// <list type="bullet">
/// <item>Installed with the setup: downloads the new setup and runs it silently, then Draftly reopens.</item>
/// <item>Unzipped (portable, or installed by the Bytehawk Studio Launcher): replaces Draftly.exe in place
/// (a running program can be renamed on Windows, so the old one becomes Draftly.exe.old and is removed next time).</item>
/// </list>
/// Downloads only come from GitHub over HTTPS and are checked against the size and SHA-256 that GitHub publishes.
/// </summary>
public static class UpdateService
{
    public const string Repo = "adamglassner/draftly";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    /// <summary>Overridable for the self-test.</summary>
    public static string LatestReleaseApi { get; set; } = $"https://api.github.com/repos/{Repo}/releases/latest";

    public sealed record Asset(string Name, string Url, long Size, string Sha256);

    public sealed record UpdateInfo(Version Version, string Tag, string Notes, string Page, Asset Setup, Asset Zip)
    {
        public string Display => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";
    }

    public enum InstallKind { Setup, Portable }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentDisplay => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"Draftly/{CurrentDisplay} (+https://github.com/{Repo})");
        return h;
    });

    // ------------------------------------------------------------------ checking

    /// <summary>Returns the latest release if it's newer than this Draftly, otherwise null. Throws on network errors.</summary>
    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.Value.SendAsync(req, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        var info = Parse(json);
        return info != null && info.Version > Current ? info : null;
    }

    /// <summary>Reads a GitHub "latest release" response.</summary>
    public static UpdateInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version)) return null;

        Asset setup = null, zip = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                long size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                string sha = null;
                if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String &&
                    dg.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..].ToLowerInvariant();
                var asset = new Asset(name, url, size, sha);
                if (name.StartsWith("Draftly-", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) setup = asset;
                else if (name.StartsWith("Draftly-", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) zip = asset;
            }
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : ReleasesPage;
        return new UpdateInfo(version, tag, notes, page, setup, zip);
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        version = null;
        var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return false;
        version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        return true;
    }

    /// <summary>The release notes in plain text, shortened for a dialog.</summary>
    public static string ShortNotes(string markdown, int maxLines = 14)
    {
        var lines = (markdown ?? "").Replace("\r", "").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0 && !l.StartsWith("## "))
            .Select(l => l.StartsWith("### ") ? l[4..] : l.TrimStart().StartsWith("- ") ? "•  " + l.TrimStart()[2..] : l)
            .Select(l => l.Replace("**", "").Replace("`", ""))
            .ToList();
        var shown = lines.Take(maxLines).ToList();
        if (lines.Count > maxLines) shown.Add("…");
        return string.Join("\n", shown);
    }

    // ------------------------------------------------------------------ installing

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
    public static string ExeFolder => Path.GetDirectoryName(ExePath);

    /// <summary>Installed with Draftly's setup (it leaves an uninstaller next to the exe), or just unzipped?</summary>
    public static InstallKind DetectKind(string folder) =>
        Directory.EnumerateFiles(folder, "unins*.exe").Any() ? InstallKind.Setup : InstallKind.Portable;

    public static bool CanWriteTo(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".draftly-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "Draftly-update");

    /// <summary>Downloads an update file and checks it. Returns the local path.</summary>
    public static async Task<string> DownloadAsync(Asset asset, IProgress<double> progress, CancellationToken ct = default)
    {
        if (asset == null) throw new InvalidOperationException("This release has no download for this kind of installation.");
        if (!IsTrustedUrl(asset.Url)) throw new InvalidOperationException("The download isn't on GitHub, so Draftly won't use it.");
        Directory.CreateDirectory(DownloadFolder);
        var path = Path.Combine(DownloadFolder, Path.GetFileName(asset.Name));
        using (var resp = await Http.Value.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            if (resp.RequestMessage?.RequestUri is { } final && !IsTrustedUrl(final.AbsoluteUri))
                throw new InvalidOperationException("The download was redirected away from GitHub.");
            long total = resp.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
            }
        }
        Verify(path, asset);
        return path;
    }

    /// <summary>Checks a downloaded file against the size and SHA-256 GitHub published.</summary>
    public static void Verify(string path, Asset asset)
    {
        var info = new FileInfo(path);
        if (asset.Size > 0 && info.Length != asset.Size)
        {
            File.Delete(path);
            throw new InvalidDataException("The download is incomplete. Please try again.");
        }
        if (asset.Sha256 != null)
        {
            string hash;
            using (var fs = File.OpenRead(path)) hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (hash != asset.Sha256)
            {
                File.Delete(path);
                throw new InvalidDataException("The download doesn't match the release on GitHub, so it wasn't installed.");
            }
        }
    }

    /// <summary>Runs the downloaded setup silently. It closes Draftly if needed and reopens it when done.</summary>
    public static void RunSetup(string setupPath)
    {
        // The setup asks Windows for permission itself (UAC) when Draftly is installed for all users.
        Process.Start(new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>Unzips an update over an unzipped Draftly. Files in use (Draftly.exe) are renamed to *.old first.</summary>
    public static void ApplyPortable(string zipPath, string targetFolder)
    {
        var staging = Path.Combine(DownloadFolder, "staging-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);
            var root = staging;
            // Accept a zip with everything inside one folder too.
            if (!Directory.EnumerateFiles(staging).Any() && Directory.GetDirectories(staging) is { Length: 1 } only) root = only[0];
            if (!File.Exists(Path.Combine(root, "Draftly.exe"))) throw new InvalidDataException("The update doesn't contain Draftly.exe.");

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                var dest = Path.GetFullPath(Path.Combine(targetFolder, rel));
                if (!dest.StartsWith(Path.GetFullPath(targetFolder), StringComparison.OrdinalIgnoreCase)) continue;   // never write outside the folder
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest))
                {
                    var old = dest + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(dest, old);   // works even while Draftly.exe is running
                }
                File.Copy(file, dest);
            }
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    /// <summary>Removes files left over from the last update.</summary>
    public static void CleanUp(string folder)
    {
        try
        {
            var old = Path.Combine(folder, "Draftly.exe.old");   // only what ApplyPortable renamed
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
        try { if (Directory.Exists(DownloadFolder)) Directory.Delete(DownloadFolder, true); } catch { }
    }
}
