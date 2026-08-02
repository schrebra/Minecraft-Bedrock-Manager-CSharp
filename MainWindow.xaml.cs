using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BedrockServerManager.Helpers;
using BedrockServerManager.Models;
using BedrockServerManager.Services;
using Forms = System.Windows.Forms;

namespace BedrockServerManager;

public partial class MainWindow : Window
{
    private readonly SharedState _state = new();

    private readonly Dictionary<string, System.Windows.Media.Brush> _brushCache        = new();
    private readonly Dictionary<string, System.Windows.Media.Brush> _statusBrushCache  = new();
    private readonly Dictionary<string, System.Windows.Media.Brush> _serverBrushCache  = new();

    private readonly DispatcherTimer _timer;
    private DateTime _lastGcTime;
    private DateTime _lastServerLogClean;
    private DateTime _pcBootTime;
    private int _tickCount;
    private int _commandHistoryIdx = -1;
    private bool _uiInitialized;
    private readonly List<CancellationTokenSource> _activeCts = new();
    private readonly object _activeCtsLock = new();

    public MainWindow()
    {
        var args = App.StartupArgs ?? Array.Empty<string>();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("-RootPath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[i + 1]))
                _state.RootPath = args[i + 1];

        ConfigManager.Load(_state);

        InitializeComponent();
        BuildBrushCaches();
        
        InitializeUiState();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += Timer_Tick;

        _pcBootTime = GetPcBootTime();
        _lastGcTime = DateTime.Now;
        _lastServerLogClean = DateTime.Now;

        SourceInitialized += OnSourceInitialized;
        ContentRendered   += OnContentRendered;
        Closing           += OnClosing;
        Closed            += OnClosed;

        HookTitleBar();
        HookSettingsAutoSave();
        HookButtons();
    }

    private void InitializeUiState()
    {
        txtRootPath.Text = _state.RootPath;
        chkAutoStart.IsChecked = _state.StartAfterUpdate;
        chkAutoLaunch.IsChecked = _state.AutoLaunchOnStart;
        chkCrashProtect.IsChecked = _state.CrashProtection;
        chkAutoCheckUpdates.IsChecked = _state.AutoCheckUpdates;
        chkAutoApplyUpdates.IsChecked = _state.AutoApplyUpdates;
        
        // Grey out the apply updates box if check updates is off
        chkAutoApplyUpdates.IsEnabled = _state.AutoCheckUpdates;
        
        txtMaxBackups.Text = _state.MaxBackups.ToString();
        chkScheduleReboot.IsChecked = _state.ScheduleRebootEnabled;
        lblHostname.Text = NetworkHelper.GetHostName();
    }

    private void BuildBrushCaches()
    {
        var colourMap = new Dictionary<string, string>
        {
            ["INFO"]="CAD3F5", ["WARN"]="F5A97F", ["ERROR"]="ED8796", ["SUCCESS"]="A6DA95",
            ["HEADER"]="7DC4E4", ["SYSTEM"]="6E738D", ["PERIODIC"]="C6A0F6"
        };
        var statusColourMap = new Dictionary<string, string>
        {
            ["green"]="A6DA95", ["blue"]="8AADF4", ["red"]="ED8796", ["orange"]="F5A97F",
            ["gray"]="5B6078", ["white"]="CAD3F5"
        };
        var serverColourMap = new Dictionary<string, string>
        {
            ["INFO"]="CAD3F5", ["WARN"]="F5A97F", ["ERROR"]="ED8796", ["SUCCESS"]="A6DA95",
            ["CMD"]="8BD5CA", ["SYSTEM"]="6E738D"
        };
        foreach (var kv in colourMap)        _brushCache[kv.Key]       = FreezeBrush(kv.Value);
        foreach (var kv in statusColourMap)  _statusBrushCache[kv.Key] = FreezeBrush(kv.Value);
        foreach (var kv in serverColourMap)  _serverBrushCache[kv.Key] = FreezeBrush(kv.Value);
    }
    private static System.Windows.Media.Brush FreezeBrush(string hex)
    {
        var b = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#" + hex);
        b.Freeze();
        return b;
    }

    private void HookTitleBar()
    {
        btnMin.Click  += (_, _) => WindowState = WindowState.Minimized;
        btnMax.Click  += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnClose.Click+= (_, _) => Close();
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var src = PresentationSource.FromVisual(this) as HwndSource;
        if (src == null) return;
        src.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            try
            {
                var hMon = MaximizeHelper.MonitorFromWindow(hwnd, MaximizeHelper.MONITOR_DEFAULTTONEAREST);
                if (hMon == IntPtr.Zero) return IntPtr.Zero;
                var mi = new MaximizeHelper.MONITORINFO { cbSize = Marshal.SizeOf<MaximizeHelper.MONITORINFO>() };
                if (MaximizeHelper.GetMonitorInfo(hMon, ref mi))
                {
                    var scale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
                    double bp = 6 * scale;
                    int mW = (mi.rcWork.Right - mi.rcWork.Left) + (int)(bp * 2);
                    int mH = (mi.rcWork.Bottom - mi.rcWork.Top) + (int)(bp * 2);
                    int mX = mi.rcWork.Left - (int)bp;
                    int mY = mi.rcWork.Top  - (int)bp;
                    Marshal.WriteInt32(lParam, 8,  mW);
                    Marshal.WriteInt32(lParam, 12, mH);
                    Marshal.WriteInt32(lParam, 16, mX);
                    Marshal.WriteInt32(lParam, 20, mY);
                    handled = true;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    private void OnContentRendered(object sender, EventArgs e)
    {
        _state.GuiReady = true;
        UpdatePathLabels();
        UpdateNextRebootLabel();

        // Calculate the initial update check date
        if (_state.AutoCheckUpdates)
        {
            _state.NextUpdateCheckDate = ScheduledRebootService.GetNextRebootDate(
                _state.UpdateCheckFreq, _state.UpdateCheckDate, _state.UpdateCheckTime);
        }

        var exe = Path.Combine(_state.ServerPath, _state.ServerExecutable);
        if (File.Exists(exe))
        {
            _state.IsInstalled = true;
            var vi = FileVersionInfo.GetVersionInfo(exe);
            var v = !string.IsNullOrEmpty(vi.ProductVersion) ? vi.ProductVersion
                  : !string.IsNullOrEmpty(vi.FileVersion)    ? vi.FileVersion : null;
            var appliedPath = Path.Combine(_state.ServerPath, "applied_version.txt");
            var appliedVer = File.Exists(appliedPath) ? File.ReadAllText(appliedPath).Trim() : null;

            if (string.IsNullOrWhiteSpace(v))
                v = appliedVer ?? "Unknown";
            else
            {
                v = v.Trim();
                if (appliedVer == null) File.WriteAllText(appliedPath, v);
            }
            lblInstalled.Text = v;
            lblInstalled.Foreground = _statusBrushCache["white"];
            lblSetupStatus.Text = "INSTALLED";
            lblSetupStatus.Foreground = _statusBrushCache["green"];

            var exeName = Path.GetFileNameWithoutExtension(_state.ServerExecutable);
            var proc = Process.GetProcessesByName(exeName).FirstOrDefault(p =>
            { try { return p.MainModule?.FileName == exe; } catch { return false; } });

            if (proc != null)
            {
                FirewallHelper.EnsureRule(exe, LogToManager);
                _state.IsRunning = true; _state.ExpectedToRun = true;
                try { _state.ServerStartTime = proc.StartTime; } catch { _state.ServerStartTime = null; }
                _state.ServerProcessId = proc.Id;
                lblServerStatus.Text = $"RUNNING (PID {proc.Id} — adopted)";
                lblServerStatus.Foreground = _statusBrushCache["green"];
                var (h, ip) = NetworkHelper.GetServerConnectionInfo(_state.ServerPath);
                lblIpPort.Text = ip; lblIpPort.Foreground = _statusBrushCache["blue"];
                AppendServerLine($"[Detected already-running bedrock_server (PID {proc.Id}). Stdin wrapper unavailable — stop and start from GUI to enable command input.]", "SYSTEM");
            }
            else
            {
                _state.IsRunning = false;
                lblServerStatus.Text = "STOPPED";
                lblServerStatus.Foreground = _statusBrushCache["red"];
                lblIpPort.Text = "—"; lblIpPort.Foreground = _statusBrushCache["gray"];
            }
        }
        else
        {
            _state.IsInstalled = false; _state.IsRunning = false;
            lblInstalled.Text = "Not installed";
            lblInstalled.Foreground = _statusBrushCache["red"];
            lblSetupStatus.Text = "NOT INSTALLED";
            lblSetupStatus.Foreground = _statusBrushCache["red"];
            lblServerStatus.Text = "N/A";
            lblServerStatus.Foreground = _statusBrushCache["orange"];
            lblIpPort.Text = "—";
            lblIpPort.Foreground = _statusBrushCache["gray"];
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        AppendLogLine($"{now} [SYSTEM ] -------------------------------------------", "SYSTEM");
        AppendLogLine($"{now} [SYSTEM ]   Minecraft Bedrock Server Manager", "SUCCESS");
        AppendLogLine($"{now} [SYSTEM ] -------------------------------------------", "SYSTEM");

        if (!File.Exists(exe))
            AppendLogLine($"{now} [WARN   ] No installation detected — click 'Setup / Install' to begin.", "WARN");
        else
        {
            AppendLogLine($"{now} [INFO   ] Installation found. Performing initial update check...", "INFO");
            StartBackgroundWork(async ct =>
            {
                SetBusy(true);
                try
                {
                    await PeriodicStatusCheckAsync(ct);
                    if (_state.AutoLaunchOnStart && !_state.IsRunning)
                        await ServerProcessService.StartServerProcessAsync(_state, LogToManager, SetStatusLabel, ct);
                }
                finally { SetBusy(false); }
            });
        }

        _uiInitialized = true;
        UpdateButtonStates();
        _timer.Start();
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (_state.WindowClosed) return;

        int msgCount = Math.Min(50, _state.PendingMessages.Count);
        for (int i = 0; i < msgCount; i++)
        {
            if (_state.PendingMessages.TryDequeue(out var m))
            {
                var c = _brushCache.GetValueOrDefault(m.Level, _brushCache["INFO"]);
                AppendParagraph(rtbLog, m.Text, c);
            }
        }
        TrimBlocks(rtbLog, 500);
        rtbLog.ScrollToEnd();

        int srvCount = Math.Min(100, _state.ServerConsoleMessages.Count);
        for (int i = 0; i < srvCount; i++)
        {
            if (_state.ServerConsoleMessages.TryDequeue(out var m))
            {
                var c = _serverBrushCache.GetValueOrDefault(m.Level, _serverBrushCache["INFO"]);
                AppendParagraph(rtbServerLog, m.Text, c);
            }
        }

        if (_state.ServerOutputReader != null)
        {
            int read = 0;
            while (read < 100 && _state.ServerOutputReader.OutputQueue.TryDequeue(out var line))
            {
                var level = ClassifyServerLine(line);
                AppendParagraph(rtbServerLog, line, _serverBrushCache.GetValueOrDefault(level, _serverBrushCache["INFO"]));
                read++;
            }
            while (_state.ServerOutputReader.ErrorQueue.TryDequeue(out var line))
                AppendParagraph(rtbServerLog, line, _serverBrushCache["ERROR"]);
            TrimBlocks(rtbServerLog, _state.MaxServerConsoleLines);
            rtbServerLog.ScrollToEnd();
        }

        while (_state.PendingServerCommands.TryDequeue(out var cmd))
        {
            var sp = _state.ServerProcess;
            if (sp != null && !sp.HasExited)
            {
                try
                {
                    lock (_state.StdInWriteLock)
                    {
                        sp.StandardInput.WriteLine(cmd);
                        sp.StandardInput.Flush();
                    }
                    _state.ServerConsoleMessages.Enqueue(new LogEntry($"> {cmd}", "CMD"));
                    if (cmd.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                        _state.ExpectedToRun = false;
                }
                catch (Exception ex)
                {
                    AppendLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR  ] Failed to send command '{cmd}': {ex.Message}", "ERROR");
                }
            }
        }

        while (_state.PendingStatus.TryDequeue(out var u))
        {
            var ctrl = FindName(u.Control) as TextBlock;
            if (ctrl != null)
            {
                ctrl.Text = u.Text;
                if (_statusBrushCache.TryGetValue(u.Colour, out var b)) ctrl.Foreground = b;
                if (u.Control is "lblInstallDir" or "txtRootPath") UpdatePathLabels();
            }
        }

        ProgressUpdate prog;
        lock (_state.ProgressLock) prog = _state.PendingProgress;
        switch (prog.Type)
        {
            case "indeterminate":
                progressBar.IsIndeterminate = true;
                lblProgressText.Visibility = Visibility.Collapsed;
                break;
            case "value":
                progressBar.IsIndeterminate = false;
                progressBar.Value = prog.Value;
                lblProgressText.Text = $"{prog.Value}%";
                lblProgressText.Visibility = Visibility.Visible;
                break;
            case "reset":
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                lblProgressText.Visibility = Visibility.Collapsed;
                lock (_state.ProgressLock) _state.PendingProgress = new ProgressUpdate("none", 0);
                break;
        }

        while (_state.PendingButtons.TryDequeue(out var b))
        {
            if (b.Action == "busy") _state.IsBusy = true;
            else if (b.Action == "free") _state.IsBusy = false;
        }

        if (_state.ServerProcess is { HasExited: true } sp2 && !_state.IsBusy)
        {
            int code = sp2.ExitCode;
            _state.ServerConsoleMessages.Enqueue(new LogEntry($"[Process exited with code {code}]", "SYSTEM"));
            AppendLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [WARN   ] Server process exited with code {code}.", "WARN");
            if (!_state.ExpectedToRun)
            {
                _state.ServerProcess = null; _state.ServerProcessId = null;
                _state.ServerOutputReader = null;
                _state.IsRunning = false; _state.ServerStartTime = null;
                lblServerStatus.Text = "STOPPED"; lblServerStatus.Foreground = _statusBrushCache["red"];
                lblIpPort.Text = "—"; lblIpPort.Foreground = _statusBrushCache["gray"];
            }
            else
            {
                _state.ServerProcess = null; _state.ServerProcessId = null;
                _state.ServerOutputReader = null;
            }
        }

        UpdateButtonStates();
        _tickCount++;

        if (_state.ScheduleRebootEnabled && _state.NextRebootDate.HasValue
            && !_state.IsBusy && !_state.IsRebooting
            && DateTime.Now >= _state.NextRebootDate.Value)
        {
            _state.IsRebooting = true;
            _state.NextRebootDate = null;
            AppendLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [SYSTEM ] Scheduled reboot time reached. Restarting server...", "SYSTEM");
            StartBackgroundWork(async ct =>
            {
                try
                {
                    await ServerProcessService.StopGameServerAsync(_state, LogToManager, SetStatusLabel);
                    LogToManager("SYSTEM", "Waiting 10 seconds before restarting to prevent file locks...");
                    await Task.Delay(10000, ct);
                    await ServerProcessService.StartServerProcessAsync(_state, LogToManager, SetStatusLabel, ct);
                }
                finally
                {
                    _state.IsRebooting = false;
                    _state.NextRebootDate = ScheduledRebootService.GetNextRebootDate(
                        _state.ScheduleRebootFreq, _state.ScheduleRebootDate, _state.ScheduleRebootTime);
                }
            });
        }

        if (_tickCount % 17 == 0)
        {
            if (_pcBootTime != default)
            {
                var up = DateTime.Now - _pcBootTime;
                lblPcUptime.Text = $"{up.Days}d {up.Hours}h {up.Minutes}m";
            }
            if (_state.IsRunning && _state.ServerStartTime.HasValue)
            {
                var su = DateTime.Now - _state.ServerStartTime.Value;
                lblServerUptime.Text = $"{su.Days}d {su.Hours}h {su.Minutes}m";
            }
            else lblServerUptime.Text = "—";

            if (Directory.Exists(_state.BackupPath))
            {
                var latest = new DirectoryInfo(_state.BackupPath)
                    .GetFiles("full_backup_*.zip")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                lblLastBackup.Text = latest?.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "None";
            }
            else lblLastBackup.Text = "None";
        }

        if (!_state.IsBusy && _state.ExpectedToRun && _state.CrashProtection && !_state.IsRebooting)
        {
            var exePath = Path.Combine(_state.ServerPath, _state.ServerExecutable);
            if (File.Exists(exePath))
            {
                bool procFound = false;
                if (_state.ServerProcessId is int pid)
                {
                    try { var p = Process.GetProcessById(pid); if (!p.HasExited) procFound = true; } catch { }
                }
                if (!procFound)
                {
                    var exeName = Path.GetFileNameWithoutExtension(_state.ServerExecutable);
                    var byName = Process.GetProcessesByName(exeName).FirstOrDefault(p =>
                    { try { return p.MainModule?.FileName == exePath; } catch { return false; } });
                    if (byName != null && !byName.HasExited) { procFound = true; _state.ServerProcessId = byName.Id; }
                }
                if (!procFound && _state.IsRunning)
                {
                    _state.IsRunning = false; _state.ServerStartTime = null;
                    _state.ServerProcess = null; _state.ServerProcessId = null;
                    _state.ServerOutputReader = null;
                    AppendLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR  ] Crash detected! Server process missing. Attempting recovery...", "ERROR");
                    lblServerStatus.Text = "CRASHED - RECOVERING";
                    lblServerStatus.Foreground = _statusBrushCache["red"];
                    StartBackgroundWork(async ct =>
                    {
                        LogToManager("SYSTEM", "Waiting 10 seconds before restarting to prevent file locks...");
                        await Task.Delay(10000, ct);
                        await ServerProcessService.StartServerProcessAsync(_state, LogToManager, SetStatusLabel, ct);
                    });
                }
            }
        }

        // Scheduled Update Check Logic
        if (_state.AutoCheckUpdates && _state.NextUpdateCheckDate.HasValue && !_state.IsBusy)
        {
            var ts = _state.NextUpdateCheckDate.Value - DateTime.Now;
            if (ts.TotalSeconds <= 0)
            {
                _state.NextUpdateCheckDate = ScheduledRebootService.GetNextRebootDate(_state.UpdateCheckFreq, _state.UpdateCheckDate, _state.UpdateCheckTime);
                dotPeriodic.Fill = _statusBrushCache["orange"];
                StartBackgroundWork(async ct =>
                {
                    SetBusy(true);
                    try { await PeriodicStatusCheckAsync(ct); }
                    finally { SetBusy(false); }
                });
            }
            else
            {
                // Show the exact time of the next check instead of a countdown
                lblNextCheck.Text = _state.NextUpdateCheckDate.Value.ToString("MMM dd HH:mm");
                dotPeriodic.Fill = ts.TotalHours < 1 ? _statusBrushCache["red"] : _statusBrushCache["green"];
            }
        }
        else if (_state.AutoCheckUpdates && !_state.NextUpdateCheckDate.HasValue)
        {
            _state.NextUpdateCheckDate = ScheduledRebootService.GetNextRebootDate(_state.UpdateCheckFreq, _state.UpdateCheckDate, _state.UpdateCheckTime);
        }
        else
        {
            lblNextCheck.Text = "Off";
            dotPeriodic.Fill = _statusBrushCache["gray"];
        }

        if ((DateTime.Now - _lastGcTime).TotalMinutes >= 5)
        {
            _lastGcTime = DateTime.Now;
            GC.Collect(); GC.WaitForPendingFinalizers();
        }

        if ((DateTime.Now - _lastServerLogClean).TotalHours >= 24)
        {
            rtbServerLog.Document.Blocks.Clear();
            AppendParagraph(rtbServerLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SYSTEM] Automatic 24-hour console cleanup executed.",
                _serverBrushCache["SYSTEM"]);
            _lastServerLogClean = DateTime.Now;
        }

        if (lblInstallDir.Text != _state.ServerPath) UpdatePathLabels();
    }

    private async Task PeriodicStatusCheckAsync(CancellationToken ct)
    {
        LogToManager("PERIODIC", "-- Periodic status check --");
        if (InstallService.TestServerInstalled(_state))
        {
            _state.IsInstalled = true;
            var applied = InstallService.GetAppliedVersion(_state);
            if (applied == null)
            {
                var exeVer = InstallService.GetInstalledVersion(_state);
                if (exeVer != null) { InstallService.SetAppliedVersion(_state, exeVer); applied = exeVer; }
            }
            RefreshInstalledLabel();
            SetStatusLabel("lblSetupStatus", "INSTALLED", "green");
        }
        else
        {
            _state.IsInstalled = false;
            SetStatusLabel("lblInstalled", "Not installed", "red");
            SetStatusLabel("lblSetupStatus", "NOT INSTALLED", "red");
            return;
        }

        var proc = ServerProcessService.GetRunningServer(_state);
        if (proc != null)
        {
            _state.IsRunning = true; _state.ExpectedToRun = true;
            try { _state.ServerStartTime = proc.StartTime; } catch { _state.ServerStartTime = DateTime.Now; }
            SetStatusLabel("lblServerStatus", $"RUNNING (PID {proc.Id})", "green");
            var (h, ip) = NetworkHelper.GetServerConnectionInfo(_state.ServerPath);
            SetStatusLabel("lblHostname", h, "white");
            SetStatusLabel("lblIpPort",   ip, "blue");
        }
        else
        {
            _state.IsRunning = false;
            SetStatusLabel("lblServerStatus", "STOPPED", "red");
            SetStatusLabel("lblIpPort", "—", "gray");
        }

        try
        {
            var latest = await UpdateService.FetchLatestVersionAsync(_state.ApiUrl, LogToManager, ct);
            _state.LatestUrl = latest.Url;
            _state.LatestFilename = latest.Filename;
            var verLatest = VersionComparer.ExtractVersionFromFilename(latest.Filename);
            _state.LatestVersion = verLatest;
            SetStatusLabel("lblLatest", verLatest, "blue");

            var current = InstallService.GetAppliedVersion(_state);
            if (current == null)
            {
                LogToManager("WARN", $"Installed version unknown. Syncing tracking file to latest ({verLatest}) to prevent false update loops.");
                InstallService.SetAppliedVersion(_state, verLatest);
                current = verLatest;
            }
            int cmp = VersionComparer.CompareBedrockVersion(current, verLatest);
            if (cmp == 0)
            {
                _state.UpdateAvailable = false;
                SetStatusLabel("lblUpdateStatus", "UP TO DATE", "green");
            }
            else
            {
                _state.UpdateAvailable = true;
                SetStatusLabel("lblUpdateStatus", "UPDATE AVAILABLE", "orange");
                LogToManager("WARN", $"New version available: {verLatest} (current: {current})");
                if (_state.AutoApplyUpdates)
                {
                    LogToManager("SYSTEM", "Auto-apply enabled. Starting update process...");
                    await InstallService.DownloadAndInstallAsync(_state, latest.Url, latest.Filename, false,
                        LogToManager, SetProgress, SetStatusLabel, _ => RefreshInstalledLabel(), ct);
                }
            }
        }
        catch (Exception ex) { LogToManager("WARN", $"Periodic update check failed: {ex.Message}"); }
        LogToManager("PERIODIC", "-- Periodic check done --");
    }

    private void HookButtons()
    {
        btnFirstSetup.Click += (_, _) =>
        {
            if (_state.IsBusy) return;
            StartBackgroundWork(async ct =>
            {
                SetBusy(true); SetProgress("indeterminate");
                try
                {
                    if (InstallService.TestServerInstalled(_state))
                    { LogToManager("WARN", "Server is already installed. Use 'Download and Update' instead."); SetProgress("reset"); return; }

                    LogToManager("HEADER", "-------------------------------------------");
                    LogToManager("HEADER", "  FIRST-TIME SETUP / FRESH INSTALL");
                    LogToManager("HEADER", "-------------------------------------------");
                    InstallService.InitializeDirectories(_state, LogToManager);

                    var latest = await UpdateService.FetchLatestVersionAsync(_state.ApiUrl, LogToManager, ct);
                    _state.LatestUrl = latest.Url; _state.LatestFilename = latest.Filename;
                    var ver = VersionComparer.ExtractVersionFromFilename(latest.Filename);
                    _state.LatestVersion = ver;
                    SetStatusLabel("lblLatest", ver, "blue");

                    await InstallService.DownloadAndInstallAsync(_state, latest.Url, latest.Filename, true,
                        LogToManager, SetProgress, SetStatusLabel, _ => RefreshInstalledLabel(), ct);

                    RefreshInstalledLabel();
                    LogToManager("HEADER", "-------------------------------------------");
                    LogToManager("SUCCESS", "  Setup completed successfully!");
                    LogToManager("HEADER", "-------------------------------------------");
                }
                catch (Exception ex)
                {
                    LogToManager("ERROR", $"Setup FAILED: {ex.Message}");
                    SetStatusLabel("lblSetupStatus", "SETUP FAILED", "red");
                    SetProgress("reset");
                }
                finally { SetBusy(false); }
            });
        };

        btnCheckUpdate.Click += (_, _) =>
        {
            if (_state.IsBusy) return;
            StartBackgroundWork(async ct =>
            {
                SetBusy(true); SetProgress("indeterminate");
                try
                {
                    LogToManager("INFO", "Checking for updates…");
                    if (!InstallService.TestServerInstalled(_state)) { LogToManager("WARN", "No installation found."); return; }
                    RefreshInstalledLabel();
                    var latest = await UpdateService.FetchLatestVersionAsync(_state.ApiUrl, LogToManager, ct);
                    _state.LatestUrl = latest.Url; _state.LatestFilename = latest.Filename;
                    var ver = VersionComparer.ExtractVersionFromFilename(latest.Filename);
                    _state.LatestVersion = ver;
                    SetStatusLabel("lblLatest", ver, "blue");
                    var appliedVer = InstallService.GetAppliedVersion(_state);
                    if (appliedVer == null) { LogToManager("WARN", $"Installed version unknown. Syncing tracking file to latest ({ver}) to prevent false update loops."); InstallService.SetAppliedVersion(_state, ver); appliedVer = ver; }
                    int cmp = VersionComparer.CompareBedrockVersion(appliedVer, ver);
                    if (cmp == 0) { _state.UpdateAvailable = false; LogToManager("SUCCESS", "Already up to date."); SetStatusLabel("lblUpdateStatus", "UP TO DATE", "green"); }
                    else { _state.UpdateAvailable = true; LogToManager("WARN", $"Update available: {ver} (current: {appliedVer})"); SetStatusLabel("lblUpdateStatus", "UPDATE AVAILABLE", "orange"); }
                    var proc = ServerProcessService.GetRunningServer(_state);
                    if (proc != null) { _state.IsRunning = true; SetStatusLabel("lblServerStatus", $"RUNNING (PID {proc.Id})", "green"); }
                    else { _state.IsRunning = false; SetStatusLabel("lblServerStatus", "STOPPED", "red"); }
                }
                catch (Exception ex) { LogToManager("ERROR", $"Error checking for updates: {ex.Message}"); }
                finally { SetProgress("reset"); SetBusy(false); }
            });
        };

        btnUpdate.Click += (_, _) =>
        {
            if (_state.IsBusy || !_state.UpdateAvailable || _state.AutoApplyUpdates) return;
            StartBackgroundWork(async ct =>
            {
                SetBusy(true);
                try
                {
                    var url = _state.LatestUrl; var file = _state.LatestFilename;
                    if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(file)) { LogToManager("ERROR", "No update URL cached."); return; }
                    LogToManager("HEADER", "-------------------------------------------");
                    LogToManager("HEADER", "  STARTING UPDATE PROCESS");
                    LogToManager("HEADER", "-------------------------------------------");
                    await InstallService.DownloadAndInstallAsync(_state, url, file, false, LogToManager, SetProgress, SetStatusLabel, _ => RefreshInstalledLabel(), ct);
                    RefreshInstalledLabel();
                    LogToManager("HEADER", "-------------------------------------------");
                    LogToManager("SUCCESS", "  Update completed successfully!");
                    LogToManager("HEADER", "-------------------------------------------");
                    SetStatusLabel("lblUpdateStatus", "UP TO DATE", "green");
                    _state.UpdateAvailable = false;
                }
                catch (Exception ex) { LogToManager("ERROR", $"Update FAILED: {ex.Message}"); SetStatusLabel("lblUpdateStatus", "UPDATE FAILED", "red"); SetProgress("reset"); }
                finally { SetBusy(false); }
            });
        };

        btnStartServer.Click += (_, _) =>
        {
            if (_state.IsBusy || _state.IsRunning) return;
            StartBackgroundWork(async ct =>
            {
                SetBusy(true);
                try
                {
                    if (!InstallService.TestServerInstalled(_state)) { LogToManager("ERROR", "Executable not found."); return; }
                    await ServerProcessService.StartServerProcessAsync(_state, LogToManager, SetStatusLabel, ct);
                }
                catch (Exception ex) { LogToManager("ERROR", $"Error starting server: {ex.Message}"); }
                finally { SetBusy(false); }
            });
        };

        btnStopServer.Click += (_, _) =>
        {
            if (_state.IsBusy || !_state.IsRunning) return;
            StartBackgroundWork(async _ =>
            {
                SetBusy(true);
                try { if (ServerProcessService.GetRunningServer(_state) != null)
                          await ServerProcessService.StopGameServerAsync(_state, LogToManager, SetStatusLabel); }
                catch (Exception ex) { LogToManager("ERROR", $"Error stopping server: {ex.Message}"); }
                finally { SetBusy(false); }
            });
        };

        btnRefresh.Click += (_, _) =>
        {
            if (_state.IsBusy) return;
            StartBackgroundWork(async _ =>
            {
                SetBusy(true);
                try
                {
                    LogToManager("SYSTEM", "Refreshing status…");
                    if (InstallService.TestServerInstalled(_state))
                    {
                        _state.IsInstalled = true;
                        if (InstallService.GetAppliedVersion(_state) == null) { var exeVer = InstallService.GetInstalledVersion(_state); if (exeVer != null) InstallService.SetAppliedVersion(_state, exeVer); }
                        RefreshInstalledLabel();
                        SetStatusLabel("lblSetupStatus", "INSTALLED", "green");
                    }
                    else
                    {
                        _state.IsInstalled = false;
                        SetStatusLabel("lblInstalled", "Not installed", "red");
                        SetStatusLabel("lblSetupStatus", "NOT INSTALLED", "red");
                    }
                    var proc = ServerProcessService.GetRunningServer(_state);
                    if (proc != null)
                    {
                        _state.IsRunning = true; _state.ExpectedToRun = true;
                        try { _state.ServerStartTime = proc.StartTime; } catch { _state.ServerStartTime = DateTime.Now; }
                        SetStatusLabel("lblServerStatus", $"RUNNING (PID {proc.Id})", "green");
                    }
                    else
                    {
                        _state.IsRunning = false;
                        SetStatusLabel("lblServerStatus", "STOPPED", "red");
                    }
                    LogToManager("SUCCESS", "Status refreshed.");
                }
                catch (Exception ex) { LogToManager("ERROR", $"Error refreshing: {ex.Message}"); }
                finally { SetBusy(false); }
            });
        };

        btnBackupNow.Click += (_, _) =>
        {
            if (_state.IsBusy || !_state.IsInstalled || _state.IsRunning) return;
            StartBackgroundWork(async _ =>
            {
                SetBusy(true); SetProgress("indeterminate");
                try
                {
                    InstallService.InitializeDirectories(_state, LogToManager);
                    BackupService.BackupAll(_state, LogToManager);
                }
                catch (Exception ex) { LogToManager("ERROR", $"Manual backup failed: {ex.Message}"); }
                finally { SetProgress("reset"); SetBusy(false); }
            });
        };

        btnRestoreBackup.Click += (_, _) =>
        {
            if (_state.IsBusy || !_state.IsInstalled || _state.IsRunning) return;
            var backupRoot = _state.BackupPath;
            if (!Directory.Exists(backupRoot)) { System.Windows.MessageBox.Show("Backup directory does not exist.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog { InitialDirectory = backupRoot, Filter = "Zip Archives (*.zip)|*.zip", Title = "Select a backup to restore" };
            if (dlg.ShowDialog() == true)
            {
                var selectedZip = dlg.FileName;
                
                // Use our custom themed dialog instead of the default MessageBox
                var confirmDialog = new RestoreConfirmWindow(selectedZip)
                {
                    Owner = this // Centers the dialog on the main window
                };
                
                if (confirmDialog.ShowDialog() == true)
                {
                    _state.RestoreZipPath = selectedZip;
                    StartBackgroundWork(async _ =>
                    {
                        SetBusy(true); SetProgress("indeterminate");
                        try { BackupService.RestoreBackup(_state, _state.RestoreZipPath, LogToManager); }
                        catch (Exception ex) { LogToManager("ERROR", $"Restore failed: {ex.Message}"); }
                        finally { SetProgress("reset"); SetBusy(false); }
                    });
                }
            }
        };

        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select root directory (e.g. C:\\Bedrock)",
                SelectedPath = txtRootPath.Text
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
            {
                txtRootPath.Text = dlg.SelectedPath;
                _state.RootPath = dlg.SelectedPath;
                _state.RecomputePaths();
                UpdatePathLabels();
                SaveSettingsAuto();
            }
        };

        btnOpenFolder.Click += (_, _) =>
        {
            if (Directory.Exists(_state.RootPath))
                Process.Start("explorer.exe", $"\"{_state.RootPath}\"");
            else
                System.Windows.MessageBox.Show("Folder does not exist yet.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        btnClearLog.Click       += (_, _) => rtbLog.Document.Blocks.Clear();
        btnClearServerLog.Click += (_, _) => rtbServerLog.Document.Blocks.Clear();

        btnSendCommand.Click += (_, _) => SendCommandFromTextBox();
        txtServerCommand.KeyDown += TxtServerCommand_KeyDown;

        lblInstallDir.MouseLeftButtonUp += (_, _) => OpenInExplorer(_state.ServerPath);
        lblBackupDir.MouseLeftButtonUp   += (_, _) =>
        {
            if (Directory.Exists(_state.BackupPath)) OpenInExplorer(_state.BackupPath);
            else System.Windows.MessageBox.Show("Backup folder does not exist yet.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        lblLogFile.MouseLeftButtonUp += (_, _) =>
        {
            if (Directory.Exists(_state.LogsPath)) OpenInExplorer(_state.LogsPath);
            else System.Windows.MessageBox.Show("Logs folder does not exist yet.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        btnEditConfig.Click += (_, _) =>
        {
            if (_state.IsBusy) return;
            
            if (!Directory.Exists(_state.ServerPath))
            {
                System.Windows.MessageBox.Show("Please install the server first (Server directory not found).", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string htmlFilePath = Path.Combine(_state.ServerPath, "bedrock_config_editor.html");
                var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ConfigEditor.html", UriKind.Absolute));
                
                if (resourceInfo != null)
                {
                    using (var fileStream = new FileStream(htmlFilePath, FileMode.Create, FileAccess.Write))
                    {
                        resourceInfo.Stream.CopyTo(fileStream);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("Config editor resource not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = htmlFilePath,
                    UseShellExecute = true
                });
                
                LogToManager("SYSTEM", "Opened server.properties web editor in default browser.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open config editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void TxtServerCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendCommandFromTextBox(); e.Handled = true; return; }

        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            var hist = _state.ServerConsoleHistory.ToArray();
            if (hist.Length == 0) return;
            if (e.Key == Key.Up && _commandHistoryIdx > 0) _commandHistoryIdx--;
            else if (e.Key == Key.Down)
            {
                if (_commandHistoryIdx < hist.Length - 1) _commandHistoryIdx++;
                else { txtServerCommand.Text = ""; _commandHistoryIdx = hist.Length; e.Handled = true; return; }
            }
            txtServerCommand.Text = hist[_commandHistoryIdx];
            txtServerCommand.CaretIndex = txtServerCommand.Text.Length;
            e.Handled = true;
        }
    }

    private void SendCommandFromTextBox()
    {
        var cmd = txtServerCommand.Text;
        if (string.IsNullOrWhiteSpace(cmd)) return;
        if (!_state.IsRunning || _state.ServerProcess == null)
        {
            System.Windows.MessageBox.Show("Server is not running or was adopted (no stdin available). Stop and restart from GUI to enable command input.",
                "Cannot send command", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _state.PendingServerCommands.Enqueue(cmd);
        _state.ServerConsoleHistory.Enqueue(cmd);
        _commandHistoryIdx = _state.ServerConsoleHistory.Count;
        txtServerCommand.Clear();
    }

    private void HookSettingsAutoSave()
    {
        // TextChanged causes disk I/O on every keystroke. LostKeyboardFocus is safer.
        txtRootPath.LostKeyboardFocus += (_, _) => SaveSettingsAuto();
        chkAutoStart.Checked        += (_, _) => SaveSettingsAuto();
        chkAutoStart.Unchecked      += (_, _) => SaveSettingsAuto();
        chkAutoLaunch.Checked       += (_, _) => SaveSettingsAuto();
        chkAutoLaunch.Unchecked     += (_, _) => SaveSettingsAuto();
        chkCrashProtect.Checked     += (_, _) => SaveSettingsAuto();
        chkCrashProtect.Unchecked   += (_, _) => SaveSettingsAuto();
        
        // Hook for the new Update Check checkbox
        chkAutoCheckUpdates.Click += (s, e) =>
        {
            if (chkAutoCheckUpdates.IsChecked == true)
            {
                var dlg = new ScheduleUpdateCheckWindow(_state) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _state.AutoCheckUpdates = true;
                    _state.NextUpdateCheckDate = ScheduledRebootService.GetNextRebootDate(_state.UpdateCheckFreq, _state.UpdateCheckDate, _state.UpdateCheckTime);
                    chkAutoApplyUpdates.IsEnabled = true;
                    SaveSettingsAuto();
                }
                else
                {
                    // Revert if they cancel the dialog
                    chkAutoCheckUpdates.IsChecked = false;
                }
            }
            else
            {
                _state.AutoCheckUpdates = false;
                _state.NextUpdateCheckDate = null;
                chkAutoApplyUpdates.IsEnabled = false;
                chkAutoApplyUpdates.IsChecked = false;
                SaveSettingsAuto();
            }
        };

        chkAutoApplyUpdates.Checked   += (_, _) => SaveSettingsAuto();
        chkAutoApplyUpdates.Unchecked += (_, _) => SaveSettingsAuto();
        
        txtMaxBackups.LostKeyboardFocus += (_, _) => SaveSettingsAuto();

        chkScheduleReboot.Checked   += (_, _) =>
        {
            var result = ShowScheduleDialog();
            if (!result) { chkScheduleReboot.IsChecked = false; _state.ScheduleRebootEnabled = false; }
            else _state.ScheduleRebootEnabled = true;
            UpdateNextRebootLabel();
            SaveSettingsAuto();
        };
        chkScheduleReboot.Unchecked += (_, _) =>
        {
            _state.ScheduleRebootEnabled = false;
            UpdateNextRebootLabel();
            SaveSettingsAuto();
        };
    }

    private void SaveSettingsAuto()
    {
        if (!_uiInitialized) return;
        _state.StartAfterUpdate  = chkAutoStart.IsChecked ?? true;
        _state.AutoLaunchOnStart = chkAutoLaunch.IsChecked ?? false;
        _state.CrashProtection   = chkCrashProtect.IsChecked ?? true;
        _state.AutoCheckUpdates  = chkAutoCheckUpdates.IsChecked ?? true;
        _state.AutoApplyUpdates  = chkAutoApplyUpdates.IsChecked ?? false;

        if (int.TryParse(txtMaxBackups.Text, out var bak) && bak >= 1) _state.MaxBackups = bak;

        var newPath = txtRootPath.Text;
        var invalid = Path.GetInvalidPathChars();
        if (!string.IsNullOrWhiteSpace(newPath) && !newPath.Any(c => invalid.Contains(c)))
        {
            _state.RootPath = newPath;
            _state.RecomputePaths();
            UpdatePathLabels();
        }
        ConfigManager.Save(_state);
        AppendLogLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [SYSTEM ] Settings updated and saved automatically.", "SYSTEM");
    }

    private bool ShowScheduleDialog()
    {
        var dlg = new ScheduleRebootWindow(_state) { Owner = this };
        dlg.ShowDialog();
        return dlg.DialogResult == true;
    }

    private void UpdateNextRebootLabel()
    {
        if (_state.ScheduleRebootEnabled)
        {
            var next = ScheduledRebootService.GetNextRebootDate(
                _state.ScheduleRebootFreq, _state.ScheduleRebootDate, _state.ScheduleRebootTime);
            _state.NextRebootDate = next;
            if (next.HasValue)
            {
                lblNextReboot.Text = next.Value.ToString("MMM dd HH:mm");
                dotReboot.Fill = _statusBrushCache["green"];
            }
            else
            {
                lblNextReboot.Text = "Invalid";
                dotReboot.Fill = _statusBrushCache["red"];
            }
        }
        else
        {
            _state.NextRebootDate = null;
            lblNextReboot.Text = "Off";
            dotReboot.Fill = _statusBrushCache["gray"];
        }
    }

    private void StartBackgroundWork(Func<CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        lock (_activeCtsLock) _activeCts.Add(cts);
        Task.Run(async () =>
        {
            try { await work(cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogToManager("ERROR", $"Background work failed: {ex.Message}"); }
            finally { lock (_activeCtsLock) _activeCts.Remove(cts); cts.Dispose(); GC.Collect(); }
        }, cts.Token);
    }

    private void SetStatusLabel(string control, string text, string colour) =>
        _state.PendingStatus.Enqueue(new StatusUpdate(control, text, colour));

    private void SetProgress(string type, int value = 0)
    { lock (_state.ProgressLock) _state.PendingProgress = new ProgressUpdate(type, value); }

    private void SetBusy(bool busy)
    {
        _state.IsBusy = busy;
        _state.PendingButtons.Enqueue(new ButtonUpdate(busy ? "busy" : "free"));
    }

    private void LogToManager(string level, string message)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var entry = $"{ts} [{level.PadRight(7)}] {message}";
        _state.PendingMessages.Enqueue(new LogEntry(entry, level));
        try
        {
            Directory.CreateDirectory(_state.LogsPath);
            var logFile = Path.Combine(_state.LogsPath, $"BedrockServerManager_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, entry + Environment.NewLine);
            foreach (var f in new DirectoryInfo(_state.LogsPath)
                                .GetFiles("BedrockServerManager_*.log")
                                .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-_state.LogRetentionDays)))
                try { f.Delete(); } catch { }
        }
        catch { }
    }

    private void AppendLogLine(string text, string level) =>
        _state.PendingMessages.Enqueue(new LogEntry(text, level));

    private void AppendServerLine(string text, string level) =>
        _state.ServerConsoleMessages.Enqueue(new LogEntry(text, level));

    private void AppendParagraph(System.Windows.Controls.RichTextBox rtb, string text, System.Windows.Media.Brush brush)
    {
        var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        var run = new Run(text) { Foreground = brush };
        para.Inlines.Add(run);
        rtb.Document.Blocks.Add(para);
    }

    private static void TrimBlocks(System.Windows.Controls.RichTextBox rtb, int max)
    {
        while (rtb.Document.Blocks.Count > max)
        {
            var b = rtb.Document.Blocks.FirstBlock;
            rtb.Document.Blocks.Remove(b);
        }
    }

    private static string ClassifyServerLine(string line)
    {
        if (line.Contains("ERROR") || line.Contains("FATAL") || line.Contains("crashed")) return "ERROR";
        if (line.Contains("WARN")  || line.Contains("Warning")) return "WARN";
        if (line.Contains("Player connected") || line.Contains("Player disconnected")
            || line.Contains("Server started") || line.Contains("done")) return "SUCCESS";
        return "INFO";
    }

    private void UpdatePathLabels()
    {
        lblInstallDir.Text = _state.ServerPath;
        lblBackupDir.Text  = _state.BackupPath;
        lblLogFile.Text    = _state.LogsPath;
    }

    private void RefreshInstalledLabel()
    {
        var ver = InstallService.GetInstalledVersion(_state);
        if (ver != null) { SetStatusLabel("lblInstalled", ver, "white"); _state.InstalledVersion = ver; }
    }

    private void UpdateButtonStates()
    {
        if (_state.IsBusy)
        {
            btnFirstSetup.IsEnabled = btnCheckUpdate.IsEnabled = btnUpdate.IsEnabled =
            btnStartServer.IsEnabled = btnStopServer.IsEnabled = btnRefresh.IsEnabled =
            btnBrowse.IsEnabled = btnOpenFolder.IsEnabled = txtRootPath.IsEnabled =
            btnBackupNow.IsEnabled = btnRestoreBackup.IsEnabled = btnEditConfig.IsEnabled = false;
        }
        else
        {
            btnFirstSetup.IsEnabled    = !_state.IsInstalled;
            btnCheckUpdate.IsEnabled   = _state.IsInstalled;
            btnUpdate.IsEnabled        = _state.UpdateAvailable && !_state.AutoApplyUpdates;
            btnStartServer.IsEnabled   = _state.IsInstalled && !_state.IsRunning;
            btnStopServer.IsEnabled    = _state.IsRunning;
            btnRefresh.IsEnabled       = true;
            btnBrowse.IsEnabled        = true;
            btnOpenFolder.IsEnabled    = true;
            txtRootPath.IsEnabled      = true;
            bool cb = _state.IsInstalled && !_state.IsRunning;
            btnBackupNow.IsEnabled     = cb;
            btnRestoreBackup.IsEnabled = cb;
            btnEditConfig.IsEnabled    = _state.IsInstalled;
        }
        bool canSend = _state.IsRunning && _state.ServerProcess != null && !_state.ServerProcess.HasExited;
        txtServerCommand.IsEnabled = canSend;
        btnSendCommand.IsEnabled   = canSend;
    }

    private void OpenInExplorer(string path)
    {
        if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }

    private static DateTime GetPcBootTime()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            using var collection = searcher.Get();
            var mo = collection.Cast<ManagementObject>().First();
            return ManagementDateTimeConverter.ToDateTime((string)mo["LastBootUpTime"]);
        }
        catch { return DateTime.Now; }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_state.IsBusy)
        {
            var r = System.Windows.MessageBox.Show(
                "A background task is currently running. Closing the manager may interrupt it and corrupt files. Are you sure you want to exit?",
                "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        var sp = _state.ServerProcess;
        if (sp != null && !sp.HasExited)
        {
            try { lock (_state.StdInWriteLock) { sp.StandardInput.WriteLine("stop"); sp.StandardInput.Flush(); } } catch { }
            int w = 0;
            while (!sp.HasExited && w < 50) { Thread.Sleep(200); w++; }
            if (!sp.HasExited) { try { sp.Kill(); } catch { } Thread.Sleep(1000); }
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _timer.Stop();
        _state.WindowClosed = true;
        ConfigManager.Save(_state);
        lock (_activeCtsLock)
        {
            foreach (var cts in _activeCts) cts.Cancel();
            _activeCts.Clear();
        }
    }
}