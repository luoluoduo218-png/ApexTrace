using System.Security.Cryptography;
using System.Text.Json;

namespace ApexTrace.Setup;

public sealed record SetupValue(string Key, string DisplayValue, double NumericValue, bool Available);
public sealed record SetupSnapshot(int SchemaVersion, string Source, string SourceHash, IReadOnlyList<SetupValue> Values, string Diagnostic);

public static class SetupSnapshotProbe
{
    public static SetupSnapshot FromLmuMetadataJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SetupSnapshot(1, "Unavailable", string.Empty, [], "没有可用的 LMU 调教元数据；不生成调教建议。" );
        }

        using var document = JsonDocument.Parse(json);
        var values = new List<SetupValue>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.StartsWith("VM_", StringComparison.Ordinal)
                && !property.Name.StartsWith("WM_", StringComparison.Ordinal))
            {
                continue;
            }

            var value = property.Value;
            values.Add(new SetupValue(
                property.Name,
                value.TryGetProperty("stringValue", out var display) ? display.GetString() ?? string.Empty : string.Empty,
                value.TryGetProperty("value", out var numeric) && numeric.TryGetDouble(out var number) ? number : 0,
                !value.TryGetProperty("available", out var available) || available.GetBoolean()));
        }

        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        return new SetupSnapshot(1, "LMU native DuckDB metadata (read-only)", hash, values,
            "调教快照来自 LMU 官方原生遥测元数据；ApexTrace 不修改游戏调教文件。" );
    }

    public static SetupSnapshot ProbeSvmReadOnly(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new SetupSnapshot(1, Path.GetFullPath(path), hash, [],
            "SVM 文件仅完成哈希与存在性探测；未知字段未解释，原文件未修改。" );
    }
}
