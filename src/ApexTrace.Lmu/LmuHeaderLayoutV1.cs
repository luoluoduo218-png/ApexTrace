using System.Runtime.InteropServices;

namespace ApexTrace.Lmu;

/// <summary>
/// Versioned byte layout generated from the local official LMU headers dated 2026-06-17.
/// No game DLL or process-memory access is used. All offsets assume #pragma pack(push, 4), x64.
/// </summary>
public static class LmuHeaderLayoutV1
{
    public const string SharedMemoryName = "LMU_Data";
    public const string SharedMemoryEventName = "LMU_Data_Event";

    public const string InternalsHeaderSha256 = "9B6EE8CF610FA5049B18DF580A9A9BC9EBB91346FC466584D576A6442ABCF68F";
    public const string PluginObjectsHeaderSha256 = "F65F1D2226AF1ACB277F8337FB10D8955DB384118FC36EEF95EA446BE058E247";
    public const string SharedMemoryHeaderSha256 = "194FF1AB39030BC811540931C8B9817258727252C9A4B35FA4734BBAA16D4DDC";

    public const int SharedMemoryEventCount = 17;
    public const int ScoringEventCounterOffset = 10 * sizeof(uint);
    public const int TelemetryEventCounterOffset = 11 * sizeof(uint);

    public const int TelemVect3Size = 24;
    public const int TelemWheelSize = 260;
    public const int TelemInfoSize = 1888;
    public const int TelemVehicleModelOffset = 796;
    public const int TelemWheelArrayOffset = 848;
    public const int TelemCurrentSectorOffset = 600;
    public const int TelemFrontTireCompoundNameOffset = 620;
    public const int TelemRearTireCompoundNameOffset = 638;
    public const int TelemTireCompoundNameLength = 18;
    public const int TelemPhysicalSteeringWheelRangeOffset = 692;
    public const int TelemBatteryChargeFractionOffset = 704;
    public const int TelemElectricBoostMotorStateOffset = 744;
    public const int ScoringDarkCloudOffset = 212;
    public const int VehicleScoringInfoSize = 584;
    public const int ScoringInfoSize = 552;
    public const int ApplicationStateSize = 260;
    public const int GenericSize = 332;
    public const int PathDataSize = 1300;
    public const int ScoringDataSize = 126832;
    public const int TelemetryDataSize = 196356;
    public const int RootSize = 324820;

    public const int ScoringOffset = GenericSize + PathDataSize;
    public const int VehicleScoringArrayOffset = ScoringOffset + ScoringInfoSize + sizeof(long);
    public const int TelemetryOffset = ScoringOffset + ScoringDataSize;
    public const int TelemInfoArrayOffset = TelemetryOffset + 4;

    public static int TelemInfoOffset(int vehicleIndex) => TelemInfoArrayOffset + vehicleIndex * TelemInfoSize;
    public static int VehicleScoringOffset(int vehicleIndex) => VehicleScoringArrayOffset + vehicleIndex * VehicleScoringInfoSize;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PackedBoolContract
    {
        public byte ActiveVehicles;
        public byte PlayerVehicleIndex;
        [MarshalAs(UnmanagedType.I1)] public bool PlayerHasVehicle;
    }
}
