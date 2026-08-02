using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BedrockServerManager.Models;

namespace BedrockServerManager.Services;

public static class BackupService
{
    public static void BackupAll(SharedState s, Action<string, string> log)
    {
        log("SYSTEM", "Starting full backup (Configs + Worlds)...");
        Directory.CreateDirectory(s.BackupPath);
        var timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        var zipName = $"full_backup_{timeStr}.zip";
        var zipPath = Path.Combine(s.BackupPath, zipName);
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
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(stageDir, zipPath, CompressionLevel.Optimal, false);

            var sizeMb = Math.Round(new FileInfo(zipPath).Length / 1024.0 / 1024.0, 2);
            log("SUCCESS", $"Backup complete ({sizeMb} MB).");

            var old = Directory.EnumerateFiles(s.BackupPath, "full_backup_*.zip")
                .OrderByDescending(f => Path.GetFileName(f))
                .Skip(s.MaxBackups)
                .ToList();
                
            if (old.Count > 0)
            {
                log("SYSTEM", $"Purging {old.Count} old backup(s) to retain max {s.MaxBackups}...");
                foreach (var f in old) File.Delete(f);
            }
        }
        catch (Exception ex) { log("ERROR", $"Backup failed: {ex.Message}"); }
        finally { try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch { } }
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