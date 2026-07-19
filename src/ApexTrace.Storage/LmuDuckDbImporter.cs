using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ApexTrace.Core;
using ApexTrace.Recording;
using ApexTrace.Track;
using DuckDB.NET.Data;

namespace ApexTrace.Storage;

public sealed class LmuDuckDbImporter
{
    private const double EarthRadiusMeters = 6_378_137;

    public Task<TelemetrySession> ImportAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(path, cancellationToken), cancellationToken);

    private static TelemetrySession Import(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("LMU native telemetry database was not found.", path);
        }

        using var connection = new DuckDBConnection($"Data Source={path};access_mode=read_only");
        connection.Open();

        var metadata = ReadMetadata(connection);
        var times = ReadDoubles(connection, "GPS Time", "value");
        if (times.Length == 0)
        {
            throw new InvalidDataException("The LMU DuckDB contains no GPS Time samples. It may be an empty recording.");
        }

        var speedKph = ReadDoubles(connection, "Ground Speed", "value");
        var rpm = ReadDoubles(connection, "Engine RPM", "value");
        var throttle = ReadDoubles(connection, "Throttle Pos Unfiltered", "value");
        var brake = ReadDoubles(connection, "Brake Pos Unfiltered", "value");
        var steering = ReadDoubles(connection, "Steering Pos Unfiltered", "value");
        var clutch = ReadDoubles(connection, "Clutch Pos Unfiltered", "value");
        var fuel = ReadDoubles(connection, "Fuel Level", "value");
        var lapDistance = ReadDoubles(connection, "Lap Dist", "value");
        var latitude = ReadDoubles(connection, "GPS Latitude", "value");
        var longitude = ReadDoubles(connection, "GPS Longitude", "value");
        var lateralG = ReadDoubles(connection, "G Force Lat", "value");
        var longitudinalG = ReadDoubles(connection, "G Force Long", "value");
        var ambient = ReadDoubles(connection, "Ambient Temperature", "value");
        var trackTemperature = ReadDoubles(connection, "Track Temperature", "value");
        var ersCharge = TryReadDoubles(connection, "SoC", "value");
        var windSpeed = ReadDoubles(connection, "Wind Speed", "value");
        var windHeading = ReadDoubles(connection, "Wind Heading", "value");
        var tirePressure = ReadFourColumns(connection, "TyresPressure");
        var tireCarcass = ReadFourColumns(connection, "TyresCarcassTemp");
        var tireLeft = ReadFourColumns(connection, "TyresTempLeft");
        var tireCenter = ReadFourColumns(connection, "TyresTempCentre");
        var tireRight = ReadFourColumns(connection, "TyresTempRight");
        var tireWear = ReadFourColumns(connection, "Tyres Wear");
        var rideHeight = ReadFourColumns(connection, "RideHeights");
        var suspension = ReadFourColumns(connection, "Susp Pos");
        var brakeTemperature = ReadFourColumns(connection, "Brakes Temp");
        var wheelSpeed = ReadFourColumns(connection, "Wheel Speed");

        var laps = ReadEvents<int>(connection, "Lap", value => Convert.ToInt32(value, CultureInfo.InvariantCulture));
        var gears = ReadEvents<int>(connection, "Gear", value => Convert.ToInt32(value, CultureInfo.InvariantCulture));
        var absEvents = ReadEvents<bool>(connection, "ABS", value => Convert.ToBoolean(value, CultureInfo.InvariantCulture));
        var tcEvents = ReadEvents<bool>(connection, "TC", value => Convert.ToBoolean(value, CultureInfo.InvariantCulture));
        var absLevel = ReadEvents<byte>(connection, "ABSLevel", value => Convert.ToByte(value, CultureInfo.InvariantCulture));
        var tcLevel = ReadEvents<byte>(connection, "TCLevel", value => Convert.ToByte(value, CultureInfo.InvariantCulture));
        var tcSlip = ReadEvents<byte>(connection, "TCSlipAngle", value => Convert.ToByte(value, CultureInfo.InvariantCulture));
        var tcCut = ReadEvents<byte>(connection, "TCCut", value => Convert.ToByte(value, CultureInfo.InvariantCulture));
        var brakeBias = ReadEvents<double>(connection, "Brake Bias Rear", value => Convert.ToDouble(value, CultureInfo.InvariantCulture));
        var wetness = ReadEvents<double>(connection, "Minimum Path Wetness", value => Convert.ToDouble(value, CultureInfo.InvariantCulture));
        var cloudDarkness = TryReadEvents<bool>(connection, "CloudDarkness", value => Convert.ToBoolean(value, CultureInfo.InvariantCulture));
        var sectors = TryReadEvents<int>(connection, "Current Sector", value =>
        {
            var nativeSector = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return nativeSector is >= 1 and <= 3 ? nativeSector - 1 : nativeSector;
        });

        var startedAtUtc = ParseRecordingTime(metadata.GetValueOrDefault("RecordingTime"));
        var originLatitude = latitude.FirstOrDefault();
        var originLongitude = longitude.FirstOrDefault();
        var samples = new List<TelemetrySample>(times.Length);

        var lapCursor = 0;
        var gearCursor = 0;
        var absCursor = 0;
        var tcCursor = 0;
        var absLevelCursor = 0;
        var tcLevelCursor = 0;
        var tcSlipCursor = 0;
        var tcCutCursor = 0;
        var brakeBiasCursor = 0;
        var wetnessCursor = 0;
        var cloudDarknessCursor = 0;
        var sectorCursor = 0;

        var currentLap = laps.Count > 0 ? laps[0].Value : 0;
        var currentGear = gears.Count > 0 ? gears[0].Value : 0;
        var currentAbs = absEvents.Count > 0 && absEvents[0].Value;
        var currentTc = tcEvents.Count > 0 && tcEvents[0].Value;
        byte currentAbsLevel = absLevel.Count > 0 ? absLevel[0].Value : (byte)0;
        byte currentTcLevel = tcLevel.Count > 0 ? tcLevel[0].Value : (byte)0;
        byte currentTcSlip = tcSlip.Count > 0 ? tcSlip[0].Value : (byte)0;
        byte currentTcCut = tcCut.Count > 0 ? tcCut[0].Value : (byte)0;
        var currentBrakeBias = brakeBias.Count > 0 ? brakeBias[0].Value : 0;
        var currentWetness = wetness.Count > 0 ? wetness[0].Value : 0;
        var currentCloudDarkness = cloudDarkness.Count > 0 && cloudDarkness[0].Value;
        var currentSector = sectors.Count > 0 ? sectors[0].Value : -1;
        var hasErs = ersCharge.Any(value => double.IsFinite(value) && value > 0.001);
        var steeringWheelRange = ParseSteeringWheelRange(metadata.GetValueOrDefault("CarSetup"));
        var (frontTireCompound, rearTireCompound) = ParseTireCompounds(metadata.GetValueOrDefault("CarSetup"));

        for (var index = 0; index < times.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var time = times[index];
            currentLap = Advance(laps, time, ref lapCursor, currentLap);
            currentGear = Advance(gears, time, ref gearCursor, currentGear);
            currentAbs = Advance(absEvents, time, ref absCursor, currentAbs);
            currentTc = Advance(tcEvents, time, ref tcCursor, currentTc);
            currentAbsLevel = Advance(absLevel, time, ref absLevelCursor, currentAbsLevel);
            currentTcLevel = Advance(tcLevel, time, ref tcLevelCursor, currentTcLevel);
            currentTcSlip = Advance(tcSlip, time, ref tcSlipCursor, currentTcSlip);
            currentTcCut = Advance(tcCut, time, ref tcCutCursor, currentTcCut);
            currentBrakeBias = Advance(brakeBias, time, ref brakeBiasCursor, currentBrakeBias);
            currentWetness = Advance(wetness, time, ref wetnessCursor, currentWetness);
            currentCloudDarkness = Advance(cloudDarkness, time, ref cloudDarknessCursor, currentCloudDarkness);
            currentSector = Advance(sectors, time, ref sectorCursor, currentSector);

            var lat = Sample(latitude, index, times.Length);
            var lon = Sample(longitude, index, times.Length);
            var world = ProjectGps(lat, lon, originLatitude, originLongitude);
            var speedMps = Sample(speedKph, index, times.Length) / 3.6;
            var wheels = Enumerable.Range(0, 4)
                .Select(wheel => new WheelSample(
                    Sample(suspension[wheel], index, times.Length),
                    Sample(rideHeight[wheel], index, times.Length),
                    0,
                    Sample(brakeTemperature[wheel], index, times.Length),
                    Clamp01(Sample(brake, index, times.Length) / 100.0),
                    Sample(wheelSpeed[wheel], index, times.Length),
                    0,
                    0,
                    0,
                    0,
                    Sample(tirePressure[wheel], index, times.Length),
                    Sample(tireLeft[wheel], index, times.Length),
                    Sample(tireCenter[wheel], index, times.Length),
                    Sample(tireRight[wheel], index, times.Length),
                    Sample(tireCarcass[wheel], index, times.Length),
                    1 - Clamp01(Sample(tireWear[wheel], index, times.Length) / 100.0),
                    false,
                    false,
                    0,
                    0))
                .ToArray();

            var heading = Sample(windHeading, index, times.Length) * Math.PI / 180;
            var wind = Sample(windSpeed, index, times.Length);
            samples.Add(new TelemetrySample(
                1,
                index + 1,
                startedAtUtc.AddSeconds(time - times[0]),
                time,
                currentLap,
                Sample(lapDistance, index, times.Length),
                world,
                Orientation3D.Identity,
                new Vector3D(0, 0, -speedMps),
                new Vector3D(
                    Sample(lateralG, index, times.Length) * 9.80665,
                    0,
                    -Sample(longitudinalG, index, times.Length) * 9.80665),
                speedMps,
                currentGear,
                Sample(rpm, index, times.Length),
                Clamp01(Sample(throttle, index, times.Length) / 100.0),
                Clamp01(Sample(brake, index, times.Length) / 100.0),
                Math.Clamp(Sample(steering, index, times.Length) / 100.0, -1, 1),
                Clamp01(Sample(clutch, index, times.Length) / 100.0),
                Sample(fuel, index, times.Length),
                Clamp01(currentBrakeBias),
                currentAbs,
                currentTc,
                new VehicleControlSettings(currentTcLevel, currentTcSlip, currentTcCut, currentAbsLevel, 0, 0, 0, 0),
                wheels,
                new EnvironmentSample(
                    Sample(ambient, index, times.Length),
                    Sample(trackTemperature, index, times.Length),
                    -1,
                    Clamp01(currentWetness),
                    new Vector3D(Math.Cos(heading) * wind, 0, Math.Sin(heading) * wind),
                    0,
                    currentCloudDarkness ? 1 : 0),
                new SampleQuality(true, true, true, latitude.Length > 0 && longitude.Length > 0, index + 1, 0, 0,
                    "Imported read-only from LMU native DuckDB; channels aligned by declared sample frequency."),
                currentSector is >= 0 and <= 2 ? currentSector : -1,
                frontTireCompound,
                rearTireCompound,
                steeringWheelRange,
                hasErs ? Clamp01(Sample(ersCharge, index, times.Length) / 100.0) : -1,
                hasErs ? (byte)1 : (byte)0));
        }

        var completeLaps = TelemetryLapBuilder.Build(samples);
        var hasCompleteLap = completeLaps.Any(lap => lap.IsComplete);
        var trackName = metadata.GetValueOrDefault("TrackName") ?? "Unknown track";
        var track = GpsTrackReconstructor.FromSamples(trackName, samples, hasCompleteLap);
        var diagnostic = hasCompleteLap
            ? "LMU native DuckDB contains at least one completed lap."
            : "LMU 官方 DuckDB 仅包含一段非完整记录；未检测到完整圈，因此暂不生成分圈分析与建议。";

        var sessionMetadata = new SessionMetadata(
            1,
            Guid.NewGuid(),
            trackName,
            metadata.GetValueOrDefault("TrackLayout") ?? trackName,
            metadata.GetValueOrDefault("CarName") ?? "Unknown vehicle",
            metadata.GetValueOrDefault("CarClass") ?? "Unknown class",
            metadata.GetValueOrDefault("SessionType") ?? "Unknown session",
            startedAtUtc,
            startedAtUtc.AddSeconds(times[^1] - times[0]),
            TelemetryDataSource.LmuNativeDuckDb,
            Path.GetFullPath(path),
            null,
            null,
            hasCompleteLap,
            diagnostic,
            metadata.GetValueOrDefault("CarSetup"),
            metadata.GetValueOrDefault("WeatherConditions") ?? string.Empty);

        return new TelemetrySession(
            1,
            sessionMetadata,
            samples,
            completeLaps,
            track,
            [],
            []);
    }

    private static Dictionary<string, string> ReadMetadata(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM metadata";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    private static double[] ReadDoubles(DuckDBConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CAST(\"{column}\" AS DOUBLE) FROM \"{table.Replace("\"", "\"\"")}\"";
        using var reader = command.ExecuteReader();
        var result = new List<double>();
        while (reader.Read())
        {
            result.Add(reader.IsDBNull(0) ? double.NaN : reader.GetDouble(0));
        }

        return result.ToArray();
    }

    private static double[] TryReadDoubles(DuckDBConnection connection, string table, string column)
    {
        try
        {
            return ReadDoubles(connection, table, column);
        }
        catch (DbException)
        {
            return [];
        }
    }

    private static double[][] ReadFourColumns(DuckDBConnection connection, string table)
    {
        var result = Enumerable.Range(0, 4).Select(_ => new List<double>()).ToArray();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CAST(value1 AS DOUBLE), CAST(value2 AS DOUBLE), CAST(value3 AS DOUBLE), CAST(value4 AS DOUBLE) FROM \"{table.Replace("\"", "\"\"")}\"";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            for (var column = 0; column < 4; column++)
            {
                result[column].Add(reader.IsDBNull(column) ? double.NaN : reader.GetDouble(column));
            }
        }

        return result.Select(values => values.ToArray()).ToArray();
    }

    private static List<TimedValue<T>> ReadEvents<T>(DuckDBConnection connection, string table, Func<object, T> convert)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ts, value FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY ts";
        using var reader = command.ExecuteReader();
        var result = new List<TimedValue<T>>();
        while (reader.Read())
        {
            result.Add(new TimedValue<T>(reader.GetDouble(0), convert(reader.GetValue(1))));
        }

        return result;
    }

    private static List<TimedValue<T>> TryReadEvents<T>(DuckDBConnection connection, string table, Func<object, T> convert)
    {
        try
        {
            return ReadEvents(connection, table, convert);
        }
        catch (DbException)
        {
            return [];
        }
    }

    private static T Advance<T>(IReadOnlyList<TimedValue<T>> events, double time, ref int cursor, T current)
    {
        while (cursor < events.Count && events[cursor].Time <= time + 0.00001)
        {
            current = events[cursor].Value;
            cursor++;
        }

        return current;
    }

    private static double Sample(IReadOnlyList<double> values, int targetIndex, int targetCount)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sourceIndex = Math.Min(values.Count - 1, (int)((long)targetIndex * values.Count / targetCount));
        var value = values[sourceIndex];
        return double.IsFinite(value) ? value : 0;
    }

    private static DateTimeOffset ParseRecordingTime(string? value) =>
        DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH_mm_ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static double ParseSteeringWheelRange(string? setupJson)
    {
        if (string.IsNullOrWhiteSpace(setupJson)) return 1080;
        try
        {
            using var document = JsonDocument.Parse(setupJson);
            if (!document.RootElement.TryGetProperty("VM_STEER_LOCK", out var setting)
                || !setting.TryGetProperty("stringValue", out var value)) return 1080;
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text)) return 1080;
            var number = new string(text.TakeWhile(character => char.IsDigit(character) || character is '.' or ',').ToArray());
            return double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var range)
                && range is >= 180 and <= 1440 ? range : 1080;
        }
        catch (JsonException)
        {
            return 1080;
        }
    }

    private static (string Front, string Rear) ParseTireCompounds(string? setupJson)
    {
        if (string.IsNullOrWhiteSpace(setupJson)) return (string.Empty, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(setupJson);
            var front = ReadSetupString(document.RootElement, "VM_FRONT_TIRE_COMPOUND", "WM_COMPOUND-W_FL");
            var rear = ReadSetupString(document.RootElement, "VM_REAR_TIRE_COMPOUND", "WM_COMPOUND-W_RL");
            return (front, rear);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string ReadSetupString(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var setting) && setting.TryGetProperty("stringValue", out var value))
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }
        return string.Empty;
    }

    private static Vector3D ProjectGps(double latitude, double longitude, double originLatitude, double originLongitude)
    {
        var lat = latitude * Math.PI / 180;
        var originLat = originLatitude * Math.PI / 180;
        var x = (longitude - originLongitude) * Math.PI / 180 * Math.Cos((lat + originLat) / 2) * EarthRadiusMeters;
        var north = (latitude - originLatitude) * Math.PI / 180 * EarthRadiusMeters;
        return new Vector3D(x, 0, -north);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private sealed record TimedValue<T>(double Time, T Value);
}
