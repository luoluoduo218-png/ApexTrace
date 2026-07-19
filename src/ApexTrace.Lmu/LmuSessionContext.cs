namespace ApexTrace.Lmu;

public sealed record LmuSessionContext(
    string TrackName,
    string VehicleName,
    string EntryName,
    string VehicleClass,
    string SessionType,
    double TrackLengthMeters,
    double LapStartElapsedSeconds,
    double LastLapTimeSeconds,
    bool LapInvalidated,
    bool InRealtime,
    int VehicleCount);
