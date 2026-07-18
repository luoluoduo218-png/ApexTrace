namespace ApexTrace.Core;

public enum AppSessionState
{
    Disconnected,
    GameDetected,
    SharedMemoryReady,
    TrackAndVehicleReady,
    ReadyToRecord,
    Recording,
    Finalizing,
    ReviewPending,
    Saved,
    Exported,
    Discarded,
    Faulted
}

public enum TelemetryDataSource
{
    LmuSharedMemory,
    LmuNativeDuckDb,
    ApexTracePackage,
    DesignTimeSimulation
}

public enum TrackGeometrySource
{
    OfficialAiw,
    OfficialGeometry,
    RuntimeReconstruction,
    RuntimeReconstructionPartial,
    ProtectedContent
}

public readonly record struct Vector3D(double X, double Y, double Z);

public readonly record struct Orientation3D(
    Vector3D Row0,
    Vector3D Row1,
    Vector3D Row2)
{
    public static Orientation3D Identity { get; } = new(
        new Vector3D(1, 0, 0),
        new Vector3D(0, 1, 0),
        new Vector3D(0, 0, 1));
}

public sealed record VehicleControlSettings(
    byte TractionControl,
    byte TractionControlSlip,
    byte TractionControlCut,
    byte Abs,
    byte MotorMap,
    byte Migration,
    byte FrontAntiRollBar,
    byte RearAntiRollBar);

public sealed record WheelSample(
    double SuspensionDeflectionMeters,
    double RideHeightMeters,
    double SuspensionForceNewtons,
    double BrakeTemperatureCelsius,
    double BrakePressure,
    double RotationRadiansPerSecond,
    double LateralPatchVelocity,
    double LongitudinalPatchVelocity,
    double TireLoadNewtons,
    double GripFraction,
    double PressureKpa,
    double SurfaceLeftCelsius,
    double SurfaceCenterCelsius,
    double SurfaceRightCelsius,
    double CarcassTemperatureCelsius,
    double WearFraction,
    bool Flat,
    bool Detached,
    double CamberRadians,
    double ToeRadians)
{
    public static WheelSample Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, 0, 0);
}

public sealed record EnvironmentSample(
    double AmbientTemperatureCelsius,
    double TrackTemperatureCelsius,
    double RainFraction,
    double PathWetnessFraction,
    Vector3D WindMetersPerSecond,
    byte TrackGripLevel);

public sealed record SampleQuality(
    bool IsConsistentSnapshot,
    bool IsPlayerResolved,
    bool IsLapDistanceValid,
    bool IsWorldPositionValid,
    long SourceSequence,
    uint TelemetryEventCounter,
    uint ScoringEventCounter,
    string? Diagnostic = null);

public sealed record TelemetrySample(
    int SchemaVersion,
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    double SessionElapsedSeconds,
    int LapNumber,
    double LapDistanceMeters,
    Vector3D WorldPosition,
    Orientation3D Orientation,
    Vector3D LocalVelocity,
    Vector3D LocalAcceleration,
    double SpeedMetersPerSecond,
    int Gear,
    double EngineRpm,
    double Throttle,
    double Brake,
    double Steering,
    double Clutch,
    double FuelLiters,
    double BrakeBiasRear,
    bool AbsActive,
    bool TcActive,
    VehicleControlSettings Controls,
    WheelSample[] Wheels,
    EnvironmentSample Environment,
    SampleQuality Quality);

public sealed record SessionMetadata(
    int SchemaVersion,
    Guid SessionId,
    string TrackName,
    string TrackLayout,
    string VehicleName,
    string VehicleClass,
    string SessionType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    TelemetryDataSource DataSource,
    string SourcePath,
    string? GameVersion,
    string? SharedMemoryHeaderSha256,
    bool IsComplete,
    string CompletenessDiagnostic,
    string? SetupJson);

public sealed record LapRecord(
    int SchemaVersion,
    int LapNumber,
    double StartedAtSeconds,
    double EndedAtSeconds,
    double LapTimeSeconds,
    bool IsValid,
    bool IsComplete,
    int SampleCount);

public sealed record TrackPoint(double DistanceMeters, double X, double Y, double ElevationMeters = 0);

public sealed record TrackDefinition(
    int SchemaVersion,
    string Name,
    string Version,
    TrackGeometrySource Source,
    double? AccuracyEstimateMeters,
    string SourceFilesHash,
    IReadOnlyList<TrackPoint> CenterLine,
    string Diagnostic);

public enum DrivingEventType
{
    LapStarted,
    LapCompleted,
    BrakeStarted,
    PeakBrake,
    BrakeReleased,
    TurnIn,
    Apex,
    ThrottleStarted,
    FullThrottle,
    GearShift,
    AbsActivation,
    TcActivation,
    OffTrack,
    LapInvalidated,
    PitEntry,
    PitExit,
    Impact
}

public sealed record DrivingEvent(
    int SchemaVersion,
    DrivingEventType Type,
    double SessionElapsedSeconds,
    double LapDistanceMeters,
    int LapNumber,
    double Value,
    string Evidence);

public sealed record Recommendation(
    int SchemaVersion,
    Guid Id,
    string Type,
    string Scope,
    string Title,
    IReadOnlyList<string> Evidence,
    double? CurrentValue,
    double? SuggestedValue,
    string Unit,
    double EstimatedGainMinSeconds,
    double EstimatedGainMaxSeconds,
    double Confidence,
    IReadOnlyList<string> ValidationSteps);

public sealed record TelemetrySession(
    int SchemaVersion,
    SessionMetadata Metadata,
    IReadOnlyList<TelemetrySample> Samples,
    IReadOnlyList<LapRecord> Laps,
    TrackDefinition Track,
    IReadOnlyList<DrivingEvent> Events,
    IReadOnlyList<Recommendation> Recommendations)
{
    public double DurationSeconds => Samples.Count == 0
        ? 0
        : Samples[^1].SessionElapsedSeconds - Samples[0].SessionElapsedSeconds;
}

public sealed record LmuConnectionStatus(
    bool ProcessDetected,
    int? ProcessId,
    bool SharedMemoryAvailable,
    bool HeaderSupported,
    string InstallationPath,
    string HeaderSha256,
    string HeaderVersion,
    string Diagnostic);
