using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using BedrockServerManager.Helpers;

namespace BedrockServerManager.Models;

public sealed class SharedState
{
    public string RootPath        { get; set; } = @"C:\Bedrock";
    public string ServerPath      { get; set; }
    public string BackupPath      { get; set; }
    public string LogsPath        { get; set; }
    public string UpdateTempPath  { get; set; }
    public string ConfigPath      { get; set; }

    public string ApiUrl              { get; set; } = "https://net-secondary.web.minecraft-services.net/api/v1.0/download/links";
    public string ServerExecutable    { get; set; } = "bedrock_server.exe";
    public string[] FilesToBackup     { get; set; } = new[] { "server.properties", "allowlist.json", "permissions.json" };

    public bool   StartAfterUpdate     { get; set; } = true;
    public bool   AutoLaunchOnStart    { get; set; } = false;
    public bool   CrashProtection      { get; set; } = true;
    public bool   AutoApplyUpdates     { get; set; } = false;
    public bool   AutoCheckUpdates     { get; set; } = true;
    public int    UpdateCheckHours     { get; set; } = 24;
    public int    MaxBackups           { get; set; } = 3;
    public int    LogRetentionDays     { get; set; } = 30;
    public int    ServerStopTimeout    { get; set; } = 15;
    public int    DownloadTimeout      { get; set; } = 180;
    public int    MaxServerConsoleLines { get; set; } = 2000;

    public bool   ScheduleRebootEnabled { get; set; } = false;
    public string ScheduleRebootFreq    { get; set; } = "Daily";
    public string ScheduleRebootDate    { get; set; } = "N/A";
    public string ScheduleRebootTime    { get; set; } = "03:00";
    public DateTime? NextRebootDate     { get; set; }
    public bool   IsRebooting           { get; set; }

    public string LatestUrl         { get; set; }
    public string LatestFilename    { get; set; }
    public string LatestVersion     { get; set; }
    public string InstalledVersion  { get; set; }
    public bool   IsBusy            { get; set; }
    public bool   IsInstalled       { get; set; }
    public bool   IsRunning         { get; set; }
    public bool   UpdateAvailable   { get; set; }
    public bool   ExpectedToRun     { get; set; }
    public DateTime? ServerStartTime { get; set; }
    public bool   StopRequested     { get; set; }
    public bool   GuiReady          { get; set; }
    public bool   WindowClosed      { get; set; }
    public Process ServerProcess    { get; set; }
    public BedrockProcessReader ServerOutputReader { get; set; }
    public int?   ServerProcessId   { get; set; }
    public bool   FirewallRuleVerified { get; set; }
    public string RestoreZipPath    { get; set; }

    public readonly object StdInWriteLock  = new();
    public readonly object ProgressLock    = new();

    public ConcurrentQueue<LogEntry>  PendingMessages        { get; } = new();
    public ConcurrentQueue<StatusUpdate> PendingStatus       { get; } = new();
    public ConcurrentQueue<ButtonUpdate> PendingButtons      { get; } = new();
    public ConcurrentQueue<LogEntry>  ServerConsoleMessages  { get; } = new();
    public ConcurrentQueue<string>    PendingServerCommands  { get; } = new();
    public ConcurrentQueue<string>    ServerConsoleHistory   { get; } = new();

    // Fixed record initialization
    public ProgressUpdate PendingProgress = new ProgressUpdate("none", 0);

    public void RecomputePaths()
    {
        ServerPath     = System.IO.Path.Combine(RootPath, "Server");
        BackupPath     = System.IO.Path.Combine(RootPath, "Backups");
        LogsPath       = System.IO.Path.Combine(RootPath, "Logs");
        UpdateTempPath = System.IO.Path.Combine(RootPath, "UpdateTemp");
        ConfigPath     = System.IO.Path.Combine(RootPath, "Config");
    }
}

public sealed record LogEntry(string Text, string Level);
public sealed record StatusUpdate(string Control, string Text, string Colour);
public sealed record ButtonUpdate(string Action);
public sealed record ProgressUpdate(string Type, int Value);