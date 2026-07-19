using ApexTrace.Core;

namespace ApexTrace.Recording;

public sealed record LapSignalFrame(
    double SessionElapsedSeconds,
    int LapNumber,
    double LapDistanceMeters,
    double TrackLengthMeters,
    double LapStartElapsedSeconds,
    double LastLapTimeSeconds,
    bool LapInvalidated);

public sealed record LapTransition(int CompletedLapNumber, double CompletedAtSeconds, int AgreementCount, string Evidence);

public sealed class LapDetector
{
    private LapSignalFrame? _previous;

    public LapTransition? Observe(LapSignalFrame current)
    {
        if (_previous is null)
        {
            _previous = current;
            return null;
        }

        var previous = _previous;
        _previous = current;
        var evidence = new List<string>();

        if (current.LapNumber > previous.LapNumber)
        {
            evidence.Add($"lap number {previous.LapNumber}->{current.LapNumber}");
        }

        if (current.TrackLengthMeters > 100
            && previous.LapDistanceMeters > current.TrackLengthMeters * 0.75
            && current.LapDistanceMeters < current.TrackLengthMeters * 0.25)
        {
            evidence.Add($"lap distance wrapped {previous.LapDistanceMeters:F1}m->{current.LapDistanceMeters:F1}m");
        }

        if (current.LapStartElapsedSeconds > previous.LapStartElapsedSeconds + 0.5)
        {
            evidence.Add($"lap start ET changed {previous.LapStartElapsedSeconds:F3}->{current.LapStartElapsedSeconds:F3}");
        }

        if (current.LastLapTimeSeconds > 0
            && Math.Abs(current.LastLapTimeSeconds - previous.LastLapTimeSeconds) > 0.001)
        {
            evidence.Add($"last lap time became {current.LastLapTimeSeconds:F3}s");
        }

        return evidence.Count >= 2
            ? new LapTransition(Math.Max(0, current.LapNumber - 1), current.SessionElapsedSeconds, evidence.Count, string.Join("; ", evidence))
            : null;
    }

    public void Reset() => _previous = null;
}
