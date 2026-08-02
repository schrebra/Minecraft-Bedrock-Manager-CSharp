using System;
using System.Diagnostics;

namespace BedrockServerManager.Helpers;

public static class FirewallHelper
{
    public const string RuleName = "Minecraft Bedrock Server";

    public static bool IsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static void EnsureRule(string exePath, Action<string, string> log)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh",
                $"advfirewall firewall show rule name=\"{RuleName}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            bool exists = output.Contains(RuleName);

            if (!exists)
            {
                if (!IsAdmin()) { log("WARN", $"Firewall rule '{RuleName}' missing. Run as Administrator to create it."); return; }
                RunNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" profile=any");
                log("SYSTEM", $"Firewall rule '{RuleName}' created for: {exePath}");
            }
            else if (!output.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAdmin()) { log("WARN", $"Firewall rule points to a different path. Re-run as Administrator to update."); return; }
                RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
                RunNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" profile=any");
                log("SYSTEM", $"Firewall rule '{RuleName}' updated to: {exePath}");
            }
        }
        catch (Exception ex) { log("ERROR", $"Failed to apply firewall rule: {ex.Message}"); }
    }

    private static void RunNetsh(string args)
    {
        var psi = new ProcessStartInfo("netsh", args)
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi); p.WaitForExit();
    }
}