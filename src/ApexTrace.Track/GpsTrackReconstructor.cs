using System.Security.Cryptography;
using System.Text;
using ApexTrace.Core;

namespace ApexTrace.Track;

public static class GpsTrackReconstructor
{
    private const double EarthRadiusMeters = 6_378_137;

    public static TrackDefinition FromSamples(string trackName, IReadOnlyList<TelemetrySample> samples, bool isComplete)
    {
        var valid = samples
            .Where(sample => sample.Quality.IsWorldPositionValid)
            .GroupBy(sample => Math.Round(sample.LapDistanceMeters, 1))
            .Select(group => group.First())
            .OrderBy(sample => sample.LapDistanceMeters)
            .Select(sample => new TrackPoint(
                sample.LapDistanceMeters,
                sample.WorldPosition.X,
                -sample.WorldPosition.Z,
                sample.WorldPosition.Y))
            .ToArray();

        var hashInput = string.Join('|', valid.Select(point => $"{point.DistanceMeters:F1},{point.X:F3},{point.Y:F3}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        var diagnostic = isComplete
            ? "赛道几何由完整运行时轨迹重建；仍需用官方 AIW 或多圈残差验证精度。"
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
}
