using ApexTrace.Recording;

namespace ApexTrace.Recording.Tests;

public sealed class LapDetectorTests
{
    [Fact]
    public void OneNoisySignal_DoesNotCreateLap()
    {
        var detector = new LapDetector();
        detector.Observe(new(90, 2, 6900, 7000, 0, 0, false));
        var transition = detector.Observe(new(90.1, 2, 20, 7000, 0, 0, false));
        Assert.Null(transition);
    }

    [Fact]
    public void TwoIndependentSignals_ConfirmLap()
    {
        var detector = new LapDetector();
        detector.Observe(new(90, 2, 6900, 7000, 0, 0, false));
        var transition = detector.Observe(new(90.1, 3, 20, 7000, 90.1, 90.1, false));
        Assert.NotNull(transition);
        Assert.True(transition.AgreementCount >= 2);
        Assert.Equal(2, transition.CompletedLapNumber);
    }
}
