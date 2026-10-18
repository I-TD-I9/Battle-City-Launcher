using System.Buffers.Binary;
using System.Text;

namespace BattleCity.Client;

/// <summary>
/// A 32-bit Windows program held as bytes and addressed by virtual address: enough to change its
/// data and code in place and keep the checksum in its header right.
/// </summary>
public sealed class PeImage
{
    readonly byte[] data;
    readonly Section[] sections;
    readonly int sectionTable;

    /// <summary>
    /// A section as its header gives it: where it starts in memory and how far it reaches there,
    /// and where its bytes lie in the file.
    /// </summary>
    public readonly record struct Section(string Name, uint VirtualSize, uint VirtualAddress, uint RawSize,
                                          uint RawOffset);

    /// <summary>Reads the headers of <paramref name="data"/>, which the image then changes in place.</summary>
    public PeImage(byte[] data)
    {
        this.data = data;
        if (data.Length < 0x40 || data[0] != (byte)'M' || data[1] != (byte)'Z')
            throw new PatchException("the file is not a Windows program: it has no MZ header");
        int pe = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x3C));
        // Aligned, as linkers place it, so that the checksum field covers two whole words.
        if (pe < 0x40 || pe > data.Length - 24 || (pe & 3) != 0 || U32(pe) != 0x00004550)
            throw new PatchException("the file has no PE header");
        if (U16(pe + 4) != 0x14C)
            throw new PatchException("the file is not a 32-bit x86 program");
        int count = U16(pe + 6);
        int optionalSize = U16(pe + 20);
        int optional = pe + 24;
        if (optionalSize < 68 || optional + optionalSize > data.Length || U16(optional) != 0x10B)
            throw new PatchException("the file has no PE32 optional header");
        ImageBase = U32(optional + 28);
        ChecksumOffset = optional + 64;
        int table = optional + optionalSize;
        if (count == 0 || table + count * 40 > data.Length)
            throw new PatchException("the file's section table runs past its end");
        sectionTable = table;
        sections = new Section[count];
        for (int i = 0; i < count; i++)
        {
            int s = table + i * 40;
            sections[i] = new Section(Encoding.ASCII.GetString(data, s, 8).TrimEnd('\0'),
                                      VirtualSize: U32(s + 8), VirtualAddress: U32(s + 12),
                                      RawSize: U32(s + 16), RawOffset: U32(s + 20));
        }
    }

    public uint ImageBase { get; }

    /// <summary>The file offset of the optional header's CheckSum field.</summary>
    public int ChecksumOffset { get; }

    /// <summary>The image's bytes, with every change made so far.</summary>
    public byte[] Bytes => data;

    public uint StoredChecksum => U32(ChecksumOffset);

    /// <summary>
    /// The file offset of the <paramref name="count"/> bytes at <paramref name="va"/>, which must
    /// all lie in one section's bytes on disk: past them a section exists only in memory, as the
    /// zeros the loader supplies.
    /// </summary>
    public int Offset(uint va, int count)
    {
        foreach (var s in sections)
        {
            ulong start = (ulong)ImageBase + s.VirtualAddress;
            if (va < start || va >= start + Math.Max(s.VirtualSize, s.RawSize))
                continue;
            ulong into = va - start;
            if (into + (ulong)count > s.RawSize || (ulong)s.RawOffset + s.RawSize > (ulong)data.Length)
                throw new PatchException($"the {count} bytes at 0x{va:x} are not all in the file's {s.Name} section");
            return (int)(s.RawOffset + into);
        }
        throw new PatchException($"0x{va:x} is outside every section of the file");
    }

    public ReadOnlySpan<byte> Read(uint va, int count) => data.AsSpan(Offset(va, count), count);

    public void Write(uint va, ReadOnlySpan<byte> bytes) => bytes.CopyTo(data.AsSpan(Offset(va, bytes.Length)));

    /// <summary>The section called <paramref name="name"/>, which the file must have once.</summary>
    public Section SectionNamed(string name) => sections[IndexOf(name)];

    /// <summary>
    /// Sets how far the section called <paramref name="name"/> reaches in memory. Code written
    /// past that size, into the padding that fills the section's bytes on disk out to the file's
    /// alignment, lies outside the section until the size covers it.
    /// </summary>
    public void SetVirtualSize(string name, uint size)
    {
        int i = IndexOf(name);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionTable + i * 40 + 8), size);
        sections[i] = sections[i] with { VirtualSize = size };
    }

    int IndexOf(string name)
    {
        int found = -1;
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i].Name != name)
                continue;
            if (found >= 0)
                throw new PatchException($"the file has more than one section called {name}");
            found = i;
        }
        return found >= 0 ? found : throw new PatchException($"the file has no section called {name}");
    }

    public void UpdateChecksum() =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(ChecksumOffset), Checksum(data, ChecksumOffset));

    /// <summary>
    /// The checksum Windows' CheckSumMappedFile computes: the file summed as little-endian 16-bit
    /// words with every carry folded back into the low 16 bits, the checksum field itself left
    /// out, and the file's length added at the end.
    /// </summary>
    public static uint Checksum(ReadOnlySpan<byte> file, int checksumOffset)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 1 < file.Length; i += 2)
        {
            if (i == checksumOffset || i == checksumOffset + 2)
                continue;
            sum += BinaryPrimitives.ReadUInt16LittleEndian(file[i..]);
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        if (i < file.Length)
        {
            // An odd last byte counts as a word whose high byte is zero.
            sum += file[i];
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        return sum + (uint)file.Length;
    }

    ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));

    uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
}
