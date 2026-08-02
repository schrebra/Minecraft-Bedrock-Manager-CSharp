using System;
using System.Text.RegularExpressions;

namespace BedrockServerManager.Helpers;

public static class VersionComparer
{
    public static int CompareBedrockVersion(string v1, string v2)
    {
        if (string.IsNullOrWhiteSpace(v1) && string.IsNullOrWhiteSpace(v2)) return 0;
        if (string.IsNullOrWhiteSpace(v1)) return -1;
        if (string.IsNullOrWhiteSpace(v2)) return 1;

        var p1 = v1.Split('.');
        var p2 = v2.Split('.');
        int max = Math.Max(p1.Length, p2.Length);
        for (int i = 0; i < max; i++)
        {
            int a = i < p1.Length && int.TryParse(p1[i], out var x) ? x : 0;
            int b = i < p2.Length && int.TryParse(p2[i], out var y) ? y : 0;
            if (a > b) return 1;
            if (a < b) return -1;
        }
        return 0;
    }

    public static string ExtractVersionFromFilename(string filename)
    {
        var m = Regex.Match(filename, @"bedrock-server-(.+?)\.zip");
        return m.Success ? m.Groups[1].Value : filename;
    }
}
