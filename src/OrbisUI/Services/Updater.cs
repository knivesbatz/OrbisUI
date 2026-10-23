using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OrbisUI.Core;

namespace OrbisUI.Services;

public sealed record UpdateInfo(Version Version, string Tag, string ZipUrl, string ShaUrl, string Notes);

/// <summary>
/// Updates from GitHub Releases of a fixed repository only. The download must match the SHA-256 published next to it.
/// Files are swapped by renaming the old ones to *.old (rolled back on failure); user folders are never touched.
/// </summary>
public static class Updater
{
    public const string Repo = "knivesbatz/OrbisUI";
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    { "Config", "Cache", "Logs", "Data", "Games", "Firmware", "Emulators", "Downloads", "Backups" };

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static Updater() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbisUI-Updater/" + CurrentText);

    public static Version? ParseTag(string tag)
    {
        var t = tag.TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash];
        return Version.TryParse(t, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            AppSettings.Current.LastUpdateCheck = DateTime.UtcNow;
            using var r = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!r.IsSuccessStatusCode) return null;
            using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var tag = d.RootElement.GetProperty("tag_name").GetString() ?? "";
            var v = ParseTag(tag);
            var cur = new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            if (v == null || v <= cur) return null;
            string? zip = null, sha = null;
            foreach (var a in d.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                // Only accept GitHub-hosted assets of this repository.
                if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                else if (name.EndsWith("-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
            }
            if (zip == null || sha == null) { Log.Warn($"Release {tag} has no verifiable package"); return null; }
            return new UpdateInfo(v, tag, zip, sha, d.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
        }
        catch (Exception ex) { Log.Warn("Update check failed: " + ex.Message); return null; }
    }

    public static async Task<string> DownloadAsync(UpdateInfo u, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(AppPaths.Downloads, "update");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, $"OrbisUI-{u.Tag}.zip");
        var expected = (await Http.GetStringAsync(u.ShaUrl, ct)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
        if (expected.Length != 64) throw new InvalidDataException("Release checksum file is malformed.");

        using (var resp = await Http.GetAsync(u.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            var buf = new byte[81920];
            long got = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                if (total > 0) progress?.Report((double)got / total);
            }
        }
        var actual = Sha256File(zipPath);
        if (actual != expected)
        {
            File.Delete(zipPath);
            throw new InvalidDataException("The download did not pass verification and was discarded.");
        }
        Log.Info($"Update {u.Tag} downloaded and verified ({actual})");
        return zipPath;
    }

    public static string Sha256File(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    /// <summary>Extracts the verified package over the app files and restarts. Returns false (and rolls back) on failure.</summary>
    public static bool Apply(string zipPath)
    {
        var root = AppPaths.Root;
        var staging = Path.Combine(AppPaths.Downloads, "update", "staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipPath, staging);
        // Packages may contain a single top-level folder.
        var top = Directory.GetDirectories(staging);
        if (Directory.GetFiles(staging).Length == 0 && top.Length == 1) staging = top[0];

        var replaced = new List<(string target, string backup)>();
        var added = new List<string>();
        try
        {
            foreach (var src in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(staging, src);
                var first = rel.Split(Path.DirectorySeparatorChar)[0];
                if (Protected.Contains(first)) continue;
                var target = Path.GetFullPath(Path.Combine(root, rel));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // path traversal guard
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    var bak = target + ".old";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(target, bak);              // works even for the running exe
                    replaced.Add((target, bak));
                }
                else added.Add(target);
                File.Copy(src, target);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update failed, rolling back", ex);
            foreach (var a in added) try { File.Delete(a); } catch { }
            foreach (var (t, b) in replaced) try { if (File.Exists(t)) File.Delete(t); File.Move(b, t); } catch { }
            return false;
        }
        Log.Info("Update applied; restarting");
        Process.Start(new ProcessStartInfo(Path.Combine(root, "OrbisUI.exe"), "--updated") { UseShellExecute = false, WorkingDirectory = root });
        return true;
    }

    /// <summary>Remove leftovers from the previous version (called at startup).</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(AppPaths.Root, "*.old", SearchOption.TopDirectoryOnly))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }
}
