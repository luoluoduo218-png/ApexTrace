using ApexTrace.Lmu;

namespace ApexTrace.Lmu.Tests;

public sealed class LiveSharedMemoryTests
{
    [Fact]
    public async Task RunningLmu_ExposesPlausiblePlayerAndSessionDataReadOnly()
    {
        var status = new LmuInstallationProbe().Probe();
        if (!status.ProcessDetected || !status.SharedMemoryAvailable)
        {
            return;
        }

        await using var reader = new LmuSharedMemoryReader(status);
        Assert.True(reader.TryReadConsistentSnapshot(out var sample, out var telemetryCounter));
        Assert.NotNull(sample);
        Assert.True(telemetryCounter > 0);
        Assert.True(sample.Quality.IsPlayerResolved, sample.Quality.Diagnostic);

        var context = Assert.IsType<LmuSessionContext>(reader.CurrentContext);
        Assert.False(string.IsNullOrWhiteSpace(context.TrackName));
        Assert.False(string.IsNullOrWhiteSpace(context.VehicleName));
        Assert.False(string.IsNullOrWhiteSpace(context.EntryName));
        Assert.InRange(context.TrackLengthMeters, 1_000, 30_000);
        Assert.InRange(context.VehicleCount, 1, 104);
        Assert.InRange(sample.Environment.AmbientTemperatureCelsius, -50, 80);
        Assert.InRange(sample.Environment.TrackTemperatureCelsius, -50, 120);
    }

    [Fact]
    public async Task RunningSimulation_StreamsMoreThanOneTelemetryFrame()
    {
        var status = new LmuInstallationProbe().Probe();
        if (!status.SharedMemoryAvailable)
        {
            return;
        }

        await using var probe = new LmuSharedMemoryReader(status);
        if (!probe.TryReadConsistentSnapshot(out var first, out _) || first is null)
        {
            return;
        }
        await Task.Delay(100);
        if (!probe.TryReadConsistentSnapshot(out var second, out _) || second is null
            || second.SessionElapsedSeconds <= first.SessionElapsedSeconds)
        {
            return; // LMU is paused or its simulation is not advancing.
        }

        await using var reader = new LmuSharedMemoryReader(status);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var samples = new List<double>();
        try
        {
            await foreach (var sample in reader.ReadAllAsync(timeout.Token))
            {
                samples.Add(sample.SessionElapsedSeconds);
                if (samples.Count >= 50) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.True(samples.Count >= 50, $"Expected at least 50 changing frames, received {samples.Count}.");
        Assert.Equal(samples.Count, samples.Distinct().Count());
        Assert.InRange(samples[^1] - samples[0], 0.35, 1.25);
    }
}
