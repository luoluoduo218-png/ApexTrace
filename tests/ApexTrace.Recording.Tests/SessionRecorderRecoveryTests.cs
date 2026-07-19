using ApexTrace.Core;
using ApexTrace.Recording;

namespace ApexTrace.Recording.Tests;

public sealed class SessionRecorderRecoveryTests
{
    [Fact]
    public async Task RecoverAsync_RestoresCompleteLinesAndSkipsInterruptedTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApexTrace.Recovery.Tests", Guid.NewGuid().ToString("N"));
        var sessionId = Guid.NewGuid();
        var metadata = new SessionMetadata(1, sessionId, "Silverstone", "Silverstone", "Ferrari 296 LMGT3", "GT3",
            "Qualifying", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TelemetryDataSource.LmuSharedMemory,
            "LMU_Data", null, "hash", false, "recording", null);
        try
        {
            await using (var recorder = new SessionRecorder())
            {
                var directory = await recorder.StartAsync(root, metadata);
                await recorder.EnqueueAsync(Sample(1, 10));
                await recorder.EnqueueAsync(Sample(2, 10.02));
                await recorder.EnqueueAsync(Sample(3, 10.04));
                await recorder.FinishAsync();
                await File.AppendAllTextAsync(Path.Combine(directory, "samples.ndjson"), "{\"interrupted\":");
            }

            var recoveredDirectory = Assert.Single(SessionRecorder.FindRecoverableSessions(root));
            var recovered = await SessionRecorder.RecoverAsync(recoveredDirectory);

            Assert.Equal(sessionId, recovered.Metadata.SessionId);
            Assert.Equal("Ferrari 296 LMGT3", recovered.Metadata.VehicleName);
            Assert.Equal(3, recovered.Samples.Count);
            Assert.Equal(1, recovered.SkippedLines);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static TelemetrySample Sample(long sequence, double elapsed) => new(
        1, sequence, DateTimeOffset.UnixEpoch.AddSeconds(elapsed), elapsed, 1, elapsed * 10,
        new Vector3D(elapsed, 0, -elapsed), Orientation3D.Identity, new Vector3D(0, 0, -20), new Vector3D(0, 0, 0),
        20, 3, 5000, 0.5, 0, 0, 0, 50, 0.5, false, false,
        new VehicleControlSettings(0, 0, 0, 0, 0, 0, 0, 0), Enumerable.Repeat(WheelSample.Empty, 4).ToArray(),
        new EnvironmentSample(20, 30, 0, 0, new Vector3D(0, 0, 0), 0),
        new SampleQuality(true, true, true, true, sequence, 0, 0));
}
