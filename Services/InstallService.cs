using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BedrockServerManager.Helpers;
using BedrockServerManager.Models; // This was missing!

namespace BedrockServerManager.Services;

public static class InstallService
{
    public static void InitializeDirectories(SharedState s, Action<string, string> log)
    {
        foreach (var d in new[] { s.RootPath, s.ServerPath, s.BackupPath, s.LogsPath, s.UpdateTempPath, s.ConfigPath })
        {
            if (!Directory.Exists(d))
            {
                Directory.CreateDirectory(d);
                log("SYSTEM", $"Created directory: {d}");
            }
        }
    }

    public static bool TestServerInstalled(SharedState s) =>
        File.Exists(Path.Combine(s.ServerPath, s.ServerExecutable));

    public static string GetAppliedVersion(SharedState s)
    {
        var p = Path.Combine(s.ServerPath, "applied_version.txt");
        return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
    }

    public static void SetAppliedVersion(SharedState s, string version) =>
        File.WriteAllText(Path.Combine(s.ServerPath, "applied_version.txt"), version);

    public static string GetInstalledVersion(SharedState s)
    {
        var exe = Path.Combine(s.ServerPath, s.ServerExecutable);
        if (!File.Exists(exe)) return null;
        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
        var v = !string.IsNullOrEmpty(vi.ProductVersion) ? vi.ProductVersion
              : !string.IsNullOrEmpty(vi.FileVersion)    ? vi.FileVersion
              : null;
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return GetAppliedVersion(s);
    }

    public static async Task DownloadAndInstallAsync(
        SharedState s, string url, string filename, bool isFirstSetup,
        Action<string, string> log, Action<string, int> setProgress,
        Action<string, string, string> setStatus, Action<SharedState> refreshInstalled,
        CancellationToken ct = default)
    {
        var zipPath = Path.Combine(s.UpdateTempPath, filename);
        InitializeDirectories(s, log);
        setProgress("value", 5);

        if (!isFirstSetup)
            await ServerProcessService.StopGameServerAsync(s, log, setStatus);

        setProgress("value", 12);
        if (TestServerInstalled(s))
            BackupService.BackupAll(s, log);

        setProgress("value", 22);
        log("INFO", $"Downloading: {filename} …");
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(s.DownloadTimeout) })
        using (var resp = await http.GetAsync(url, ct))
        using (var fs = File.Create(zipPath))
            await resp.Content.CopyToAsync(fs, ct);

        var fi = new FileInfo(zipPath);
        if (!fi.Exists || fi.Length < 1024 * 1024)
            throw new Exception("Download failed or file is too small (corrupt).");

        log("SYSTEM", "Verifying archive integrity...");
        int entries;
        using (var za = ZipFile.OpenRead(zipPath)) entries = za.Entries.Count;
        if (entries < 5) throw new Exception("Archive seems empty or invalid.");
        log("SUCCESS", $"Archive verified ({entries} entries).");

        var sizeMb = Math.Round(fi.Length / 1024.0 / 1024.0, 2);
        log("SUCCESS", $"Download complete ({sizeMb} MB).");
        setProgress("value", 58);

        log("INFO", $"Extracting server files to {s.ServerPath}…");
        ZipFile.ExtractToDirectory(zipPath, s.ServerPath, true);

        var extractedExe = Path.Combine(s.ServerPath, s.ServerExecutable);
        if (!File.Exists(extractedExe))
            throw new Exception($"Extraction verification failed: {s.ServerExecutable} not found.");
        log("SUCCESS", "Extraction complete & verified.");

        FirewallHelper.EnsureRule(extractedExe, log);
        setProgress("value", 80);
        log("SYSTEM", "Configs and Worlds preserved via archive backup.");
        setProgress("value", 90);

        var verLatest = VersionComparer.ExtractVersionFromFilename(filename);
        SetAppliedVersion(s, verLatest);
        if (File.Exists(zipPath)) { File.Delete(zipPath); log("SYSTEM", "Cleaned up downloaded archive."); }

        refreshInstalled(s);
        setStatus("lblLatest",      verLatest, "blue");
        setStatus("lblSetupStatus", "INSTALLED", "green");
        setStatus("lblUpdateStatus","UP TO DATE", "green");
        s.IsInstalled = true;
        s.UpdateAvailable = false;

        if (s.StartAfterUpdate)
            await ServerProcessService.StartServerProcessAsync(s, log, setStatus, ct);
        else
            setStatus("lblServerStatus", "STOPPED", "red");

        setProgress("value", 100);
        GC.Collect();
    }
}