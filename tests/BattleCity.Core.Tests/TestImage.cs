using System.Buffers.Binary;
using System.Text;

namespace BattleCity.Tests;

/// <summary>
/// A small PE32 image laid out like the game's client where the launcher patches it: the gate's
/// host name in a data section at 0x71e064 and the port's instruction in a code section at
/// 0x67a9f2. The real client cannot be part of the repository.
/// </summary>
static class TestImage
{
    public const int PeOffset = 0x80;
    public const int CodeRaw = 0x400;
    public const int DataRaw = 0x1400;
    public const int Length = 0x2400;
    public const uint CodeRva = 0x27A000;
    public const uint DataRva = 0x31E000;

    public const uint HostVa = 0x71E064;
    public const uint PortVa = 0x67A9F2;

    public static int HostOffset => DataRaw + (int)(HostVa - 0x400000 - DataRva);
    public static int PortOffset => CodeRaw + (int)(PortVa - 0x400000 - CodeRva);
    public static int ChecksumOffset => PeOffset + 24 + 64;

    public static byte[] Build(string host = "yugi-gate01.yugioh-online.net", ushort port = 5730)
    {
        var data = new byte[Length];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C), PeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(PeOffset), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 4), 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 6), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 20), 0xE0);
        int optional = PeOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), 0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        int table = optional + 0xE0;
        Section(data, table, ".text", CodeRva, 0x1000, CodeRaw, 0x1000);
        // Larger in memory than on disk, as the client's own .data is.
        Section(data, table + 40, ".data", DataRva, 0x5000, DataRaw, 0x1000);

        Encoding.ASCII.GetBytes(host).CopyTo(data, HostOffset);
        Encoding.ASCII.GetBytes("version error.").CopyTo(data, HostOffset + 0x20);
        new byte[] { 0x66, 0xC7, 0x45, 0x14 }.CopyTo(data, PortOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PortOffset + 4), port);
        // Code elsewhere, so that the checksum has more than zeros to add up.
        for (int i = 0; i < 0x200; i++)
            data[CodeRaw + 0x200 + i] = (byte)(i * 37 + 11);
        return data;
    }

    static void Section(byte[] data, int at, string name, uint rva, uint virtualSize, int raw, int rawSize)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(data, at);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8), virtualSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12), rva);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 16), (uint)rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 20), (uint)raw);
    }

    /// <summary>
    /// The PE checksum as pefile computes it, over 32-bit words with an end-around carry and then
    /// folded to 16 bits: a second way to the same number, to check the launcher's against.
    /// </summary>
    public static uint ReferenceChecksum(byte[] data, int checksumOffset)
    {
        ulong sum = 0;
        int remainder = data.Length % 4;
        int count = (data.Length + 3) / 4;
        for (int i = 0; i < count; i++)
        {
            if (i == checksumOffset / 4)
                continue;
            var word = new byte[4];
            Array.Copy(data, i * 4, word, 0, i + 1 == count && remainder != 0 ? remainder : 4);
            sum += BinaryPrimitives.ReadUInt32LittleEndian(word);
            if (sum >= 1UL << 32)
                sum = (sum & 0xFFFFFFFF) + (sum >> 32);
        }
        sum = (sum & 0xFFFF) + (sum >> 16);
        sum += sum >> 16;
        return (uint)(sum & 0xFFFF) + (uint)data.Length;
    }
}
