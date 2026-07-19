using System.Security.Cryptography;
using System.Text;
using ApexTrace.Core;

namespace ApexTrace.Track;

public static class GpsTrackReconstructor
{
    private const double EarthRadiusMeters = 6_378_137;

    public static TrackDefinition FromSamples(string trackName, IReadOnlyList<TelemetrySample> samples, bool isComplete)
    {
        // Never stitch equal-distance points from different laps. That used to produce a
        // geometrically impossible kink wherever the first captured (partial) lap ended.
        // Select one coherent lap/segment first, then distance-bin only inside that segment.
        var segments = SplitIntoContinuousLaps(samples);
        var maximumCoverage = segments.Count == 0 ? 0 : segments.Max(Coverage);
        var candidates = segments
            .Where(segment => segment.Count >= 20 && Coverage(segment) >= maximumCoverage * 0.92)
            .ToArray();
        var selected = candidates
            .Where(segment => segment[0].LapDistanceMeters <= Math.Max(30, maximumCoverage * 0.03))
            .OrderBy(segment => segment[^1].SessionElapsedSeconds - segment[0].SessionElapsedSeconds)
            .ThenByDescending(segment => segment.Count)
            .FirstOrDefault()
            ?? candidates.OrderByDescending(Coverage).ThenByDescending(segment => segment.Count).FirstOrDefault()
            ?? [];

        const double binSizeMeters = 2.0;
        var valid = selected
            .Where(IsUsablePosition)
            .GroupBy(sample => Math.Round(sample.LapDistanceMeters / binSizeMeters) * binSizeMeters)
            .OrderBy(group => group.Key)
            .Select(group => new TrackPoint(
                group.Average(sample => sample.LapDistanceMeters),
                group.Average(sample => sample.WorldPosition.X),
                -group.Average(sample => sample.WorldPosition.Z),
                group.Average(sample => sample.WorldPosition.Y)))
            .ToArray();

        var hashInput = string.Join('|', valid.Select(point => $"{point.DistanceMeters:F1},{point.X:F3},{point.Y:F3}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        var diagnostic = isComplete
            ? $"显示单个连续完整圈的实际走线（{valid.Length:N0} 点），不会拼接不同圈；仍需用官方 AIW 验证赛道基准线精度。"
            : "只检测到 LMU 原生遥测片段，当前轨迹是不完整运行时重建，不能标记为精准赛道。";

        return new TrackDefinition(
            1,
            trackName,
            "runtime",
            isComplete ? TrackGeometrySource.RuntimeReconstruction : TrackGeometrySource.RuntimeReconstructionPartial,
            isComplete ? 0.5 : null,
            hash,
            valid,
            diagnostic);
    }

    public static Vector3D ProjectGps(double latitude, double longitude, double originLatitude, double originLongitude)
    {
        var latitudeRadians = DegreesToRadians(latitude);
        var originLatitudeRadians = DegreesToRadians(originLatitude);
        var x = DegreesToRadians(longitude - originLongitude)
            * Math.Cos((latitudeRadians + originLatitudeRadians) / 2)
            * EarthRadiusMeters;
        var north = DegreesToRadians(latitude - originLatitude) * EarthRadiusMeters;
        return new Vector3D(x, 0, -north);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static List<IReadOnlyList<TelemetrySample>> SplitIntoContinuousLaps(IReadOnlyList<TelemetrySample> samples)
    {
        var result = new List<IReadOnlyList<TelemetrySample>>();
        var current = new List<TelemetrySample>();
        TelemetrySample? previous = null;
        foreach (var sample in samples)
        {
            if (!IsUsablePosition(sample)) continue;
            var boundary = previous is not null && (sample.LapNumber != previous.LapNumber
                || sample.SessionElapsedSeconds < previous.SessionElapsedSeconds - 0.5
                || sample.LapDistanceMeters < previous.LapDistanceMeters - 250);
            if (boundary && current.Count > 0)
            {
                result.Add(current);
                current = [];
            }
            current.Add(sample);
            previous = sample;
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    private static bool IsUsablePosition(TelemetrySample sample) =>
        sample.Quality.IsWorldPositionValid
        && sample.Quality.IsLapDistanceValid
        && sample.LapDistanceMeters >= 0
        && double.IsFinite(sample.WorldPosition.X)
        && double.IsFinite(sample.WorldPosition.Z);

    private static double Coverage(IReadOnlyList<TelemetrySample> segment) => segment.Count == 0
        ? 0
        : segment.Max(sample => sample.LapDistanceMeters) - segment.Min(sample => sample.LapDistanceMeters);
}
