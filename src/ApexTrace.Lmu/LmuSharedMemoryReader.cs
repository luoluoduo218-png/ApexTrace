using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using ApexTrace.Core;

namespace ApexTrace.Lmu;

[SupportedOSPlatform("windows")]
public sealed class LmuSharedMemoryReader : ITelemetrySource
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _buffer = new byte[LmuHeaderLayoutV1.RootSize];
    private long _sequence;

    public LmuSharedMemoryReader(LmuConnectionStatus status)
    {
        if (!status.HeaderSupported)
        {
            throw new InvalidOperationException("The local official LMU Header is not supported; refusing to read shared memory.");
        }

        _mapping = MemoryMappedFile.OpenExisting(LmuHeaderLayoutV1.SharedMemoryName, MemoryMappedFileRights.Read);
        _view = _mapping.CreateViewAccessor(0, LmuHeaderLayoutV1.RootSize, MemoryMappedFileAccess.Read);
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
        uint lastTelemetryCounter = uint.MaxValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (TryReadConsistentSnapshot(out var sample, out var telemetryCounter)
                && telemetryCounter != lastTelemetryCounter
                && sample is not null)
            {
                lastTelemetryCounter = telemetryCounter;
                yield return sample;
            }

            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
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
            _view.ReadArray(0, _buffer, 0, _buffer.Length);
            var scoringAfter = _view.ReadUInt32(LmuHeaderLayoutV1.ScoringEventCounterOffset);
            var telemetryAfter = _view.ReadUInt32(LmuHeaderLayoutV1.TelemetryEventCounterOffset);

            if (scoringBefore != scoringAfter || telemetryBefore != telemetryAfter)
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
        var pos = ReadVector(t + 160);
        var velocity = ReadVector(t + 184);
        var acceleration = ReadVector(t + 208);
        var speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        var wheels = Enumerable.Range(0, 4).Select(index => ReadWheel(t + 848 + index * LmuHeaderLayoutV1.TelemWheelSize)).ToArray();

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
                _buffer[LmuHeaderLayoutV1.ScoringOffset + 349]),
            new SampleQuality(
                true,
                playerCrossCheck,
                lapDistance >= 0 && (trackLength <= 0 || lapDistance <= trackLength + 20),
                double.IsFinite(pos.X) && double.IsFinite(pos.Z),
                _sequence,
                telemetryCounter,
                scoringCounter,
                playerCrossCheck ? null : "telemetry.playerVehicleIdx did not match scoring mIsPlayer/mControl"));
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
    private int ReadInt32(int offset) => BitConverter.ToInt32(_buffer, offset);
    private sbyte ReadSByte(int offset) => unchecked((sbyte)_buffer[offset]);
    private bool ReadBool(int offset) => _buffer[offset] != 0;
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    public ValueTask DisposeAsync()
    {
        _view.Dispose();
        _mapping.Dispose();
        return ValueTask.CompletedTask;
    }
}
