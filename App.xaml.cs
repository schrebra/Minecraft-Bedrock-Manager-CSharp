using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace BedrockServerManager;

public partial class App : System.Windows.Application
{
    public static string[] StartupArgs { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;
        
        // Check for Admin and VC++ Redist
        if (!IsAdmin())
        {
            System.Windows.MessageBox.Show("This application requires Administrator privileges to manage the firewall and network settings. Please run as Administrator.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    using (var client = new System.Net.WebClient())
                    {
                        client.DownloadFile(url, tempFile);
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

        base.OnStartup(e);
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