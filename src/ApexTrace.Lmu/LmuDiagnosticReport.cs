using System.Text.Json;
using ApexTrace.Core;

namespace ApexTrace.Lmu;

public static class LmuDiagnosticReport
{
    public static async Task<string> ExportAsync(LmuConnectionStatus status, string outputPath, CancellationToken cancellationToken = default)
    {
        var safeReport = new
        {
            schemaVersion = 1,
            createdAtUtc = DateTimeOffset.UtcNow,
            status.ProcessDetected,
            status.ProcessId,
            status.SharedMemoryAvailable,
            status.HeaderSupported,
            status.HeaderSha256,
            status.HeaderVersion,
            status.Diagnostic,
            accessMode = "MemoryMappedFileRights.Read",
            prohibitedMethods = new[] { "process injection", "game memory write", "legacy rFactor 2 shared-memory DLL" }
        };

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(safeReport, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return outputPath;
    }
}
