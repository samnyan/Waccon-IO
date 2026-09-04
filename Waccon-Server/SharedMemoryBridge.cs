using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace Waccon.Server;

/// <summary>Observed status of the local waccon-io shared-memory peer.</summary>
public readonly record struct SharedMemoryStatus(uint ServerProcessId, uint IoProcessId, ulong IoHeartbeatMs, ulong IoInputSequence, bool IoConnected);

/// <summary>Observed status of the latest LED output snapshot.</summary>
public readonly record struct SharedMemoryLedStatus(uint UnitCount, uint Sequence, bool HasNonZeroColor);

public sealed class SharedMemoryBridge : IDisposable
{
    private const uint Magic = 0x4E4F4357u;
    private const ushort MajorVersion = 1;
    private const ushort MinorVersion = 1;
    private const int HeaderSize = 24;
    private const int InputOffset = HeaderSize;
    private const int InputSize = 262;
    private const int OutputOffset = InputOffset + InputSize;
    private const int OutputSize = 1932;
    private const int EndpointSize = 32;
    private const int ServerEndpointOffset = OutputOffset + OutputSize;
    private const int IoEndpointOffset = ServerEndpointOffset + EndpointSize;
    private const int TotalSize = IoEndpointOffset + EndpointSize;
    private const uint CapabilityInput = 0x00000001u;
    private const uint CapabilityLedOutput = 0x00000002u;
    private const uint CapabilityServerStatus = 0x00000008u;
    private const uint CapabilityIoStatus = 0x00000010u;
    private const ulong IoHeartbeatTimeoutMs = 3000;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly ulong _startedMs = (ulong)Environment.TickCount64;
    private uint _lastLedUnitCount;
    private uint _lastLedSequence;
    private bool _lastLedHasNonZeroColor;

