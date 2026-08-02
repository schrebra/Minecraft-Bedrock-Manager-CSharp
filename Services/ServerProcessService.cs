using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BedrockServerManager.Helpers;
using BedrockServerManager.Models;

namespace BedrockServerManager.Services;

public static class ServerProcessService
{
    public static Process GetRunningServer(SharedState s)
    {
        if (s.ServerProcess != null && !s.ServerProcess.HasExited) return s.ServerProcess;

        if (s.ServerProcessId is int pid)
        {
            try
            {
                var byId = Process.GetProcessById(pid);
                if (!byId.HasExited) return byId;
            }
            catch { }
        }

        var exeName  = Path.GetFileNameWithoutExtension(s.ServerExecutable);
        var exePath  = Path.Combine(s.ServerPath, s.ServerExecutable);
        var procs     = Process.GetProcessesByName(exeName);
        if (procs.Length == 0) return null;
        if (procs.Length == 1)
        {
            try { if (!string.IsNullOrEmpty(procs[0].MainModule?.FileName) &&
                      !procs[0].MainModule.FileName.Equals(exePath, StringComparison.OrdinalIgnoreCase)) return null; }
            catch { }
            return procs[0];
        }
        foreach (var p in procs)
        {
            try { if (p.MainModule?.FileName == exePath) return p; } catch { }
        }
        return null;
    }

    public static async Task StartServerProcessAsync(SharedState s, Action<string, string> log, Action<string, string, string> setStatus, CancellationToken ct = default)
    {
        var exe = Path.Combine(s.ServerPath, s.ServerExecutable);
        if (!File.Exists(exe)) { log("ERROR", $"Executable not found at {exe}"); return; }

        var exeName = Path.GetFileNameWithoutExtension(s.ServerExecutable);
        var existing = Process.GetProcessesByName(exeName).FirstOrDefault(p =>
        {
            try { return p.MainModule?.FileName == exe; } catch { return false; }
        });

        if (existing != null && (s.ServerProcess == null || s.ServerProcess.HasExited))
        {
            FirewallHelper.EnsureRule(exe, log);
            s.IsRunning = true; s.ExpectedToRun = true;
            s.ServerProcess = null; s.ServerProcessId = existing.Id;
            try { s.ServerStartTime = existing.StartTime; } catch { s.ServerStartTime = DateTime.Now; }
            setStatus("lblServerStatus", $"RUNNING (PID {existing.Id} - adopted, no stdin)", "green");
            var (h, ip) = NetworkHelper.GetServerConnectionInfo(s.ServerPath);
            setStatus("lblHostname", h, "white");
            setStatus("lblIpPort",   ip, "blue");
            log("WARN", $"Server is already running (PID {existing.Id}). Adopted process - stdin wrapper unavailable.");
            return;
        }

        if (s.ServerProcess != null && !s.ServerProcess.HasExited)
        { log("WARN", $"Server already running through wrapper (PID {s.ServerProcess.Id})."); return; }

        FirewallHelper.EnsureRule(exe, log);
        log("SYSTEM", "Starting server with stdin/stdout wrapper...");
        setStatus("lblServerStatus", "STARTING...", "orange");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = s.ServerPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var reader = new BedrockProcessReader();
        reader.Attach(proc);
        s.ServerOutputReader = reader;

        bool started;
        try { started = proc.Start(); }
        catch (Exception ex)
        {
            log("ERROR", $"Failed to start server process: {ex.Message}");
            setStatus("lblServerStatus", "START FAILED", "red");
            s.ServerOutputReader = null;
            return;
        }
        if (!started)
        {
            log("ERROR", "Failed to start server process (Start() returned false).");
            setStatus("lblServerStatus", "START FAILED", "red");
            s.ServerOutputReader = null;
            return;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        s.ServerProcess = proc;
        s.ServerProcessId = proc.Id;

        log("SYSTEM", "Waiting for server to initialize (up to 45 seconds)...");
        Process runningProc = null;
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(3000, ct);
            if (proc.HasExited) { log("ERROR", $"Server process exited prematurely (code {proc.ExitCode})."); break; }
            try
            {
                var chk = Process.GetProcessById(proc.Id);
                if (!chk.HasExited) { runningProc = chk; break; }
            }
            catch { }
        }

        if (runningProc != null)
        {
            s.IsRunning = true; s.ExpectedToRun = true;
            try { s.ServerStartTime = runningProc.StartTime; } catch { s.ServerStartTime = DateTime.Now; }
            setStatus("lblServerStatus", $"RUNNING (PID {runningProc.Id})", "green");
            var (h, ip) = NetworkHelper.GetServerConnectionInfo(s.ServerPath);
            setStatus("lblHostname", h, "white");
            setStatus("lblIpPort",   ip, "blue");
            log("SUCCESS", $"Server is listening on {h} ({ip})");
        }
        else
        {
            s.IsRunning = false; s.ExpectedToRun = false;
            try { proc.Dispose(); } catch { }
            s.ServerProcess = null; s.ServerProcessId = null; s.ServerOutputReader = null;
            setStatus("lblServerStatus", "START FAILED", "red");
            log("ERROR", "Server process exited or did not respond in time.");
        }
    }

    public static async Task StopGameServerAsync(SharedState s, Action<string, string> log, Action<string, string, string> setStatus)
    {
        var proc = GetRunningServer(s);
        if (proc == null)
        {
            s.IsRunning = false; s.ExpectedToRun = false;
            s.ServerStartTime = null; s.ServerProcess = null; s.ServerProcessId = null;
            return;
        }
        log("WARN", $"Stopping server (PID {proc.Id})...");
        setStatus("lblServerStatus", "STOPPING...", "orange");

        var sp = s.ServerProcess;
        if (sp != null && !sp.HasExited)
        {
            try
            {
                lock (s.StdInWriteLock)
                {
                    sp.StandardInput.WriteLine("stop");
                    sp.StandardInput.Flush();
                }
                log("SYSTEM", "Sent 'stop' command via stdin (graceful shutdown).");
            }
            catch (Exception ex) { log("WARN", $"Could not send stdin 'stop': {ex.Message}"); }
        }

        int elapsed = 0;
        while (GetRunningServer(s) != null && elapsed < s.ServerStopTimeout)
        {
            await Task.Delay(1000);
            elapsed++;
        }

        if (GetRunningServer(s) != null)
        {
            log("WARN", $"Server did not exit in {s.ServerStopTimeout}s. Force-killing...");
            try { GetRunningServer(s)?.Kill(true); } catch (Exception ex) { log("WARN", $"Force-kill error: {ex.Message}"); }
            await Task.Delay(2000);
        }

        if (s.ServerProcess != null) { try { s.ServerProcess.Dispose(); } catch { } s.ServerProcess = null; }
        s.ServerProcessId = null;
        s.ServerOutputReader = null;
        s.IsRunning = false; s.ExpectedToRun = false;
        s.ServerStartTime = null;

        log("SUCCESS", "Server stopped.");
        setStatus("lblServerStatus", "STOPPED", "red");
        setStatus("lblIpPort", "—", "gray");
    }
}