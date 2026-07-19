using System.Text.Json;
using System.Threading.Channels;
using ApexTrace.Core;

namespace ApexTrace.Recording;

public sealed record RecorderCheckpoint(
    int SchemaVersion,
    Guid SessionId,
    DateTimeOffset UpdatedAtUtc,
    long SampleCount,
    long QueueDepth,
    string State,
    string HeaderHash);

public sealed record RecoveredRecording(SessionMetadata Metadata, IReadOnlyList<TelemetrySample> Samples, int SkippedLines);

public sealed class SessionRecorder : IAsyncDisposable
{
    private readonly Channel<TelemetrySample> _channel = Channel.CreateBounded<TelemetrySample>(new BoundedChannelOptions(32_768)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly CancellationTokenSource _cts = new();
    private Task? _writerTask;
    private string? _sessionDirectory;
    private Guid _sessionId;
    private long _sampleCount;
    private long _queueDepth;
    private string _headerHash = string.Empty;

    public long SampleCount => Interlocked.Read(ref _sampleCount);
    public long QueueDepth => Interlocked.Read(ref _queueDepth);
    public string? SessionDirectory => _sessionDirectory;

    public async Task<string> StartAsync(string tempRoot, SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (_writerTask is not null)
        {
            throw new InvalidOperationException("Recorder is already running.");
        }

        _sessionId = metadata.SessionId;
        _headerHash = metadata.SharedMemoryHeaderSha256 ?? string.Empty;
        _sessionDirectory = Path.Combine(tempRoot, _sessionId.ToString("N"));
        Directory.CreateDirectory(_sessionDirectory);
        await AtomicWriteJsonAsync(Path.Combine(_sessionDirectory, "manifest.partial.json"), new
        {
            schemaVersion = 1,
            metadata.SessionId,
            metadata.TrackName,
            metadata.VehicleName,
            metadata.StartedAtUtc,
            metadata.DataSource,
            headerHash = _headerHash,
            state = "Recording"
        }, cancellationToken);
        _writerTask = WriteLoopAsync(_cts.Token);
        return _sessionDirectory;
    }

    public async ValueTask EnqueueAsync(TelemetrySample sample, CancellationToken cancellationToken = default)
    {
        if (_writerTask is null)
        {
            throw new InvalidOperationException("Recorder has not been started.");
        }

        Interlocked.Increment(ref _queueDepth);
        try
        {
            await _channel.Writer.WriteAsync(sample, cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _queueDepth);
            throw;
        }
    }

    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_writerTask is null)
        {
            return;
        }

        _channel.Writer.TryComplete();
        await _writerTask.WaitAsync(cancellationToken);
        await WriteCheckpointAsync("ReviewPending", cancellationToken);
        _writerTask = null;
    }

    public async Task WriteCheckpointAsync(string state = "Recording", CancellationToken cancellationToken = default)
    {
        if (_sessionDirectory is null)
        {
            return;
        }

        await AtomicWriteJsonAsync(Path.Combine(_sessionDirectory, "checkpoint.json"),
            new RecorderCheckpoint(1, _sessionId, DateTimeOffset.UtcNow, SampleCount, QueueDepth, state, _headerHash), cancellationToken);
    }

    public static IReadOnlyList<string> FindRecoverableSessions(string tempRoot)
    {
        if (!Directory.Exists(tempRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(tempRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.partial.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToArray();
    }

    public static Task<RecoveredRecording> RecoverAsync(string sessionDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recover(sessionDirectory, cancellationToken), cancellationToken);

    private static RecoveredRecording Recover(string sessionDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(sessionDirectory, "manifest.partial.json");
        var samplesPath = Path.Combine(sessionDirectory, "samples.ndjson");
        if (!File.Exists(manifestPath) || !File.Exists(samplesPath))
            throw new InvalidDataException("临时记录缺少 manifest.partial.json 或 samples.ndjson。");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<PartialManifest>(File.ReadAllText(manifestPath), options)
            ?? throw new InvalidDataException("临时记录 manifest 无法解析。");
        var samples = new List<TelemetrySample>();
        var skipped = 0;
        foreach (var line in File.ReadLines(samplesPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var sample = JsonSerializer.Deserialize<TelemetrySample>(line, options);
                if (sample is not null) samples.Add(sample); else skipped++;
            }
            catch (JsonException)
            {
                skipped++; // An abrupt exit can leave only the final NDJSON line incomplete.
            }
        }
        if (samples.Count == 0) throw new InvalidDataException("临时记录中没有可恢复的完整遥测样本。");

        var metadata = new SessionMetadata(
            1,
            manifest.SessionId,
            manifest.TrackName,
            manifest.TrackName,
            manifest.VehicleName,
            string.Empty,
            "Recovered live session",
            manifest.StartedAtUtc,
            samples[^1].CapturedAtUtc,
            TelemetryDataSource.LmuSharedMemory,
            Path.GetFullPath(sessionDirectory),
            null,
            manifest.HeaderHash,
            false,
            $"从中断的临时记录恢复 {samples.Count:N0} 个样本；跳过 {skipped} 个不完整行。",
            null);
        return new RecoveredRecording(metadata, samples, skipped);
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_sessionDirectory!, "samples.ndjson");
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);
        var lastCheckpoint = DateTimeOffset.UtcNow;

        await foreach (var sample in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(sample, _jsonOptions));
            Interlocked.Increment(ref _sampleCount);
            Interlocked.Decrement(ref _queueDepth);
            if (DateTimeOffset.UtcNow - lastCheckpoint >= TimeSpan.FromSeconds(30))
            {
                await writer.FlushAsync(cancellationToken);
                await WriteCheckpointAsync(cancellationToken: cancellationToken);
                lastCheckpoint = DateTimeOffset.UtcNow;
            }
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static async Task AtomicWriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var tempPath = path + ".new";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        File.Move(tempPath, path, true);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        if (_writerTask is not null)
        {
            await _writerTask;
        }

        _cts.Dispose();
    }

    private sealed record PartialManifest(
        int SchemaVersion,
        Guid SessionId,
        string TrackName,
        string VehicleName,
        DateTimeOffset StartedAtUtc,
        TelemetryDataSource DataSource,
        string HeaderHash,
        string State);
}
