using ApexTrace.Analysis;
using ApexTrace.Core;

namespace ApexTrace.Analysis.Tests;

public sealed class SessionAnalysisTests
{
    [Fact]
    public void CompleteLaps_ProduceComparisonAndCornerRows()
    {
        var lapTimes = new[] { 100d, 95d, 98d };
        var samples = lapTimes.SelectMany((lapTime, lapIndex) => Enumerable.Range(0, 101).Select(index =>
        {
            var distance = index * 10d;
            var angle = distance / 1000 * Math.PI * 2;
            return Sample(lapIndex + 1, lapTimes.Take(lapIndex).Sum() + lapTime * index / 100,
                distance, new(Math.Cos(angle) * 100, 0, Math.Sin(angle) * 100), distance is >= 300 and <= 500 ? .12 : 0);
        })).ToArray();
        var laps = lapTimes.Select((lapTime, index) => new LapRecord(1, index + 1,
            lapTimes.Take(index).Sum(), lapTimes.Take(index + 1).Sum(), lapTime, true, true, 101)).ToArray();
        var metadata = new SessionMetadata(1, Guid.NewGuid(), "Test", "Test", "Car", "Class", "Practice",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(lapTimes.Sum()), TelemetryDataSource.DesignTimeSimulation,
            "test", null, null, true, "complete", null);
        var session = new TelemetrySession(1, metadata, samples, laps,
            new(1, "Test", "runtime", TrackGeometrySource.RuntimeReconstruction, .5, "", [], ""), [], []);

        var engine = new SessionAnalysisEngine();
        var comparison = engine.Compare(session);
        var corners = engine.AnalyzeCorners(session, comparison);

        Assert.True(comparison.IsAvailable);
        Assert.Equal(3, comparison.CurrentLap!.LapNumber);
        Assert.Equal(2, comparison.ReferenceLap!.LapNumber);
        Assert.Equal(3, comparison.Sectors.Count);
        Assert.NotEmpty(corners);
        Assert.All(corners, corner => Assert.NotEmpty(corner.TrackPoints));

        var selectedComparison = engine.Compare(session, laps[0], laps[2]);
        Assert.True(selectedComparison.IsAvailable);
        Assert.Equal(1, selectedComparison.CurrentLap!.LapNumber);
        Assert.Equal(3, selectedComparison.ReferenceLap!.LapNumber);
    }

    private static TelemetrySample Sample(int lap, double time, double distance, Vector3D position, double steering) => new(
        1, (long)(time * 100), DateTimeOffset.UnixEpoch.AddSeconds(time), time, lap, distance, position,
        Orientation3D.Identity, default, default, 30, 4, 5000, .5, distance is >= 250 and <= 330 ? .5 : 0,
        steering, 0, 40, .48, false, false, new(1, 5, 5, 2, 1, 0, 5, 3),
        [WheelSample.Empty, WheelSample.Empty, WheelSample.Empty, WheelSample.Empty], new(20, 30, 0, 0, default, 0),
        new(true, true, true, true, 0, 0, 0));
}
