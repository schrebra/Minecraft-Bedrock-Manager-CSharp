using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BedrockServerManager.Models;

namespace BedrockServerManager.Services;

public static class UpdateService
{
    public sealed record LatestVersionInfo(string Url, string Filename);

    public static async Task<LatestVersionInfo> FetchLatestVersionAsync(
        string apiUrl, Action<string, string> log, CancellationToken ct = default)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        int maxRetries = 3;
        Exception last = null;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var http = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });
                http.Timeout = TimeSpan.FromSeconds(15);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                using var resp = await http.GetAsync(apiUrl, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                foreach (var link in doc.RootElement.GetProperty("result").GetProperty("links").EnumerateArray())
                {
                    if (link.GetProperty("downloadType").GetString() == "serverBedrockWindows")
                    {
                        var url = link.GetProperty("downloadUrl").GetString();
                        return new LatestVersionInfo(url, Path.GetFileName(new Uri(url).LocalPath));
                    }
                }
                throw new Exception("API did not return a serverBedrockWindows download URL.");
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt + 1 < maxRetries)
                {
                    log("WARN", $"API call failed (Attempt {attempt + 1}/{maxRetries}): {ex.Message}. Retrying in 5 seconds...");
                    await Task.Delay(5000, ct);
                }
            }
        }
        throw new Exception($"Failed to contact Minecraft API after {maxRetries} attempts: {last?.Message}");
    }
}
