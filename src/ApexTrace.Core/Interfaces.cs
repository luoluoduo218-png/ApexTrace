namespace ApexTrace.Core;

public interface ITelemetrySource : IAsyncDisposable
{
    TelemetryDataSource SourceKind { get; }
    IAsyncEnumerable<TelemetrySample> ReadAllAsync(CancellationToken cancellationToken = default);
}

public interface ITelemetryTransport
{
    ValueTask PublishAsync(TelemetrySample sample, CancellationToken cancellationToken = default);
}

public interface ISessionRepository
{
    Task<string> SaveAsync(TelemetrySession session, CancellationToken cancellationToken = default);
    Task<TelemetrySession> OpenAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionMetadata>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IReferenceLapProvider
{
    Task<LapRecord?> GetReferenceLapAsync(string track, string vehicle, CancellationToken cancellationToken = default);
}

public interface ISetupShareService
{
    Task<string?> ExportPlanAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public interface IRecommendationNarrator
{
    string Narrate(Recommendation recommendation);
}

public sealed class NullTelemetryTransport : ITelemetryTransport
{
    public ValueTask PublishAsync(TelemetrySample sample, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public sealed class NullReferenceLapProvider : IReferenceLapProvider
{
    public Task<LapRecord?> GetReferenceLapAsync(string track, string vehicle, CancellationToken cancellationToken = default) =>
        Task.FromResult<LapRecord?>(null);
}
