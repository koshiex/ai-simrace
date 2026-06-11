using System.Buffers.Binary;
using System.Text;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Low-level MCAP wire primitives (https://mcap.dev/spec): opcodes, magic, record framing,
/// little-endian scalars and uint32-length-prefixed UTF-8 strings.
/// </summary>
internal static class McapFormat
{
    public const byte HeaderOpcode = 0x01;
    public const byte FooterOpcode = 0x02;
    public const byte SchemaOpcode = 0x03;
    public const byte ChannelOpcode = 0x04;
    public const byte MessageOpcode = 0x05;
    public const byte ChunkOpcode = 0x06;
    public const byte DataEndOpcode = 0x0F;

    public static ReadOnlySpan<byte> Magic => [0x89, 0x4D, 0x43, 0x41, 0x50, 0x30, 0x0D, 0x0A];

    /// <summary>Record framing: opcode byte + uint64 LE content length + content.</summary>
    public static void WriteRecord(Stream target, byte opcode, ReadOnlySpan<byte> content)
    {
        Span<byte> head = stackalloc byte[9];
        head[0] = opcode;
        BinaryPrimitives.WriteUInt64LittleEndian(head[1..], (ulong)content.Length);
        target.Write(head);
        target.Write(content);
    }

    public static void WriteUInt16(Stream target, ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        target.Write(buffer);
    }

    public static void WriteUInt32(Stream target, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        target.Write(buffer);
    }

    public static void WriteUInt64(Stream target, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        target.Write(buffer);
    }

    public static void WriteString(Stream target, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        WriteUInt32(target, (uint)encoded.Length);
        target.Write(encoded);
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureAvailable(source, position, sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source[position..]);
        position += sizeof(ushort);
        return value;
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureAvailable(source, position, sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
        position += sizeof(uint);
        return value;
    }

    public static ulong ReadUInt64(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureAvailable(source, position, sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(source[position..]);
        position += sizeof(ulong);
        return value;
    }

    public static string ReadString(ReadOnlySpan<byte> source, ref int position)
    {
        int length = ReadLength32(source, ref position);
        string value = Encoding.UTF8.GetString(source.Slice(position, length));
        position += length;
        return value;
    }

    /// <summary>
    /// Reads a uint32 length and validates it against the remaining bytes BEFORE casting,
    /// so malformed lengths surface as <see cref="InvalidDataException"/>, never overflow.
    /// </summary>
    public static int ReadLength32(ReadOnlySpan<byte> source, ref int position)
    {
        uint declared = ReadUInt32(source, ref position);
        return ValidateLength(declared, source, position);
    }

    /// <summary>Same as <see cref="ReadLength32"/> for uint64 lengths.</summary>
    public static int ReadLength64(ReadOnlySpan<byte> source, ref int position)
    {
        ulong declared = ReadUInt64(source, ref position);
        return ValidateLength(declared, source, position);
    }

    private static int ValidateLength(ulong declared, ReadOnlySpan<byte> source, int position)
    {
        if (declared > (ulong)(source.Length - position))
        {
            throw new InvalidDataException(
                $"Declared length {declared} at offset {position} exceeds the {source.Length - position} remaining bytes.");
        }

        return (int)declared;
    }

    public static byte[] ReadBytes(ReadOnlySpan<byte> source, ref int position, int length)
    {
        EnsureAvailable(source, position, length);
        byte[] value = source.Slice(position, length).ToArray();
        position += length;
        return value;
    }

    public static void EnsureAvailable(ReadOnlySpan<byte> source, int position, int required)
    {
        // long arithmetic: position + required must not wrap for adversarial inputs
        if (position < 0 || required < 0 || (long)position + required > source.Length)
        {
            throw new InvalidDataException(
                $"Truncated MCAP data: needed {required} bytes at offset {position}, "
                + $"but only {source.Length - position} remain.");
        }
    }
}
