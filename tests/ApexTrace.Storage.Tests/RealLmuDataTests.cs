using System.IO.Compression;
using ApexTrace.Core;
using ApexTrace.Storage;

namespace ApexTrace.Storage.Tests;

public sealed class RealLmuDataTests
{
    private static string TelemetryDirectory => @"D:\SteamLibrary\steamapps\common\Le Mans Ultimate\UserData\Telemetry";

    [Fact]
    public async Task LatestNonEmptyOfficialDuckDb_ImportsActualSamplesAndStaysPartial()
    {
        var path = FindRealRecording();
        var session = await new LmuDuckDbImporter().ImportAsync(path);

        Assert.Equal(TelemetryDataSource.LmuNativeDuckDb, session.Metadata.DataSource);
        Assert.Contains("Spa", session.Metadata.TrackName);
        Assert.Equal(4570, session.Samples.Count);
        Assert.InRange(session.DurationSeconds, 45.67, 45.71);
        Assert.True(session.Samples.Max(sample => sample.LapDistanceMeters) > 1400);
        Assert.All(session.Samples, sample => { Assert.InRange(sample.Throttle, 0, 1); Assert.InRange(sample.Brake, 0, 1); });
        Assert.Contains("中性", session.Samples[0].FrontTireCompound);
        Assert.Contains("中性", session.Samples[0].RearTireCompound);
        Assert.Equal("微云", session.Metadata.WeatherConditions);
        Assert.Equal(-1, session.Samples[0].Environment.RainFraction);
        Assert.Equal(0, session.Samples[0].ElectricMotorState);
        Assert.All(session.Laps, lap => Assert.False(lap.IsComplete));
        Assert.False(session.Metadata.IsComplete);
    }

    [Fact]
    public async Task ImportedRecording_RoundTripsThroughApexTracePackage()
    {
        var service = new ApexTracePackageService();
        var source = await new LmuDuckDbImporter().ImportAsync(FindRealRecording());
        var output = Path.Combine(Path.GetTempPath(), "ApexTrace.Tests", Guid.NewGuid() + ".apextrace");
        try
        {
            await service.ExportAsync(source, output);
            using (var archive = ZipFile.OpenRead(output))
            {
                Assert.NotNull(archive.GetEntry("manifest.json"));
                Assert.True(archive.GetEntry("telemetry/samples.parquet")!.Length > 0);
                Assert.True(archive.GetEntry("telemetry/samples.csv")!.Length > 0);
                Assert.True(archive.GetEntry("preview/session.png")!.Length > 0);
            }
            var reopened = await service.OpenAsync(output);
            Assert.Equal(source.Samples.Count, reopened.Samples.Count);
            Assert.Equal(source.Metadata.SessionId, reopened.Metadata.SessionId);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static string FindRealRecording() => Directory.EnumerateFiles(TelemetryDirectory, "*.duckdb")
        .Select(path => new FileInfo(path)).Where(file => file.Length > 1_000_000)
        .OrderByDescending(file => file.LastWriteTimeUtc).First().FullName;
}
