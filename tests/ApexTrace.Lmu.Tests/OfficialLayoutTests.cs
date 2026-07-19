using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ApexTrace.Lmu;

namespace ApexTrace.Lmu.Tests;

public sealed class OfficialLayoutTests
{
    private const string HeaderRoot = @"D:\SteamLibrary\steamapps\common\Le Mans Ultimate\Support\SharedMemoryInterface";

    [Fact]
    public void Pack4AndOneByteBoolContract_IsStable()
    {
        Assert.Equal(3, Marshal.SizeOf<LmuHeaderLayoutV1.PackedBoolContract>());
        Assert.Equal(324820, LmuHeaderLayoutV1.RootSize);
        Assert.Equal(1632, LmuHeaderLayoutV1.ScoringOffset);
        Assert.Equal(552, LmuHeaderLayoutV1.ScoringInfoSize);
        Assert.Equal(128464, LmuHeaderLayoutV1.TelemetryOffset);
        Assert.Equal(128468, LmuHeaderLayoutV1.TelemInfoArrayOffset);
        Assert.Equal(2192, LmuHeaderLayoutV1.VehicleScoringArrayOffset);
        Assert.Equal(796, LmuHeaderLayoutV1.TelemVehicleModelOffset);
        Assert.Equal(848, LmuHeaderLayoutV1.TelemWheelArrayOffset);
        Assert.Equal(692, LmuHeaderLayoutV1.TelemPhysicalSteeringWheelRangeOffset);
        Assert.Equal(704, LmuHeaderLayoutV1.TelemBatteryChargeFractionOffset);
        Assert.Equal(744, LmuHeaderLayoutV1.TelemElectricBoostMotorStateOffset);
        Assert.Equal(212, LmuHeaderLayoutV1.ScoringDarkCloudOffset);
    }

    [Theory]
    [InlineData("InternalsPlugin.hpp", LmuHeaderLayoutV1.InternalsHeaderSha256)]
    [InlineData("PluginObjects.hpp", LmuHeaderLayoutV1.PluginObjectsHeaderSha256)]
    [InlineData("SharedMemoryInterface.hpp", LmuHeaderLayoutV1.SharedMemoryHeaderSha256)]
    public void LocalOfficialHeaders_MatchPinnedHashes(string name, string expected)
    {
        var path = Path.Combine(HeaderRoot, name);
        Assert.True(File.Exists(path), $"LMU official header should exist at {path}");
        using var stream = File.OpenRead(path);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(stream)));
    }

    [Fact]
    public void SharedMemoryContract_UsesLmuNativeMap()
    {
        Assert.Equal("LMU_Data", LmuHeaderLayoutV1.SharedMemoryName);
        Assert.Equal("LMU_Data_Event", LmuHeaderLayoutV1.SharedMemoryEventName);
    }
}
