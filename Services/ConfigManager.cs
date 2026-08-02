using System;
using System.IO;
using System.Linq;
using BedrockServerManager.Models;

namespace BedrockServerManager.Services;

public static class ConfigManager
{
    private static string LegacyDir  => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BedrockServerManager");
    private static string LegacyPath => Path.Combine(LegacyDir, "config.ini");

    public static void Save(SharedState s)
    {
        s.RecomputePaths();
        Directory.CreateDirectory(s.ConfigPath);
        var cfgPath = Path.Combine(s.ConfigPath, "config.ini");
        var lines = new[]
        {
            "[Settings]",
            $"RootPath={s.RootPath}",
            $"StartAfterUpdate={s.StartAfterUpdate}",
            $"AutoLaunchOnStart={s.AutoLaunchOnStart}",
            $"CrashProtection={s.CrashProtection}",
            $"AutoCheckUpdates={s.AutoCheckUpdates}",
            $"UpdateCheckHours={s.UpdateCheckHours}",
            $"AutoApplyUpdates={s.AutoApplyUpdates}",
            $"LogRetentionDays={s.LogRetentionDays}",
            $"MaxBackups={s.MaxBackups}",
            $"ScheduleRebootEnabled={s.ScheduleRebootEnabled}",
            $"ScheduleRebootFreq={s.ScheduleRebootFreq}",
            $"ScheduleRebootDate={s.ScheduleRebootDate}",
            $"ScheduleRebootTime={s.ScheduleRebootTime}"
        };
        File.WriteAllLines(cfgPath, lines);
    }

    public static void Load(SharedState s)
    {
        s.RecomputePaths();
        var cfgPath = Path.Combine(s.ConfigPath, "config.ini");

        if (File.Exists(LegacyPath) && !File.Exists(cfgPath))
        {
            try
            {
                Directory.CreateDirectory(s.ConfigPath);
                File.Copy(LegacyPath, cfgPath, true);
                File.Delete(LegacyPath);
                if (Directory.Exists(LegacyDir) && !Directory.EnumerateFileSystemEntries(LegacyDir).Any())
                    Directory.Delete(LegacyDir);
            }
            catch { }
        }

        if (!File.Exists(cfgPath)) return;

        foreach (var raw in File.ReadAllLines(cfgPath))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            
            Action<SharedState> apply = key switch
            {
                "RootPath"              => st => st.RootPath = val,
                "StartAfterUpdate"      => st => st.StartAfterUpdate = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "AutoLaunchOnStart"     => st => st.AutoLaunchOnStart = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "CrashProtection"       => st => st.CrashProtection = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "AutoCheckUpdates"      => st => st.AutoCheckUpdates = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "UpdateCheckHours"      => st => st.UpdateCheckHours = int.Parse(val),
                "AutoApplyUpdates"      => st => st.AutoApplyUpdates = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "LogRetentionDays"      => st => st.LogRetentionDays = int.Parse(val),
                "MaxBackups"            => st => st.MaxBackups = int.Parse(val),
                "ScheduleRebootEnabled" => st => st.ScheduleRebootEnabled = val.Equals("True", StringComparison.OrdinalIgnoreCase),
                "ScheduleRebootFreq"    => st => st.ScheduleRebootFreq = val,
                "ScheduleRebootDate"    => st => st.ScheduleRebootDate = val,
                "ScheduleRebootTime"    => st => st.ScheduleRebootTime = val,
                _ => null
            };
            apply?.Invoke(s);
        }
        s.RecomputePaths();
    }
}
