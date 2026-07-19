using System.Text.Json;
using ApexTrace.Core;

namespace ApexTrace.Storage;

public sealed record StoredSessionInfo(
    string Path,
    SessionMetadata Metadata,
    long FileSizeBytes,
    DateTime LastModifiedLocal,
    int SampleCount,
    int CompleteLapCount,
    int ValidLapCount,
    double DurationSeconds,
    IReadOnlyList<TrackPoint> TrackPoints,
    double? BestLapTimeSeconds,
    double TrackLengthMeters,
    double AmbientTemperatureCelsius,
    double TrackTemperatureCelsius)
{
    public string LapCountText => $"{CompleteLapCount} / {ValidLapCount}";
    public string FileSizeText => FileSizeBytes >= 1_048_576
        ? $"{FileSizeBytes / 1_048_576d:F1} MB"
        : $"{FileSizeBytes / 1024d:F1} KB";
    public string BestLapTimeText => BestLapTimeSeconds is { } seconds
        ? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff")
        : "--:--.---";
    public string DurationText => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(DurationSeconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    public string TrackLengthText => TrackLengthMeters > 0 ? $"{TrackLengthMeters / 1000:F3} km" : "--";
    public string TemperatureText => $"{AmbientTemperatureCelsius:F0}°C";
}

public sealed class LocalSessionRepository(string rootDirectory) : ISessionRepository
{
    private readonly ApexTracePackageService _packages = new();
    public string RootDirectory => Path.GetFullPath(rootDirectory);

    public async Task<string> SaveAsync(TelemetrySession session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        var safeTrack = string.Concat(session.Metadata.TrackName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var fileName = $"{safeTrack}_{session.Metadata.StartedAtUtc:yyyy-MM-dd_HH-mm-ss}.apextrace";
        return await _packages.ExportAsync(session, Path.Combine(rootDirectory, fileName), cancellationToken);
    }

    public Task<TelemetrySession> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        _packages.OpenAsync(path, cancellationToken);

    public async Task<IReadOnlyList<SessionMetadata>> ListAsync(CancellationToken cancellationToken = default)
        => (await ListEntriesAsync(cancellationToken)).Select(entry => entry.Metadata).ToArray();

    public async Task<IReadOnlyList<StoredSessionInfo>> ListEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var result = new List<StoredSessionInfo>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.apextrace"))
        {
            try
            {
                var session = await _packages.OpenAsync(path, cancellationToken);
                var file = new FileInfo(path);
                var completeLaps = session.Laps.Where(lap => lap.IsComplete).ToArray();
                var validLaps = completeLaps.Where(lap => lap.IsValid).ToArray();
                var bestLap = (validLaps.Length > 0 ? validLaps : completeLaps).MinBy(lap => lap.LapTimeSeconds);
                var lastSample = session.Samples.LastOrDefault();
                var trackLength = session.Samples.Where(sample => sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0)
                    .Select(sample => sample.LapDistanceMeters).DefaultIfEmpty(0).Max();
                result.Add(new StoredSessionInfo(Path.GetFullPath(path), session.Metadata, file.Length, file.LastWriteTime,
                    session.Samples.Count, completeLaps.Length, validLaps.Length, session.DurationSeconds,
                    session.Track.CenterLine, bestLap?.LapTimeSeconds, trackLength,
                    lastSample?.Environment.AmbientTemperatureCelsius ?? 0,
                    lastSample?.Environment.TrackTemperatureCelsius ?? 0));
            }
            catch (InvalidDataException)
            {
                // One damaged package must not prevent the library from opening.
            }
        }

        return result.OrderByDescending(entry => entry.Metadata.StartedAtUtc).ToArray();
    }

    public async Task<TelemetrySession?> OpenLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory)) return null;
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.apextrace")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                return await _packages.OpenAsync(path, cancellationToken);
            }
            catch (InvalidDataException)
            {
                // Continue to an older healthy package.
            }
        }
        return null;
    }

    public (int Count, long Bytes) GetStorageStatistics()
    {
        if (!Directory.Exists(rootDirectory)) return (0, 0);
        var files = Directory.EnumerateFiles(rootDirectory, "*.apextrace").Select(path => new FileInfo(path)).ToArray();
        return (files.Length, files.Sum(file => file.Length));
    }
}
