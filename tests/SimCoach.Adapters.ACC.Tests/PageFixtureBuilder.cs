using System.Buffers.Binary;
using System.Text;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Builds synthetic ACC shared-memory pages by writing values at documented native offsets.
/// Verifies struct layouts end-to-end, independently of <c>Marshal.OffsetOf</c>.
/// </summary>
internal sealed class PageFixtureBuilder
{
    private readonly byte[] _page;

    public PageFixtureBuilder(int sizeBytes)
    {
        _page = new byte[sizeBytes];
    }

    public PageFixtureBuilder WithInt32(int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_page.AsSpan(offset, sizeof(int)), value);
        return this;
    }

    public PageFixtureBuilder WithSingle(int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_page.AsSpan(offset, sizeof(float)), value);
        return this;
    }

    /// <summary>
    /// Writes a UTF-16LE string into a wchar_t[<paramref name="capacityChars"/>] field.
    /// The null terminator comes from the zero-initialized page when the value is shorter
    /// than the capacity; a value filling the full capacity is unterminated (torn-read shape).
    /// Throws when the value would overflow the field into its neighbor.
    /// </summary>
    public PageFixtureBuilder WithUtf16(int offset, string value, int capacityChars)
    {
        if (value.Length > capacityChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Value of {value.Length} chars overflows a wchar_t[{capacityChars}] field at offset {offset}.");
        }

        byte[] encoded = Encoding.Unicode.GetBytes(value);
        encoded.CopyTo(_page.AsSpan(offset, encoded.Length));
        return this;
    }

    public byte[] Build() => (byte[])_page.Clone();
}
