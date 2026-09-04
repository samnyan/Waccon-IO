using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace Waccon.Server;

/// <summary>Writes network input and reads LED output from the waccon-io mapping.</summary>
public sealed class SharedMemoryBridge : IDisposable
{
    private const int HeaderSize = 24;
    private const int InputOffset = HeaderSize;
    private const int InputSize = 262;
    private const int OutputOffset = InputOffset + InputSize;
    private const int OutputSize = 1932;
    private const int TotalSize = OutputOffset + OutputSize;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private uint _inputSequence;

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
        var odd = (_inputSequence + 1u) | 1u;
        var even = odd + 1u;
        _view.Write(16, odd);
        _view.WriteArray(InputOffset, data, 0, data.Length);
        _view.Flush();
        _view.Write(16, even);
        _inputSequence = even;
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
        return before == after && (after & 1u) == 0 ? data : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view.Dispose();
        _mapping.Dispose();
    }

    private void InitializeHeader()
    {
        if (_view.ReadUInt32(0) == 0x4E4F4357u) return;
        _view.Write(0, 0x4E4F4357u);
        _view.Write(4, (ushort)1);
        _view.Write(6, (ushort)0);
        _view.Write(8, (uint)TotalSize);
        _view.Write(12, 3u);
        _view.Flush();
    }
}
