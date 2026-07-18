using ApexTrace.Analysis;
using ApexTrace.Core;

namespace ApexTrace.Analysis.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public void IncompleteSession_ProducesNoSetupRecommendation()
    {
        var sample = Sample(0, 1, 1, .9, false);
        var metadata = new SessionMetadata(1, Guid.NewGuid(), "Spa", "Spa", "GT3", "GT3", "Q", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TelemetryDataSource.LmuNativeDuckDb, "real.duckdb", null, null, false, "partial", null);
        var session = new TelemetrySession(1, metadata, [sample], [new(1,1,0,10,10,true,false,1)], new(1,"Spa","runtime",TrackGeometrySource.RuntimeReconstructionPartial,null,"",[],"partial"), [], []);
        Assert.Empty(new EvidenceRecommendationEngine().Analyze(session));
    }

    [Fact]
    public void ThresholdCrossings_ProduceEvidenceEvents()
    {
        var events = new DrivingEventDetector().Detect([Sample(0, 1, 0, 0, false), Sample(1, 1, .1, 0, true)]);
        Assert.Contains(events, e => e.Type == DrivingEventType.BrakeStarted);
        Assert.Contains(events, e => e.Type == DrivingEventType.TcActivation);
    }

    private static TelemetrySample Sample(double time, int lap, double brake, double throttle, bool tc) => new(
        1,(long)(time*100),DateTimeOffset.UtcNow,time,lap,time*10,new(time,0,0),Orientation3D.Identity,default,default,0,1,1000,throttle,brake,0,0,20,.5,false,tc,
        new(0,0,0,0,0,0,0,0),[WheelSample.Empty,WheelSample.Empty,WheelSample.Empty,WheelSample.Empty],new(20,30,0,0,default,0),new(true,true,true,true,0,0,0));
}
