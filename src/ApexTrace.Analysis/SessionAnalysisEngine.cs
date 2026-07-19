using ApexTrace.Core;

namespace ApexTrace.Analysis;

public sealed record SectorDelta(string Name, double CurrentSeconds, double ReferenceSeconds)
{
    public double DeltaSeconds => CurrentSeconds - ReferenceSeconds;
    public string DeltaText => $"{DeltaSeconds:+0.000;-0.000;0.000} s";
}

public sealed record LapComparisonResult(
    bool IsAvailable,
    string Diagnostic,
    LapRecord? CurrentLap,
    LapRecord? ReferenceLap,
    IReadOnlyList<SectorDelta> Sectors,
    IReadOnlyList<TrackPoint> CurrentPoints,
    IReadOnlyList<TrackPoint> ReferencePoints)
{
    public double DeltaSeconds => CurrentLap is not null && ReferenceLap is not null
        ? CurrentLap.LapTimeSeconds - ReferenceLap.LapTimeSeconds
        : 0;
}

public sealed record CornerAnalysisResult(
    string Name,
    double StartDistanceMeters,
    double ApexDistanceMeters,
    double EndDistanceMeters,
    double EntrySpeedKph,
    double MinimumSpeedKph,
    double ExitSpeedKph,
    double BrakeStartMeters,
    double PeakBrake,
    double TimeSeconds,
    double? ReferenceTimeSeconds,
    IReadOnlyList<TrackPoint> TrackPoints)
{
    public double? DeltaSeconds => ReferenceTimeSeconds is { } reference ? TimeSeconds - reference : null;
    public string DistanceText => $"{StartDistanceMeters:F0}–{EndDistanceMeters:F0} m";
    public string SpeedText => $"{EntrySpeedKph:F0} → {MinimumSpeedKph:F0} → {ExitSpeedKph:F0} km/h";
    public string DeltaText => DeltaSeconds is { } delta ? $"{delta:+0.000;-0.000;0.000} s" : "--";
}

public sealed class SessionAnalysisEngine
{
    public LapComparisonResult Compare(TelemetrySession? session)
    {
        var laps = session?.Laps.Where(lap => lap.IsComplete).ToArray() ?? [];
        if (session is null || laps.Length < 2)
        {
            return new(false, $"需要至少两个完整圈；当前识别到 {laps.Length} 个。", null, null, [], [], []);
        }

        var eligible = laps.Where(lap => lap.IsValid).ToArray();
        if (eligible.Length < 2) eligible = laps;
        var current = eligible[^1];
        var reference = eligible.Where(lap => !ReferenceEquals(lap, current) && lap != current)
            .OrderBy(lap => lap.LapTimeSeconds)
            .FirstOrDefault();
        if (reference is null)
        {
            return new(false, "没有可与当前圈配对的第二个完整圈。", null, null, [], [], []);
        }

        return Compare(session, current, reference);
    }

    public LapComparisonResult Compare(TelemetrySession? session, LapRecord? current, LapRecord? reference)
    {
        var laps = session?.Laps.Where(lap => lap.IsComplete).ToArray() ?? [];
        if (session is null || laps.Length < 2)
        {
            return new(false, $"需要至少两个完整圈；当前识别到 {laps.Length} 个。", null, null, [], [], []);
        }
        if (current is null || reference is null)
        {
            return new(false, "请选择当前圈和参考圈。", current, reference, [], [], []);
        }
        if (!laps.Contains(current) || !laps.Contains(reference))
        {
            return new(false, "所选圈不属于当前记录。", null, null, [], [], []);
        }
        if (current == reference)
        {
            return new(false, "当前圈和参考圈不能是同一圈。", current, reference, [], [], []);
        }

        var maximumDistance = session.Samples
            .Where(sample => sample.LapDistanceMeters >= 0 && sample.Quality.IsLapDistanceValid)
            .Select(sample => sample.LapDistanceMeters)
            .DefaultIfEmpty(0)
            .Max();
        var sectors = Enumerable.Range(0, 3).Select(index =>
        {
            var start = maximumDistance * index / 3;
            var end = maximumDistance * (index + 1) / 3;
            return new SectorDelta($"S{index + 1}", SectorTime(session, current, start, end, maximumDistance), SectorTime(session, reference, start, end, maximumDistance));
        }).ToArray();
        var validityNote = !current.IsValid || !reference.IsValid ? "（包含被游戏判无效但数据完整的圈）" : string.Empty;
        return new(true,
            $"圈 {current.LapNumber} 对比圈 {reference.LapNumber}{validityNote}，按圈内距离对齐。",
            current, reference, sectors, BuildLapPoints(session, current), BuildLapPoints(session, reference));
    }

