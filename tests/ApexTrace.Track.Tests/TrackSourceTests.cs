using ApexTrace.Core;
using ApexTrace.Track;

namespace ApexTrace.Track.Tests;

public sealed class TrackSourceTests
{
    [Fact]
    public void SpaManifest_IsReadWithoutOpeningProtectedMas()
    {
        const string path = @"D:\SteamLibrary\steamapps\common\Le Mans Ultimate\Installed\Locations\Spa_2023\1.29\Spa_2023.mft";
        Assert.True(File.Exists(path));
        var manifest = LmuTrackManifestProbe.Probe(path);
        Assert.Equal("Spa_2023", manifest.ComponentName);
        Assert.Equal("1.29", manifest.Version);
        Assert.Equal("Studio 397", manifest.Author);
        Assert.True(manifest.ProtectedContent);
        Assert.Equal(4, manifest.MasFiles.Count);
    }

    [Fact]
    public void PartialRuntimeTrack_IsNeverMarkedAccurate()
    {
        var track = GpsTrackReconstructor.FromSamples("Spa", [Sample(0, 0), Sample(100, 1)], false);
        Assert.Equal(TrackGeometrySource.RuntimeReconstructionPartial, track.Source);
        Assert.Null(track.AccuracyEstimateMeters);
    }

    [Fact]
    public void Reconstruction_SelectsOneContinuousLapInsteadOfStitchingLaps()
    {
        var partial = Enumerable.Range(0, 20).Select(index => Sample(index * 2, index) with
        {
            LapNumber = 1,
            WorldPosition = new(index * 2, 0, index * 2)
        });
        var complete = Enumerable.Range(0, 60).Select(index => Sample(index * 2, 100 + index) with
        {
            LapNumber = 2,
            WorldPosition = new(1000 + index * 2, 0, 1000 + index * 2)
        });

        var track = GpsTrackReconstructor.FromSamples("Test", partial.Concat(complete).ToArray(), true);

        Assert.NotEmpty(track.CenterLine);
        Assert.All(track.CenterLine, point => Assert.True(point.X >= 1000));
    }

    private static TelemetrySample Sample(double distance, long sequence) => new(
        1, sequence, DateTimeOffset.UtcNow, sequence, 1, distance, new(distance, 0, distance), Orientation3D.Identity,
        default, default, 0, 1, 0, 0, 0, 0, 0, 0, 0, false, false,
        new(0,0,0,0,0,0,0,0), [WheelSample.Empty, WheelSample.Empty, WheelSample.Empty, WheelSample.Empty],
        new(20,30,0,0,default,0), new(true,true,true,true,sequence,0,0));
}
