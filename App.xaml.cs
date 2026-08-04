using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using BedrockServerManager.Models;
using BedrockServerManager.Services;

namespace BedrockServerManager;

public partial class App : System.Windows.Application
{
    public static string[] StartupArgs { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        StartupArgs = e.Args;
        
        var tempState = new SharedState();
        for (int i = 0; i < e.Args.Length - 1; i++)
            if (e.Args[i].Equals("-RootPath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Args[i + 1]))
                tempState.RootPath = e.Args[i + 1];

        ConfigManager.Load(tempState);

        // Check for Admin
        if (!IsAdmin())
        {
            // Only show the dialog if the user hasn't dismissed it permanently
            if (!tempState.DontShowAdminWarning)
            {
                var adminWarning = new AdminWarningWindow();
                adminWarning.ShowDialog();
                
                // If they checked "Don't show me again" OR clicked "Relaunch as Admin", save that preference
                if (adminWarning.DontShowAgain)
                {
                    tempState.DontShowAdminWarning = true;
                    ConfigManager.Save(tempState);
                }

                if (adminWarning.RelaunchRequested)
                {
                    bool relaunched = false;
                    try
                    {
                        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;
                        var startInfo = new ProcessStartInfo(exePath)
                        {
                            UseShellExecute = true,
                            Verb = "runas"
                        };

                        foreach (var arg in e.Args)
                        {
                            startInfo.ArgumentList.Add(arg);
                        }

                        Process.Start(startInfo);
                        relaunched = true;
                    }
                    catch (Exception)
                    {
                        System.Windows.MessageBox.Show("Could not relaunch as Administrator. Continuing in standard mode.", "Relaunch Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    
                    if (relaunched)
                    {
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
}