    /// <summary>Opens or creates the named Waccon mapping.</summary>
    public SharedMemoryBridge(string mappingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingName);
        _mapping = MemoryMappedFile.CreateOrOpen(mappingName, TotalSize, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, TotalSize, MemoryMappedFileAccess.ReadWrite);
        InitializeHeader();
    }

    /// <summary>Publishes an input payload using the shared sequence lock.</summary>
    public void PublishInput(ReadOnlySpan<byte> payload, int leaseTimeoutMs)
    {
        if (payload.Length != WconProtocol.InputPayloadSize) throw new InvalidDataException("Invalid input snapshot length.");
        var data = payload.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(254), (ulong)leaseTimeoutMs);
        var odd = (_view.ReadUInt32(16) + 1u) | 1u;
        var even = odd + 1u;
        _view.Write(16, odd);
        _view.WriteArray(InputOffset, data, 0, data.Length);
        _view.Flush();
        _view.Write(16, even);
        UpdateServerStatus(even);
    }

    /// <summary>Clears all externally supplied input.</summary>
    public void ClearInput() => PublishInput(new byte[WconProtocol.InputPayloadSize], 1);

    /// <summary>Returns a consistent LED payload, or null if no stable frame is available.</summary>
    public byte[]? ReadLedSnapshot()
    {
        var before = _view.ReadUInt32(20);
        if ((before & 1u) != 0) return null;
        var data = new byte[OutputSize];
        _view.ReadArray(OutputOffset, data, 0, data.Length);
        var after = _view.ReadUInt32(20);
        if (before != after || (after & 1u) != 0) return null;
        var unitCount = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data), 480u);
        var hasNonZeroColor = data.AsSpan(4, (int)unitCount * 4).IndexOfAnyExcept((byte)0) >= 0;
        _lastLedUnitCount = unitCount;
        _lastLedSequence = after;
        _lastLedHasNonZeroColor = hasNonZeroColor;
        return data;
    }

    /// <summary>Returns metadata about the latest stable LED output snapshot.</summary>
    public SharedMemoryLedStatus GetLedStatus()
    {
        _ = ReadLedSnapshot();
        return new SharedMemoryLedStatus(_lastLedUnitCount, _lastLedSequence, _lastLedHasNonZeroColor);
    }

    /// <summary>Refreshes the server heartbeat so waccon-io can identify a live bridge.</summary>
    public void Heartbeat() => UpdateServerStatus(_view.ReadUInt32(16));

    /// <summary>Returns the local bridge and waccon-io connection state.</summary>
    public SharedMemoryStatus GetStatus()
    {
        var serverProcessId = _view.ReadUInt32(ServerEndpointOffset);
        var ioProcessId = _view.ReadUInt32(IoEndpointOffset);
        var ioHeartbeat = _view.ReadUInt64(IoEndpointOffset + 16);
        var ioInputSequence = _view.ReadUInt64(IoEndpointOffset + 24);
        var now = (ulong)Environment.TickCount64;
        var ioConnected = ioProcessId != 0 && ioHeartbeat != 0 && now >= ioHeartbeat && now - ioHeartbeat <= IoHeartbeatTimeoutMs;
        return new SharedMemoryStatus(serverProcessId, ioProcessId, ioHeartbeat, ioInputSequence, ioConnected);
    }

    /// <summary>Builds the backwards-compatible WELCOME metadata payload.</summary>
    public byte[] GetWelcomePayload()
    {
        var status = GetStatus();
        var ioState = status.IoConnected ? "connected" : "waiting";
        return System.Text.Encoding.UTF8.GetBytes($"Waccon;wire=1.0;shm=1.1;serverPid={Environment.ProcessId};io={ioState};ioPid={status.IoProcessId};ioInputSeq={status.IoInputSequence}");
    }

    /// <summary>Builds the localhost health document without reflection-based JSON serialization.</summary>
    public byte[] GetHealthPayload()
    {
        var status = GetStatus();
        var led = GetLedStatus();
        var ioConnected = status.IoConnected ? "true" : "false";
        return System.Text.Encoding.UTF8.GetBytes($"{{\"status\":\"ok\",\"sharedMemoryVersion\":\"1.1\",\"serverPid\":{Environment.ProcessId},\"ioConnected\":{ioConnected},\"ioPid\":{status.IoProcessId},\"ioInputSequence\":{status.IoInputSequence},\"ledUnitCount\":{led.UnitCount},\"ledSequence\":{led.Sequence},\"ledHasNonZeroColor\":{(led.HasNonZeroColor ? "true" : "false")}}}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view.Dispose();
        _mapping.Dispose();
    }

    private void InitializeHeader()
    {
        if (_view.ReadUInt32(0) == 0) {
            _view.Write(0, Magic);
            _view.Write(4, MajorVersion);
            _view.Write(6, MinorVersion);
            _view.Write(8, (uint)TotalSize);
            _view.Write(12, CapabilityInput | CapabilityLedOutput);
        } else if (_view.ReadUInt32(0) != Magic ||
            _view.ReadUInt16(4) != MajorVersion ||
            _view.ReadUInt16(6) != MinorVersion ||
            _view.ReadUInt32(8) != TotalSize) {
            throw new InvalidOperationException($"Shared memory ABI mismatch; expected WCON 1.1 size {TotalSize}.");
        }
        UpdateServerStatus(_view.ReadUInt32(16));
        _view.Flush();
    }

    private void UpdateServerStatus(uint inputSequence)
    {
        _view.Write(12, _view.ReadUInt32(12) | CapabilityServerStatus);
        _view.Write(ServerEndpointOffset, (uint)Environment.ProcessId);
        _view.Write(ServerEndpointOffset + 4, MajorVersion);
        _view.Write(ServerEndpointOffset + 6, MinorVersion);
        _view.Write(ServerEndpointOffset + 8, _startedMs);
        _view.Write(ServerEndpointOffset + 16, (ulong)Environment.TickCount64);
        _view.Write(ServerEndpointOffset + 24, (ulong)inputSequence);
    }
}
