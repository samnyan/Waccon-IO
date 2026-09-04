using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Waccon.Server;

/// <summary>WCON TCP message types.</summary>
public enum MessageType : ushort { Hello = 1, Welcome = 2, InputSnapshot = 3, ClearInput = 4, Ping = 5, Pong = 6, LedSnapshot = 7, Error = 8 }

/// <summary>WCON v1 framing helpers.</summary>
public static class WconProtocol
{
    /// <summary>Fixed header size in bytes.</summary>
    public const int HeaderSize = 24;
    /// <summary>Protocol major version.</summary>
    public const ushort Version = 0x0100;
    /// <summary>Input payload size.</summary>
    public const int InputPayloadSize = 2 + 240 + 4 + 8 + 8;
    /// <summary>First touch-cell byte in an input payload.</summary>
    public const int TouchCellOffset = 2;
    /// <summary>Touch-cell count in an input payload.</summary>
    public const int TouchCellCount = 240;
    /// <summary>Source identifier offset in an input payload.</summary>
    public const int SourceIdOffset = TouchCellOffset + TouchCellCount;

    /// <summary>Creates a framed WCON packet.</summary>
    public static byte[] Frame(MessageType type, uint sequence, ReadOnlySpan<byte> payload)
    {
        var result = new byte[HeaderSize + payload.Length];
        Encoding.ASCII.GetBytes("WCON").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)Environment.TickCount64 * 1000);
        payload.CopyTo(result.AsSpan(HeaderSize));
        return result;
    }

    /// <summary>Reads one complete frame from a stream.</summary>
    public static async ValueTask<(MessageType Type, uint Sequence, byte[] Payload)?> ReadAsync(NetworkStream stream, int maxPayload, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false)) return null;
        if (!header.AsSpan(0, 4).SequenceEqual("WCON"u8)) throw new InvalidDataException("Invalid WCON magic.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) != Version) throw new InvalidDataException("Unsupported WCON version.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        if (length > maxPayload) throw new InvalidDataException("WCON payload exceeds configured limit.");
        var payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false)) return null;
        return ((MessageType)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), payload);
    }

    /// <summary>Parses one complete WCON packet carried by a message transport.</summary>
    public static (MessageType Type, uint Sequence, byte[] Payload) ParseFrame(ReadOnlySpan<byte> frame, int maxPayload)
    {
        if (frame.Length < HeaderSize) throw new InvalidDataException("Truncated WCON header.");
        if (!frame.Slice(0, 4).SequenceEqual("WCON"u8)) throw new InvalidDataException("Invalid WCON magic.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4)) != Version) throw new InvalidDataException("Unsupported WCON version.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(8));
        if (length > maxPayload) throw new InvalidDataException("WCON payload exceeds configured limit.");
        if (length != frame.Length - HeaderSize) throw new InvalidDataException("WCON frame length does not match its payload.");
        return ((MessageType)BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6)), BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(12)), frame[HeaderSize..].ToArray());
    }

    private static async ValueTask<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        while (!buffer.IsEmpty) {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) return false;
            buffer = buffer[read..];
        }
        return true;
    }
}
