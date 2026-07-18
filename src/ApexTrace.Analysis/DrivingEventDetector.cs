using ApexTrace.Core;

namespace ApexTrace.Analysis;

public sealed class DrivingEventDetector
{
    public IReadOnlyList<DrivingEvent> Detect(IReadOnlyList<TelemetrySample> samples)
    {
        var events = new List<DrivingEvent>();
        if (samples.Count < 2)
        {
            return events;
        }

        var braking = samples[0].Brake >= 0.05;
        var throttling = samples[0].Throttle >= 0.05;
        var absActive = samples[0].AbsActive;
        var tcActive = samples[0].TcActive;
        var gear = samples[0].Gear;

        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            if (current.LapNumber != previous.LapNumber)
            {
                events.Add(Create(current, DrivingEventType.LapStarted, current.LapNumber, "LMU lap number changed"));
                events.Add(Create(previous, DrivingEventType.LapCompleted, previous.LapNumber, "Consecutive lap samples confirmed"));
            }

            if (!braking && previous.Brake < 0.02 && current.Brake >= 0.05)
            {
                braking = true;
                events.Add(Create(current, DrivingEventType.BrakeStarted, current.Brake, "Brake crossed 5% after being below 2%"));
            }
            else if (braking && current.Brake < 0.02)
            {
                braking = false;
                events.Add(Create(current, DrivingEventType.BrakeReleased, current.Brake, "Brake fell below 2%"));
            }

            if (!throttling && previous.Throttle < 0.02 && current.Throttle >= 0.05)
            {
                throttling = true;
                events.Add(Create(current, DrivingEventType.ThrottleStarted, current.Throttle, "Throttle crossed 5%"));
            }
            else if (throttling && current.Throttle < 0.02)
            {
                throttling = false;
            }

            if (current.Throttle >= 0.95 && previous.Throttle < 0.95)
            {
                events.Add(Create(current, DrivingEventType.FullThrottle, current.Throttle, "Throttle crossed 95%"));
            }

            if (!absActive && current.AbsActive)
            {
                events.Add(Create(current, DrivingEventType.AbsActivation, 1, "LMU ABS event became active"));
            }

            if (!tcActive && current.TcActive)
            {
                events.Add(Create(current, DrivingEventType.TcActivation, 1, "LMU TC event became active"));
            }

            if (gear != current.Gear)
            {
                gear = current.Gear;
                events.Add(Create(current, DrivingEventType.GearShift, gear, "LMU gear event changed"));
            }

            absActive = current.AbsActive;
            tcActive = current.TcActive;
        }

        return events;
    }

    private static DrivingEvent Create(TelemetrySample sample, DrivingEventType type, double value, string evidence) =>
        new(1, type, sample.SessionElapsedSeconds, sample.LapDistanceMeters, sample.LapNumber, value, evidence);
}
