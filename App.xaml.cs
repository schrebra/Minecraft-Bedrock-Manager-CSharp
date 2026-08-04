using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;

namespace BedrockServerManager;

public partial class App : System.Windows.Application
{
    public static string[] StartupArgs { get; private set; }

    private static string UiSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BedrockServerManager", "ui_settings.ini");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        StartupArgs = e.Args;
        
        // Check for Admin
        if (!IsAdmin())
        {
            // Check if the user previously chose "Don't show me again"
            if (!GetDontShowAdminWarning())
            {
                int shownCount = GetAdminWarningCount();
                
                var adminWarning = new AdminWarningWindow
                {
                    // Show the checkbox only if this is the 2nd launch (or later)
                    ShowDontShowAgain = shownCount >= 1
                };
                
                adminWarning.ShowDialog();
                
                // Increment the count and save it
                SetAdminWarningCount(shownCount + 1);

                // If they checked the box, save the preference
                if (adminWarning.DontShowAgain)
                {
                    SetDontShowAdminWarning(true);
                }

                // If they requested to relaunch as admin
                if (adminWarning.RelaunchRequested)
                {
                    bool relaunched = false;
                    try
                    {
                        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;
                        var startInfo = new ProcessStartInfo(exePath)
                        {
                            UseShellExecute = true,
                            Verb = "runas" // Triggers the UAC prompt
                        };

                        // Pass along any original startup arguments
                        foreach (var arg in e.Args)
                        {
                            startInfo.ArgumentList.Add(arg);
                        }

                        Process.Start(startInfo);
                        relaunched = true;
                    }
                    catch (Exception)
                    {
                        // User clicked 'No' on the UAC prompt or it failed silently
                        System.Windows.MessageBox.Show("Could not relaunch as Administrator. Continuing in standard mode.", "Relaunch Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    
                    if (relaunched)
                    {
                        // Shut down the current non-admin instance
                        Shutdown();
                        return;
                    }
                }
            }
        }

        if (!IsVcRedistInstalled())
        {
            var result = System.Windows.MessageBox.Show("Visual C++ Redistributable is not installed. Minecraft Bedrock Server requires it to run. Would you like to download and install it now?", "Missing Dependency", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
                    var tempFile = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
                    
                    using (var client = new HttpClient())
                    {
                        var response = client.GetAsync(url).GetAwaiter().GetResult();
                        response.EnsureSuccessStatusCode();
                        
                        using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                        {
                            response.Content.ReadAsStream().CopyTo(fs);
                        }
                    }

                    var proc = Process.Start(tempFile, "/install /passive /norestart");
                    proc.WaitForExit();
                    System.Windows.MessageBox.Show("Visual C++ Redistributable installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to download or install the dependency: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow; 
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private static bool IsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static bool IsVcRedistInstalled()
    {
        var regPaths = new[]
        {
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
            @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64"
        };
        foreach (var p in regPaths)
        {
            using var key = Registry.LocalMachine.OpenSubKey(p);
            if (key?.GetValue("Installed") is int val && val == 1) return true;
        }
        return false;
    }

    private static int GetAdminWarningCount()
    {
        try
        {
            if (File.Exists(UiSettingsPath))
            {
                var lines = File.ReadAllLines(UiSettingsPath);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("AdminWarningShownCount=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring("AdminWarningShownCount=".Length).Trim(), out int count))
                            return count;
                    }
                }
            }
        }
        catch { }
        return 0;
    }

    private static void SetAdminWarningCount(int count)
    {
        try
        {
            var dir = Path.GetDirectoryName(UiSettingsPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var lines = new List<string>();
            bool found = false;

            if (File.Exists(UiSettingsPath))
            {
                lines = File.ReadAllLines(UiSettingsPath).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("AdminWarningShownCount=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"AdminWarningShownCount={count}";
                        found = true;
                        break;
                    }
                }
            }
            
            if (!found)
            {
                lines.Add($"AdminWarningShownCount={count}");
            }

            File.WriteAllLines(UiSettingsPath, lines);
        }
        catch { }
    }

    private static bool GetDontShowAdminWarning()
    {
        try
        {
            if (File.Exists(UiSettingsPath))
            {
                var lines = File.ReadAllLines(UiSettingsPath);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("DontShowAdminWarning=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("DontShowAdminWarning=".Length).Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static void SetDontShowAdminWarning(bool value)
    {
        try
        {
            var dir = Path.GetDirectoryName(UiSettingsPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var lines = new List<string>();
            bool found = false;

            if (File.Exists(UiSettingsPath))
            {
                lines = File.ReadAllLines(UiSettingsPath).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("DontShowAdminWarning=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"DontShowAdminWarning={value}";
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                lines.Add($"DontShowAdminWarning={value}");
            }

            File.WriteAllLines(UiSettingsPath, lines);
        }
        catch { }
    }
}