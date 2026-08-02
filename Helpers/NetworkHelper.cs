using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace BedrockServerManager.Helpers;

public static class NetworkHelper
{
    public static string GetHostName() => Dns.GetHostName();

    public static string GetServerPort(string serverPath)
    {
        var port = "19132";
        var propsPath = Path.Combine(serverPath, "server.properties");
        if (File.Exists(propsPath))
        {
            foreach (var line in File.ReadAllLines(propsPath))
            {
                var m = Regex.Match(line, @"^server-port=(\d+)");
                if (m.Success) { port = m.Groups[1].Value; break; }
            }
        }
        return port;
    }

    public static (string Hostname, string IpPort) GetServerConnectionInfo(string serverPath)
    {
        var port = GetServerPort(serverPath);
        var ip = GetActiveIpv4Address() ?? "127.0.0.1";
        return (Dns.GetHostName(), $"{ip}:{port}");
    }

    public static string GetActiveIpv4Address()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            
            var name = ni.Name;
            if (name.Contains("VMware") || name.Contains("VirtualBox") || name.Contains("vEthernet") ||
                name.Contains("Hyper-V") || name.Contains("Docker") || name.Contains("WSL") || 
                name.Contains("TAP") || name.Contains("Tun"))
                continue;

            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var addr = ip.Address.ToString();
                if (addr.StartsWith("169.")) continue;
                if (addr == "127.0.0.1") continue;
                return addr;
            }
        }
        return null;
    }
}