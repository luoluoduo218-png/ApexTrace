using System.Diagnostics;
using System.Security.Cryptography;
using ApexTrace.Core;

namespace ApexTrace.Lmu;

public sealed class LmuInstallationProbe
{
    public const string DefaultInstallationPath = @"D:\SteamLibrary\steamapps\common\Le Mans Ultimate";

    public LmuConnectionStatus Probe(string? installationPath = null)
    {
        installationPath ??= DefaultInstallationPath;
        var headerDirectory = Path.Combine(installationPath, "Support", "SharedMemoryInterface");
        var sharedHeader = Path.Combine(headerDirectory, "SharedMemoryInterface.hpp");
        var internalsHeader = Path.Combine(headerDirectory, "InternalsPlugin.hpp");
        var pluginHeader = Path.Combine(headerDirectory, "PluginObjects.hpp");

        var process = FindGameProcess();
        var mainHash = HashIfPresent(sharedHeader);
        var internalsHash = HashIfPresent(internalsHeader);
        var pluginHash = HashIfPresent(pluginHeader);
        var supported = string.Equals(mainHash, LmuHeaderLayoutV1.SharedMemoryHeaderSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(internalsHash, LmuHeaderLayoutV1.InternalsHeaderSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pluginHash, LmuHeaderLayoutV1.PluginObjectsHeaderSha256, StringComparison.OrdinalIgnoreCase);

        var memoryAvailable = false;
        if (process is not null && supported)
        {
            memoryAvailable = LmuSharedMemoryReader.CanOpenReadOnly();
        }

        var diagnostic = !Directory.Exists(headerDirectory)
            ? "未找到官方 Support\\SharedMemoryInterface 目录。"
            : !supported
                ? "官方 Header 哈希未被当前解析器识别；为避免盲读，实时采集已安全禁用。"
                : process is null
                    ? "已验证官方 Header；LMU 当前未运行，可只读导入原生 DuckDB 遥测。"
                    : !memoryAvailable
                        ? "已检测到 LMU，但 LMU_Data 尚不可读。请在 Gameplay 中启用 Plugins 并进入赛道。"
                        : "已通过只读权限打开 LMU_Data，Header 与 V1 映射匹配。";

        return new LmuConnectionStatus(
            process is not null,
            process?.Id,
            memoryAvailable,
            supported,
            installationPath,
            mainHash,
            supported ? "LMU V1 / pack=4 / x64" : "Unknown",
            diagnostic);
    }

    public static IReadOnlyList<string> FindNativeTelemetryFiles(string installationPath)
    {
        var directory = Path.Combine(installationPath, "UserData", "Telemetry");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.duckdb", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private static Process? FindGameProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("LeMans", StringComparison.OrdinalIgnoreCase)
                    || process.ProcessName.Contains("Le Mans Ultimate", StringComparison.OrdinalIgnoreCase)
                    || process.MainWindowTitle.Contains("Le Mans Ultimate", StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static string HashIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
