using ApexTrace.Core;
using ApexTrace.Recording;

namespace ApexTrace.Recording.Tests;

public sealed class TelemetryLapBuilderTests
{
    [Fact]
    public void Build_OnlyMarksClosedMiddleGroupsComplete()
    {
        var samples = Enumerable.Range(0, 4)
            .SelectMany(lap => Enumerable.Range(0, 120).Select(index => Sample(lap, lap * 60 + index * 0.5, index)))
            .ToArray();

        var laps = TelemetryLapBuilder.Build(samples);

        Assert.Equal(4, laps.Count);
        Assert.False(laps[0].IsComplete);
        Assert.True(laps[1].IsComplete);
        Assert.True(laps[2].IsComplete);
        Assert.False(laps[3].IsComplete);
    }

    [Fact]
    public void Build_InvalidatedSamplesMakeLapInvalid()
    {
        var samples = Enumerable.Range(0, 4)
            .SelectMany(lap => Enumerable.Range(0, 120).Select(index =>
                Sample(lap, lap * 60 + index * 0.5, index, lap != 1 || index != 60)))
            .ToArray();

        var laps = TelemetryLapBuilder.Build(samples);

        Assert.True(laps[1].IsComplete);
        Assert.False(laps[1].IsValid);
        Assert.True(laps[2].IsValid);
    }

    [Fact]
    public void Build_SessionTimeResetNeverJoinsSameNumberedLapsAcrossSessions()
    {
        var firstSession = Enumerable.Range(0, 2)
            .SelectMany(lap => Enumerable.Range(0, 120).Select(index => Sample(lap, lap * 60 + index * 0.5, index)));
        var secondSession = Enumerable.Range(0, 3)
            .SelectMany(lap => Enumerable.Range(0, 120).Select(index => Sample(lap, lap * 60 + index * 0.5, index)));

        var laps = TelemetryLapBuilder.Build(firstSession.Concat(secondSession).ToArray());

        Assert.Equal(5, laps.Count);
        var complete = Assert.Single(laps, lap => lap.IsComplete);
        Assert.Equal(1, complete.LapNumber);
    }

    private static TelemetrySample Sample(int lap, double elapsed, int index, bool valid = true) => new(
        1, (long)elapsed * 100 + index, DateTimeOffset.UnixEpoch.AddSeconds(elapsed), elapsed, lap, index,
        new Vector3D(index, 0, -index), Orientation3D.Identity, new Vector3D(0, 0, -20), new Vector3D(0, 0, 0),
        20, 3, 5000, 0.5, 0, 0, 0, 50, 0.5, false, false,
        new VehicleControlSettings(0, 0, 0, 0, 0, 0, 0, 0), Enumerable.Repeat(WheelSample.Empty, 4).ToArray(),
        new EnvironmentSample(20, 30, 0, 0, new Vector3D(0, 0, 0), 0),
        new SampleQuality(true, true, true, true, index, 0, 0, null, valid));
}
