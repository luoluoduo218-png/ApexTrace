using System.Security.Cryptography;

namespace ApexTrace.Track;

public sealed record LmuTrackManifest(
    string ComponentName,
    string Version,
    string Author,
    string Signature,
    IReadOnlyList<string> MasFiles,
    bool ProtectedContent,
    string ManifestSha256,
    string Diagnostic);

public static class LmuTrackManifestProbe
{
    public static LmuTrackManifest Probe(string mftPath)
    {
        var lines = File.ReadAllLines(mftPath);
        var values = lines
            .Where(line => line.Contains('='))
            .Select(line => line.Split('=', 2))
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(parts => parts[1]).ToArray(), StringComparer.OrdinalIgnoreCase);

        var masFiles = values.TryGetValue("MASFile", out var entries) ? entries : [];
        using var stream = File.OpenRead(mftPath);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new LmuTrackManifest(
            First(values, "Name"),
            First(values, "Version"),
            First(values, "Author"),
            First(values, "Signature"),
            masFiles,
            true,
            hash,
            "MFT 元数据可合法读取；正式 MAS 内容未尝试解密，赛道几何将降级为运行时重建。" );
    }

    private static string First(IReadOnlyDictionary<string, string[]> values, string key) =>
        values.TryGetValue(key, out var entries) ? entries.FirstOrDefault() ?? string.Empty : string.Empty;
}