    public IReadOnlyList<CornerAnalysisResult> AnalyzeCorners(TelemetrySession? session, LapComparisonResult comparison)
    {
        if (session is null || comparison.CurrentLap is null) return [];
        var currentSamples = SamplesForLap(session, comparison.CurrentLap)
            .Where(sample => sample.LapDistanceMeters >= 0 && sample.Quality.IsLapDistanceValid)
            .OrderBy(sample => sample.LapDistanceMeters)
            .ToArray();
        if (currentSamples.Length < 20) return [];

        // Steering is the most stable direct signal available in both live and DuckDB data.
        // Merge short straight gaps so one complex is presented as one useful corner zone.
        var active = currentSamples
            .Where(sample => Math.Abs(sample.Steering) >= 0.075 && sample.SpeedMetersPerSecond >= 8)
            .Select(sample => sample.LapDistanceMeters)
            .ToArray();
        if (active.Length == 0) return [];

        var zones = new List<(double Start, double End)>();
        var zoneStart = active[0];
        var previous = active[0];
        foreach (var distance in active.Skip(1))
        {
            if (distance - previous > 65)
            {
                if (previous - zoneStart >= 20) zones.Add((Math.Max(0, zoneStart - 35), previous + 35));
                zoneStart = distance;
            }
            previous = distance;
        }
        if (previous - zoneStart >= 20) zones.Add((Math.Max(0, zoneStart - 35), previous + 35));

        var referenceSamples = comparison.ReferenceLap is null ? [] : SamplesForLap(session, comparison.ReferenceLap)
            .Where(sample => sample.LapDistanceMeters >= 0 && sample.Quality.IsLapDistanceValid)
            .OrderBy(sample => sample.LapDistanceMeters)
            .ToArray();
        var results = new List<CornerAnalysisResult>();
        foreach (var (start, end) in zones.Take(30))
        {
            var samples = currentSamples.Where(sample => sample.LapDistanceMeters >= start && sample.LapDistanceMeters <= end).ToArray();
            if (samples.Length < 5) continue;
            var apex = samples.MinBy(sample => sample.SpeedMetersPerSecond)!;
            var braking = currentSamples.Where(sample => sample.LapDistanceMeters >= Math.Max(0, start - 220)
                && sample.LapDistanceMeters <= apex.LapDistanceMeters && sample.Brake >= 0.05).ToArray();
            var referenceTime = referenceSamples.Length == 0 ? (double?)null : RangeTime(referenceSamples, start, end);
            results.Add(new CornerAnalysisResult(
                $"T{results.Count + 1}", start, apex.LapDistanceMeters, end,
                samples[0].SpeedMetersPerSecond * 3.6,
                apex.SpeedMetersPerSecond * 3.6,
                samples[^1].SpeedMetersPerSecond * 3.6,
                braking.FirstOrDefault()?.LapDistanceMeters ?? start,
                braking.Select(sample => sample.Brake).DefaultIfEmpty(0).Max(),
                RangeTime(samples, start, end), referenceTime,
                samples.Where((_, index) => index % 4 == 0)
                    .Select(ToTrackPoint).ToArray()));
        }
        return results;
    }

    private static IReadOnlyList<TelemetrySample> SamplesForLap(TelemetrySession session, LapRecord lap) => session.Samples
        .Where(sample => sample.LapNumber == lap.LapNumber
            && sample.SessionElapsedSeconds >= lap.StartedAtSeconds - 0.01
            && sample.SessionElapsedSeconds <= lap.EndedAtSeconds + 0.01)
        .ToArray();

    private static IReadOnlyList<TrackPoint> BuildLapPoints(TelemetrySession session, LapRecord lap) => SamplesForLap(session, lap)
        .Where(sample => sample.Quality.IsWorldPositionValid && sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0)
        .GroupBy(sample => Math.Round(sample.LapDistanceMeters / 3) * 3)
        .OrderBy(group => group.Key)
        .Select(group => new TrackPoint(group.Average(sample => sample.LapDistanceMeters),
            group.Average(sample => sample.WorldPosition.X), -group.Average(sample => sample.WorldPosition.Z),
            group.Average(sample => sample.WorldPosition.Y)))
        .ToArray();

    private static double SectorTime(TelemetrySession session, LapRecord lap, double start, double end, double lapDistance)
    {
        var samples = SamplesForLap(session, lap)
            .Where(sample => sample.LapDistanceMeters >= 0 && sample.Quality.IsLapDistanceValid)
            .OrderBy(sample => sample.LapDistanceMeters)
            .ToArray();
        return Math.Max(0, TimeAtDistance(samples, lap, end, lapDistance) - TimeAtDistance(samples, lap, start, lapDistance));
    }

    private static double TimeAtDistance(IReadOnlyList<TelemetrySample> samples, LapRecord lap, double distance, double lapDistance)
    {
        if (distance <= 0 || samples.Count == 0) return lap.StartedAtSeconds;
        if (distance >= lapDistance * 0.999) return lap.EndedAtSeconds;
        var afterIndex = 0;
        while (afterIndex < samples.Count && samples[afterIndex].LapDistanceMeters < distance) afterIndex++;
        if (afterIndex == 0) return samples[0].SessionElapsedSeconds;
        if (afterIndex >= samples.Count) return lap.EndedAtSeconds;
        var before = samples[afterIndex - 1];
        var after = samples[afterIndex];
        var span = after.LapDistanceMeters - before.LapDistanceMeters;
        if (span <= 0.001) return before.SessionElapsedSeconds;
        var fraction = (distance - before.LapDistanceMeters) / span;
        return before.SessionElapsedSeconds + (after.SessionElapsedSeconds - before.SessionElapsedSeconds) * fraction;
    }

    private static double RangeTime(IReadOnlyList<TelemetrySample> samples, double start, double end)
    {
        var first = samples.Where(sample => sample.LapDistanceMeters >= start).MinBy(sample => sample.LapDistanceMeters);
        var last = samples.Where(sample => sample.LapDistanceMeters <= end).MaxBy(sample => sample.LapDistanceMeters);
        return first is null || last is null ? 0 : Math.Max(0, last.SessionElapsedSeconds - first.SessionElapsedSeconds);
    }

    private static TrackPoint ToTrackPoint(TelemetrySample sample) => new(sample.LapDistanceMeters,
        sample.WorldPosition.X, -sample.WorldPosition.Z, sample.WorldPosition.Y);
}
