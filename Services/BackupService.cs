using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BedrockServerManager.Models;

namespace BedrockServerManager.Services;

public static class BackupService
{
    public static void BackupAll(SharedState s, Action<string, string> log, bool doLocal, bool doOffsite)
    {
        log("SYSTEM", "Starting full backup (Configs + Worlds)...");
        Directory.CreateDirectory(s.UpdateTempPath);
        var timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        var zipName = $"full_backup_{timeStr}.zip";
        var tempZipPath = Path.Combine(s.UpdateTempPath, zipName);
        var stageDir = Path.Combine(s.UpdateTempPath, $"backup_stage_{timeStr}");
        Directory.CreateDirectory(stageDir);

        try
        {
            var manifest = new Dictionary<string, string>();

            log("SYSTEM", "Hashing config files...");
            foreach (var f in s.FilesToBackup)
            {
                var src = Path.Combine(s.ServerPath, f);
                if (File.Exists(src))
                {
                    File.Copy(src, Path.Combine(stageDir, f), true);
                    manifest[f] = Sha256(src);
                }
            }

            var worldsDir = Path.Combine(s.ServerPath, "worlds");
            if (Directory.Exists(worldsDir))
            {
                log("SYSTEM", "Hashing world files...");
                foreach (var wf in Directory.EnumerateFiles(worldsDir, "*", SearchOption.AllDirectories))
                {
                    var rel = "worlds\\" + wf[(worldsDir.Length + 1)..];
                    var dest = Path.Combine(stageDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(wf, dest, true);
                    manifest[rel] = Sha256(wf);
                }
            }
            else log("WARN", "No 'worlds' directory found. Backing up configs only.");

            File.WriteAllText(Path.Combine(stageDir, "manifest.json"),
                JsonSerializer.Serialize(manifest));

            log("SYSTEM", $"Compressing backup to {zipName}... (This may take a moment)");
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            ZipFile.CreateFromDirectory(stageDir, tempZipPath, CompressionLevel.Optimal, false);

            var sizeMb = Math.Round(new FileInfo(tempZipPath).Length / 1024.0 / 1024.0, 2);
            log("SUCCESS", $"Backup archive created ({sizeMb} MB).");

            var actualLocalPath = string.IsNullOrWhiteSpace(s.LocalBackupPath) ? s.BackupPath : s.LocalBackupPath;

            // Local Backup Logic
            if (doLocal && !string.IsNullOrWhiteSpace(actualLocalPath))
            {
                try
                {
                    Directory.CreateDirectory(actualLocalPath);
                    var localZipPath = Path.Combine(actualLocalPath, zipName);
                    
                    log("SYSTEM", $"Copying backup to local location: {actualLocalPath}...");
                    File.Copy(tempZipPath, localZipPath, true);
                    log("SUCCESS", "Local backup complete.");

                    PurgeBackupsGFS(actualLocalPath, log);
                }
                catch (Exception ex) { log("ERROR", $"Local backup failed: {ex.Message}"); }
            }

            // Offsite Backup Logic
            if (doOffsite && !string.IsNullOrWhiteSpace(s.OffsiteBackupPath))
            {
                try
                {
                    Directory.CreateDirectory(s.OffsiteBackupPath);
                    var offsiteZipPath = Path.Combine(s.OffsiteBackupPath, zipName);
                    
                    log("SYSTEM", $"Copying backup to offsite location: {s.OffsiteBackupPath}...");
                    File.Copy(tempZipPath, offsiteZipPath, true);
                    log("SUCCESS", "Offsite backup complete.");

                    PurgeBackupsGFS(s.OffsiteBackupPath, log);
                }
                catch (Exception ex) { log("ERROR", $"Offsite backup failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log("ERROR", $"Backup failed: {ex.Message}"); }
        finally 
        { 
            try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch { }
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
        }
    }

    private static void PurgeBackupsGFS(string backupPath, Action<string, string> log)
    {
        try
        {
            var backups = Directory.EnumerateFiles(backupPath, "full_backup_*.zip")
                .Select(f => new { Path = f, Date = ParseBackupDate(Path.GetFileName(f)) })
                .Where(x => x.Date.HasValue)
                .OrderByDescending(x => x.Date!.Value)
                .ToList();

            var filesToDelete = new List<string>();
            
            DateTime lastKeptDaily = DateTime.MinValue;
            DateTime lastKeptWeekly = DateTime.MinValue;
            int lastKeptMonthly = -1;
            int lastKeptYearly = -1;

            foreach (var file in backups)
            {
                var date = file.Date!.Value;
                bool keep = false;

                if (date > DateTime.Now.AddDays(-7))
                {
                    if (date.Date != lastKeptDaily)
                    {
                        keep = true;
                        lastKeptDaily = date.Date;
                    }
                }
                else if (date > DateTime.Now.AddDays(-35))
                {
                    int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var weekStart = date.AddDays(-1 * diff).Date;
                    if (weekStart != lastKeptWeekly)
                    {
                        keep = true;
                        lastKeptWeekly = weekStart;
                    }
                }
                else if (date > DateTime.Now.AddYears(-1))
                {
                    if (date.Month != lastKeptMonthly)
                    {
                        keep = true;
                        lastKeptMonthly = date.Month;
                    }
                }
                else
                {
                    if (date.Year != lastKeptYearly)
                    {
                        keep = true;
                        lastKeptYearly = date.Year;
                    }
                }

                if (!keep)
                {
                    filesToDelete.Add(file.Path);
                }
            }

            if (filesToDelete.Count > 0)
            {
                log("SYSTEM", $"GFS Retention: Purging {filesToDelete.Count} old backup(s)...");
                foreach (var f in filesToDelete)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch (Exception ex) 
        { 
            log("WARN", $"GFS Retention purge failed: {ex.Message}"); 
        }
    }

    private static DateTime? ParseBackupDate(string filename)
    {
        if (!filename.StartsWith("full_backup_") || !filename.EndsWith(".zip")) return null;
        var dateStr = filename.Substring(12, filename.Length - 16);
        if (DateTime.TryParseExact(dateStr, "yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return null;
    }

    public static void RestoreBackup(SharedState s, string zipPath, Action<string, string> log)
    {
        log("WARN", $"Preparing to restore from {zipPath}...");
        var tempDir = Path.Combine(s.UpdateTempPath, $"restore_stage_{DateTime.Now:yyyyMMddHHmmss}");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        try
        {
            log("SYSTEM", "Extracting backup files to staging area...");
            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new Exception("Backup manifest.json missing. Cannot verify restore.");

            log("SYSTEM", "Verifying file checksums...");
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(manifestPath));

            foreach (var (rel, expected) in manifest)
            {
                var filePath = Path.Combine(tempDir, rel);
                if (!File.Exists(filePath))
                    throw new Exception($"Restore verification failed: Missing file {rel}.");
                if (Sha256(filePath) != expected)
                    throw new Exception($"Restore verification failed: Checksum mismatch for {rel}. Aborting restore.");
            }

            log("SUCCESS", "Checksums verified. Applying restored files...");
            CopyAll(tempDir, s.ServerPath);

            int n = s.FilesToBackup.Count(f => File.Exists(Path.Combine(s.ServerPath, f)));
            log("SUCCESS", $"Restore complete. {n} config file(s) verified and applied.");
        }
        catch (Exception ex) { log("ERROR", $"Restore failed: {ex.Message}"); }
        finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { } }
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
    }

    private static void CopyAll(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            CopyAll(dir, Path.Combine(dst, name));
        }
    }
}