using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text;
using ApexTrace.Core;

namespace ApexTrace.Lmu;

[SupportedOSPlatform("windows")]
public sealed class LmuSharedMemoryReader : ITelemetrySource
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle? _dataEvent;
    private readonly byte[] _buffer = new byte[LmuHeaderLayoutV1.RootSize];
    private long _sequence;

    public LmuSessionContext? CurrentContext { get; private set; }

    public LmuSharedMemoryReader(LmuConnectionStatus status)
    {
        if (!status.HeaderSupported)
        {
            throw new InvalidOperationException("The local official LMU Header is not supported; refusing to read shared memory.");
        }

        _mapping = MemoryMappedFile.OpenExisting(LmuHeaderLayoutV1.SharedMemoryName, MemoryMappedFileRights.Read);
        _view = _mapping.CreateViewAccessor(0, LmuHeaderLayoutV1.RootSize, MemoryMappedFileAccess.Read);
        try
        {
            _dataEvent = EventWaitHandleAcl.OpenExisting(
                LmuHeaderLayoutV1.SharedMemoryEventName,
                EventWaitHandleRights.Synchronize);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            _dataEvent = null;
        }
        catch (UnauthorizedAccessException)
        {
            _dataEvent = null;
        }
    }

    public TelemetryDataSource SourceKind => TelemetryDataSource.LmuSharedMemory;

    public static bool CanOpenReadOnly()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(LmuHeaderLayoutV1.SharedMemoryName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 4, MemoryMappedFileAccess.Read);
            _ = view.ReadByte(0);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<TelemetrySample> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        long lastElapsedBits = long.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_dataEvent is not null)
            {
                _dataEvent.WaitOne(100);
            }
            else
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (TryReadConsistentSnapshot(out var sample, out var telemetryCounter)
                && sample is not null)
            {
                var elapsedBits = BitConverter.DoubleToInt64Bits(sample.SessionElapsedSeconds);
                if (elapsedBits != lastElapsedBits)
                {
                    lastElapsedBits = elapsedBits;
                    yield return sample;
                }
            }

        }
    }

    public bool TryReadConsistentSnapshot(out TelemetrySample? sample, out uint telemetryCounter)
    {
        sample = null;
        telemetryCounter = 0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var scoringBefore = _view.ReadUInt32(LmuHeaderLayoutV1.ScoringEventCounterOffset);
            var telemetryBefore = _view.ReadUInt32(LmuHeaderLayoutV1.TelemetryEventCounterOffset);
            var activeVehiclesBefore = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset);
            var playerIndexBefore = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset + 1);
            var playerHasVehicleBefore = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset + 2);
            var playerElapsedBefore = playerHasVehicleBefore != 0 && playerIndexBefore < activeVehiclesBefore && playerIndexBefore < 104
                ? _view.ReadDouble(LmuHeaderLayoutV1.TelemInfoOffset(playerIndexBefore) + 12)
                : double.NaN;
            var scoringElapsedBefore = _view.ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 68);
            _view.ReadArray(LmuHeaderLayoutV1.ScoringOffset, _buffer, LmuHeaderLayoutV1.ScoringOffset, LmuHeaderLayoutV1.ScoringInfoSize);
            _view.ReadArray(LmuHeaderLayoutV1.TelemetryOffset, _buffer, LmuHeaderLayoutV1.TelemetryOffset, 4);
            if (playerHasVehicleBefore != 0 && playerIndexBefore < activeVehiclesBefore && playerIndexBefore < 104)
            {
                var scoringVehicleOffset = LmuHeaderLayoutV1.VehicleScoringOffset(playerIndexBefore);
                var telemetryVehicleOffset = LmuHeaderLayoutV1.TelemInfoOffset(playerIndexBefore);
                _view.ReadArray(scoringVehicleOffset, _buffer, scoringVehicleOffset, LmuHeaderLayoutV1.VehicleScoringInfoSize);
                _view.ReadArray(telemetryVehicleOffset, _buffer, telemetryVehicleOffset, LmuHeaderLayoutV1.TelemInfoSize);
            }
            var scoringAfter = _view.ReadUInt32(LmuHeaderLayoutV1.ScoringEventCounterOffset);
            var telemetryAfter = _view.ReadUInt32(LmuHeaderLayoutV1.TelemetryEventCounterOffset);
            var activeVehiclesAfter = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset);
            var playerIndexAfter = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset + 1);
            var playerHasVehicleAfter = _view.ReadByte(LmuHeaderLayoutV1.TelemetryOffset + 2);
            var playerElapsedAfter = playerHasVehicleAfter != 0 && playerIndexAfter < activeVehiclesAfter && playerIndexAfter < 104
                ? _view.ReadDouble(LmuHeaderLayoutV1.TelemInfoOffset(playerIndexAfter) + 12)
                : double.NaN;
            var scoringElapsedAfter = _view.ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 68);

            if (scoringBefore != scoringAfter || telemetryBefore != telemetryAfter
                || activeVehiclesBefore != activeVehiclesAfter
                || playerIndexBefore != playerIndexAfter
                || playerHasVehicleBefore != playerHasVehicleAfter
                || BitConverter.DoubleToInt64Bits(playerElapsedBefore) != BitConverter.DoubleToInt64Bits(playerElapsedAfter)
                || BitConverter.DoubleToInt64Bits(scoringElapsedBefore) != BitConverter.DoubleToInt64Bits(scoringElapsedAfter))
            {
                continue;
            }

            telemetryCounter = telemetryAfter;
            sample = ParsePlayerSample(scoringAfter, telemetryAfter);
            return sample is not null;
        }

        return false;
    }

    private TelemetrySample? ParsePlayerSample(uint scoringCounter, uint telemetryCounter)
    {
        var activeVehicles = _buffer[LmuHeaderLayoutV1.TelemetryOffset];
        var playerIndex = _buffer[LmuHeaderLayoutV1.TelemetryOffset + 1];
        var playerHasVehicle = _buffer[LmuHeaderLayoutV1.TelemetryOffset + 2] != 0;
        if (!playerHasVehicle || playerIndex >= activeVehicles || playerIndex >= 104)
        {
            return null;
        }

        var t = LmuHeaderLayoutV1.TelemInfoOffset(playerIndex);
        var s = LmuHeaderLayoutV1.VehicleScoringOffset(playerIndex);
        var playerCrossCheck = ReadBool(s + 196) && ReadSByte(s + 197) == 0;
        var lapDistance = ReadDouble(s + 104);
        var trackLength = ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 88);
        var trackName = ReadString(t + 96, 64);
        var entryName = ReadString(t + 32, 64);
        var vehicleModel = ReadString(t + LmuHeaderLayoutV1.TelemVehicleModelOffset, 30);
        var lapInvalidated = ReadBool(t + 745);
        CurrentContext = new LmuSessionContext(
            trackName,
            string.IsNullOrWhiteSpace(vehicleModel) ? entryName : vehicleModel,
            entryName,
            ReadString(s + 200, 32),
            FormatSessionType(ReadInt32(LmuHeaderLayoutV1.ScoringOffset + 64)),
            trackLength,
            ReadDouble(t + 24),
            ReadDouble(s + 168),
            lapInvalidated,
            ReadBool(LmuHeaderLayoutV1.ScoringOffset + 115),
            ReadInt32(LmuHeaderLayoutV1.ScoringOffset + 104));
        var pos = ReadVector(t + 160);
        var velocity = ReadVector(t + 184);
        var acceleration = ReadVector(t + 208);
        var speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        var wheels = Enumerable.Range(0, 4).Select(index => ReadWheel(t + LmuHeaderLayoutV1.TelemWheelArrayOffset + index * LmuHeaderLayoutV1.TelemWheelSize)).ToArray();

        return new TelemetrySample(
            1,
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            ReadDouble(t + 12),
            ReadInt32(t + 20),
            lapDistance,
            pos,
            new Orientation3D(ReadVector(t + 232), ReadVector(t + 256), ReadVector(t + 280)),
            velocity,
            acceleration,
            speed,
            ReadInt32(t + 352),
            ReadDouble(t + 356),
            Clamp01(ReadDouble(t + 388)),
            Clamp01(ReadDouble(t + 396)),
            Math.Clamp(ReadDouble(t + 404), -1, 1),
            Clamp01(ReadDouble(t + 412)),
            ReadDouble(t + 524),
            Clamp01(ReadDouble(t + 664)),
            ReadBool(t + 746),
            ReadBool(t + 747),
            new VehicleControlSettings(
                _buffer[t + 750], _buffer[t + 752], _buffer[t + 754], _buffer[t + 756],
                _buffer[t + 758], _buffer[t + 760], _buffer[t + 762], _buffer[t + 764]),
            wheels,
            new EnvironmentSample(
                ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 228),
                ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 236),
                ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 220),
                ReadDouble(LmuHeaderLayoutV1.ScoringOffset + 268),
                ReadVector(LmuHeaderLayoutV1.ScoringOffset + 244),
                _buffer[LmuHeaderLayoutV1.ScoringOffset + 349],
                ReadDouble(LmuHeaderLayoutV1.ScoringOffset + LmuHeaderLayoutV1.ScoringDarkCloudOffset)),
            new SampleQuality(
                true,
                playerCrossCheck,
                lapDistance >= 0 && (trackLength <= 0 || lapDistance <= trackLength + 20),
                double.IsFinite(pos.X) && double.IsFinite(pos.Z),
                _sequence,
                telemetryCounter,
                scoringCounter,
                playerCrossCheck ? null : "telemetry.playerVehicleIdx did not match scoring mIsPlayer/mControl",
                !lapInvalidated),
            ReadInt32(t + LmuHeaderLayoutV1.TelemCurrentSectorOffset) & 0x7FFFFFFF,
            ReadString(t + LmuHeaderLayoutV1.TelemFrontTireCompoundNameOffset, LmuHeaderLayoutV1.TelemTireCompoundNameLength),
            ReadString(t + LmuHeaderLayoutV1.TelemRearTireCompoundNameOffset, LmuHeaderLayoutV1.TelemTireCompoundNameLength),
            ReadSingle(t + LmuHeaderLayoutV1.TelemPhysicalSteeringWheelRangeOffset),
            Clamp01(ReadDouble(t + LmuHeaderLayoutV1.TelemBatteryChargeFractionOffset)),
            _buffer[t + LmuHeaderLayoutV1.TelemElectricBoostMotorStateOffset]);
    }

    private WheelSample ReadWheel(int offset)
    {
        var surfaceLeft = ReadDouble(offset + 128) - 273.15;
        var surfaceCenter = ReadDouble(offset + 136) - 273.15;
        var surfaceRight = ReadDouble(offset + 144) - 273.15;
        return new WheelSample(
            ReadDouble(offset), ReadDouble(offset + 8), ReadDouble(offset + 16), ReadDouble(offset + 24),
            ReadDouble(offset + 32), ReadDouble(offset + 40), ReadDouble(offset + 48), ReadDouble(offset + 56),
            ReadDouble(offset + 104), ReadDouble(offset + 112), ReadDouble(offset + 120),
            surfaceLeft, surfaceCenter, surfaceRight, ReadDouble(offset + 204) - 273.15,
            ReadDouble(offset + 152), ReadBool(offset + 177), ReadBool(offset + 178),
            ReadDouble(offset + 80), ReadDouble(offset + 196));
    }

    private Vector3D ReadVector(int offset) => new(ReadDouble(offset), ReadDouble(offset + 8), ReadDouble(offset + 16));
    private double ReadDouble(int offset) => BitConverter.ToDouble(_buffer, offset);
    private float ReadSingle(int offset) => BitConverter.ToSingle(_buffer, offset);
    private int ReadInt32(int offset) => BitConverter.ToInt32(_buffer, offset);
    private sbyte ReadSByte(int offset) => unchecked((sbyte)_buffer[offset]);
    private bool ReadBool(int offset) => _buffer[offset] != 0;
    private string ReadString(int offset, int length)
    {
        var end = Array.IndexOf(_buffer, (byte)0, offset, length);
        var count = end < 0 ? length : end - offset;
        return Encoding.UTF8.GetString(_buffer, offset, count).Trim();
    }

    private static string FormatSessionType(int session) => session switch
    {
        0 => "Test Day",
        >= 1 and <= 4 => $"Practice {session}",
        >= 5 and <= 8 => $"Qualifying {session - 4}",
        9 => "Warmup",
        >= 10 and <= 13 => $"Race {session - 9}",
        _ => $"Session {session}"
    };
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    public ValueTask DisposeAsync()
    {
        _dataEvent?.Dispose();
        _view.Dispose();
        _mapping.Dispose();
        return ValueTask.CompletedTask;
    }
}
