using System.Text.Json;
using ApexTrace.Core;

namespace ApexTrace.Storage;

public sealed class LocalSessionRepository(string rootDirectory) : ISessionRepository
{
    private readonly ApexTracePackageService _packages = new();

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
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var result = new List<SessionMetadata>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.apextrace"))
        {
            try
            {
                result.Add((await _packages.OpenAsync(path, cancellationToken)).Metadata);
            }
            catch (InvalidDataException)
            {
                // One damaged package must not prevent the library from opening.
            }
        }

        return result.OrderByDescending(metadata => metadata.StartedAtUtc).ToArray();
    }
}
