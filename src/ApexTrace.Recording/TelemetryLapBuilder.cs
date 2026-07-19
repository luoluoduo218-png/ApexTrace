using ApexTrace.Core;

namespace ApexTrace.Recording;

public static class TelemetryLapBuilder
{
    public static IReadOnlyList<LapRecord> Build(IReadOnlyList<TelemetrySample> samples)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var ranges = new List<(int Start, int End, int Segment)>();
        var start = 0;
        var segment = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            var sessionReset = samples[index].SessionElapsedSeconds < samples[index - 1].SessionElapsedSeconds - 0.5;
            if (!sessionReset && samples[index].LapNumber == samples[start].LapNumber)
            {
                continue;
            }

            ranges.Add((start, index - 1, segment));
            if (sessionReset) segment++;
            start = index;
        }
        ranges.Add((start, samples.Count - 1, segment));

        var records = new List<LapRecord>(ranges.Count);
        for (var rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
        {
            var (rangeStart, rangeEnd, rangeSegment) = ranges[rangeIndex];
            var first = samples[rangeStart];
            var last = samples[rangeEnd];
            var duration = Math.Max(0, last.SessionElapsedSeconds - first.SessionElapsedSeconds);
            var closedByNextSequentialLap = rangeIndex < ranges.Count - 1
                && ranges[rangeIndex + 1].Segment == rangeSegment
                && samples[ranges[rangeIndex + 1].Start].LapNumber == first.LapNumber + 1;
            var rangeSamples = samples.Skip(rangeStart).Take(rangeEnd - rangeStart + 1).ToArray();
            var isFirstInSegment = rangeIndex == 0 || ranges[rangeIndex - 1].Segment != rangeSegment;
            // Keep the captured first and final groups conservative. Any groups closed between
            // them are complete; transient coordinate-quality misses do not erase the whole lap.
            var complete = !isFirstInSegment && closedByNextSequentialLap
                && duration >= 10 && rangeSamples.Length >= 100;
            var validQualityRatio = rangeSamples.Count(sample => sample.Quality.IsLapDistanceValid
                && sample.Quality.IsWorldPositionValid) / (double)rangeSamples.Length;
            var valid = complete && rangeSamples.All(sample => sample.Quality.IsLapValid)
                && validQualityRatio >= 0.98;
            records.Add(new LapRecord(1, first.LapNumber, first.SessionElapsedSeconds, last.SessionElapsedSeconds,
                duration, valid, complete, rangeEnd - rangeStart + 1));
        }

        return records;
    }
}
