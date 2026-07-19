using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApexTrace.Core;
using Parquet.Serialization;

namespace ApexTrace.Storage;

public sealed record ApexTracePackageManifest(
    int PackageSchemaVersion,
    string AppVersion,
    string? LmuGameVersion,
    string? SharedMemoryHeaderSha256,
    string Track,
    string Vehicle,
    string Session,
    long SampleCount,
    double DurationSeconds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    TelemetryDataSource DataSource,
    bool IsComplete,
    string CompletenessDiagnostic,
    IReadOnlyDictionary<string, string> Files);

public sealed class ApexTracePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> ExportAsync(TelemetrySession session, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!outputPath.EndsWith(".apextrace", StringComparison.OrdinalIgnoreCase))
        {
            outputPath += ".apextrace";
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ApexTrace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            await WritePackageContentsAsync(session, tempRoot, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, outputPath, CompressionLevel.SmallestSize, false);
            return Path.GetFullPath(outputPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)
                && tempRoot.StartsWith(Path.Combine(Path.GetTempPath(), "ApexTrace"), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    public async Task<TelemetrySession> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("session.json") ?? throw new InvalidDataException("session.json is missing from the package.");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<TelemetrySession>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("session.json could not be deserialized.");
    }

    private static async Task WritePackageContentsAsync(TelemetrySession session, string root, CancellationToken cancellationToken)
    {
        foreach (var directory in new[] { "track", "vehicle", "setup", "telemetry", "laps", "corners", "analysis", "preview" })
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }

        await WriteJsonAsync(Path.Combine(root, "session.json"), session, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "track", "track.json"), session.Track, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "vehicle", "vehicle.json"), new
        {
            schemaVersion = 1,
            session.Metadata.VehicleName,
            session.Metadata.VehicleClass
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "setup", "setup.json"), new
        {
            schemaVersion = 1,
            source = "LMU native metadata, read-only",
            raw = session.Metadata.SetupJson
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "analysis", "summary.json"), new
        {
            schemaVersion = 1,
            session.Metadata.IsComplete,
            session.Metadata.CompletenessDiagnostic,
            sampleCount = session.Samples.Count,
            lapCount = session.Laps.Count,
            recommendationCount = session.Recommendations.Count
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "analysis", "recommendations.json"), session.Recommendations, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "analysis", "target-line.json"), new
        {
            schemaVersion = 1,
            available = false,
            reason = session.Metadata.IsComplete ? "Not generated in first runnable version." : "No complete laps; target line intentionally withheld."
        }, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "track", "track.svg"), BuildTrackSvg(session.Track), cancellationToken);
        await WriteTelemetryCsvAsync(Path.Combine(root, "telemetry", "samples.csv"), session.Samples, cancellationToken);
        await WriteTelemetryParquetAsync(Path.Combine(root, "telemetry", "samples.parquet"), session.Samples, cancellationToken);
        await WriteLapsCsvAsync(Path.Combine(root, "laps", "laps.csv"), session.Laps, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "corners", "corners.csv"), "corner,entry_m,apex_m,exit_m,confidence\n", cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, "preview", "session.png"), Convert.FromBase64String(TransparentPng), cancellationToken);

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith("checksums.sha256", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                file => Path.GetRelativePath(root, file).Replace('\\', '/'),
                HashFile,
                StringComparer.Ordinal);
        var manifest = new ApexTracePackageManifest(
            1,
            "0.1.0",
            session.Metadata.GameVersion,
            session.Metadata.SharedMemoryHeaderSha256,
            session.Metadata.TrackName,
            session.Metadata.VehicleName,
            session.Metadata.SessionType,
            session.Samples.Count,
            session.DurationSeconds,
            session.Metadata.StartedAtUtc,
            session.Metadata.EndedAtUtc,
            session.Metadata.DataSource,
            session.Metadata.IsComplete,
            session.Metadata.CompletenessDiagnostic,
            files);
        await WriteJsonAsync(Path.Combine(root, "manifest.json"), manifest, cancellationToken);
        var checksumLines = files.Select(pair => $"{pair.Value}  {pair.Key}")
            .Append($"{HashFile(Path.Combine(root, "manifest.json"))}  manifest.json");
        await File.WriteAllLinesAsync(Path.Combine(root, "checksums.sha256"), checksumLines, cancellationToken);
    }

    private static async Task WriteTelemetryParquetAsync(string path, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken)
    {
        var rows = samples.Select(sample => new TelemetryParquetRow
        {
            Sequence = sample.Sequence,
            SessionElapsedSeconds = sample.SessionElapsedSeconds,
            LapNumber = sample.LapNumber,
            LapDistanceMeters = sample.LapDistanceMeters,
            WorldX = sample.WorldPosition.X,
            WorldY = sample.WorldPosition.Y,
            WorldZ = sample.WorldPosition.Z,
            SpeedMetersPerSecond = sample.SpeedMetersPerSecond,
            Gear = sample.Gear,
            EngineRpm = sample.EngineRpm,
            Throttle = sample.Throttle,
            Brake = sample.Brake,
            Steering = sample.Steering
        }).ToArray();
        await using var stream = File.Create(path);
        await ParquetSerializer.SerializeAsync(rows, stream, cancellationToken: cancellationToken);
    }

    private static async Task WriteTelemetryCsvAsync(string path, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync("sequence,elapsed_s,lap,lap_distance_m,world_x_m,world_y_m,world_z_m,speed_mps,gear,rpm,throttle,brake,steering,fuel_l");
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', new[]
            {
                sample.Sequence.ToString(CultureInfo.InvariantCulture),
                sample.SessionElapsedSeconds.ToString("F3", CultureInfo.InvariantCulture),
                sample.LapNumber.ToString(CultureInfo.InvariantCulture),
                sample.LapDistanceMeters.ToString("F3", CultureInfo.InvariantCulture),
                sample.WorldPosition.X.ToString("F4", CultureInfo.InvariantCulture),
                sample.WorldPosition.Y.ToString("F4", CultureInfo.InvariantCulture),
                sample.WorldPosition.Z.ToString("F4", CultureInfo.InvariantCulture),
                sample.SpeedMetersPerSecond.ToString("F4", CultureInfo.InvariantCulture),
                sample.Gear.ToString(CultureInfo.InvariantCulture),
                sample.EngineRpm.ToString("F1", CultureInfo.InvariantCulture),
                sample.Throttle.ToString("F4", CultureInfo.InvariantCulture),
                sample.Brake.ToString("F4", CultureInfo.InvariantCulture),
                sample.Steering.ToString("F4", CultureInfo.InvariantCulture),
                sample.FuelLiters.ToString("F3", CultureInfo.InvariantCulture)
            }));
        }
    }

    private static async Task WriteLapsCsvAsync(string path, IReadOnlyList<LapRecord> laps, CancellationToken cancellationToken)
    {
        var lines = new[] { "lap,started_s,ended_s,lap_time_s,valid,complete,samples" }
            .Concat(laps.Select(lap => string.Join(',', lap.LapNumber,
                lap.StartedAtSeconds.ToString("F3", CultureInfo.InvariantCulture),
                lap.EndedAtSeconds.ToString("F3", CultureInfo.InvariantCulture),
                lap.LapTimeSeconds.ToString("F3", CultureInfo.InvariantCulture),
                lap.IsValid, lap.IsComplete, lap.SampleCount)));
        await File.WriteAllLinesAsync(path, lines, cancellationToken);
    }

    private static string BuildTrackSvg(TrackDefinition track)
    {
        if (track.CenterLine.Count == 0)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"450\"/>";
        }

        var minX = track.CenterLine.Min(point => point.X);
        var maxX = track.CenterLine.Max(point => point.X);
        var minY = track.CenterLine.Min(point => point.Y);
        var maxY = track.CenterLine.Max(point => point.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var points = string.Join(' ', track.CenterLine.Select(point =>
            $"{(20 + (point.X - minX) / width * 760).ToString("F2", CultureInfo.InvariantCulture)},{(20 + (point.Y - minY) / height * 410).ToString("F2", CultureInfo.InvariantCulture)}"));
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"450\" viewBox=\"0 0 800 450\"><rect width=\"800\" height=\"450\" fill=\"#07111D\"/><polyline points=\"{points}\" fill=\"none\" stroke=\"#2B8CFF\" stroke-width=\"5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>";
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private const string TransparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+Q8u1VwAAAABJRU5ErkJggg==";

    private sealed class TelemetryParquetRow
    {
        public long Sequence { get; set; }
        public double SessionElapsedSeconds { get; set; }
        public int LapNumber { get; set; }
        public double LapDistanceMeters { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public double WorldZ { get; set; }
        public double SpeedMetersPerSecond { get; set; }
        public int Gear { get; set; }
        public double EngineRpm { get; set; }
        public double Throttle { get; set; }
        public double Brake { get; set; }
        public double Steering { get; set; }
    }
}
