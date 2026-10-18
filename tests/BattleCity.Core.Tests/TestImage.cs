using System.Buffers.Binary;
using System.Text;

namespace BattleCity.Tests;

/// <summary>
/// A small PE32 image laid out like the game's client where the launcher patches it: the jump
/// past the updater's copy at 0x4149a8, the registry root's push at 0x4a40db, the call that makes
/// DirectDraw at 0x4db103, the activation check's jump at 0x5e2338 and the port's instruction at
/// 0x67a9f2, each in a code section; the end of .text, which reaches 0x6ec792 in memory and
/// 0x6ed000 on disk, the rest zeros; and the gate's host name in a data section at 0x71e064. The
/// real client cannot be part of the repository.
/// </summary>
static class TestImage
{
    public const int PeOffset = 0x80;
    public const int UpdaterRaw = 0x400;
    public const int RegistryRaw = 0x1400;
    public const int CodeRaw = 0x2400;
    public const int DataRaw = 0x3400;
    public const int DirectDrawRaw = 0x4400;
    public const int ActivationRaw = 0x5400;
    public const int TextRaw = 0x6400;
    public const int Length = 0x7400;
    public const uint UpdaterRva = 0x14000;
    public const uint RegistryRva = 0xA4000;
    public const uint CodeRva = 0x27A000;
    public const uint DataRva = 0x31E000;
    public const uint DirectDrawRva = 0xDB000;
    public const uint ActivationRva = 0x1E2000;
    public const uint TextRva = 0x2EC000;

    /// <summary>How far .text reaches in memory: to 0x6ec792, as in the client.</summary>
    public const uint TextVirtualSize = 0x792;

    public const uint UpdaterVa = 0x4149A8;
    public const uint RegistryVa = 0x4A40DB;
    public const uint HostVa = 0x71E064;
    public const uint PortVa = 0x67A9F2;
    public const uint DirectDrawVa = 0x4DB103;
    public const uint ActivationVa = 0x5E2338;
    public const uint StubVa = 0x6ECFD0;

    public static int UpdaterOffset => UpdaterRaw + (int)(UpdaterVa - 0x400000 - UpdaterRva);
    public static int RegistryOffset => RegistryRaw + (int)(RegistryVa - 0x400000 - RegistryRva);
    public static int HostOffset => DataRaw + (int)(HostVa - 0x400000 - DataRva);
    public static int PortOffset => CodeRaw + (int)(PortVa - 0x400000 - CodeRva);
    public static int DirectDrawOffset => DirectDrawRaw + (int)(DirectDrawVa - 0x400000 - DirectDrawRva);
    public static int ActivationOffset => ActivationRaw + (int)(ActivationVa - 0x400000 - ActivationRva);
    public static int StubOffset => TextRaw + (int)(StubVa - 0x400000 - TextRva);
    public static int ChecksumOffset => PeOffset + 24 + 64;

    // The section table follows the optional header; .text is the last of its seven entries.
    static int SectionTable => PeOffset + 24 + 0xE0;
    public static int TextVirtualSizeOffset => SectionTable + 6 * 40 + 8;

    public static byte[] Build(string host = "yugi-gate01.yugioh-online.net", ushort port = 5730)
    {
        var data = new byte[Length];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C), PeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(PeOffset), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 4), 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 6), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PeOffset + 20), 0xE0);
        int optional = PeOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), 0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        int table = SectionTable;
        Section(data, table, ".text1", UpdaterRva, 0x1000, UpdaterRaw, 0x1000);
        Section(data, table + 40, ".text2", RegistryRva, 0x1000, RegistryRaw, 0x1000);
        Section(data, table + 80, ".text3", CodeRva, 0x1000, CodeRaw, 0x1000);
        // Larger in memory than on disk, as the client's own .data is.
        Section(data, table + 120, ".data", DataRva, 0x5000, DataRaw, 0x1000);
        Section(data, table + 160, ".text4", DirectDrawRva, 0x1000, DirectDrawRaw, 0x1000);
        Section(data, table + 200, ".text5", ActivationRva, 0x1000, ActivationRaw, 0x1000);
        // Smaller in memory than on disk, as the client's own .text is.
        Section(data, table + 240, ".text", TextRva, TextVirtualSize, TextRaw, 0x1000);

        new byte[] { 0x75, 0x3D }.CopyTo(data, UpdaterOffset);
        new byte[] { 0x68, 0x02, 0x00, 0x00, 0x80 }.CopyTo(data, RegistryOffset);
        Encoding.ASCII.GetBytes(host).CopyTo(data, HostOffset);
        Encoding.ASCII.GetBytes("version error.").CopyTo(data, HostOffset + 0x20);
        new byte[] { 0x66, 0xC7, 0x45, 0x14 }.CopyTo(data, PortOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PortOffset + 4), port);
        new byte[] { 0xFF, 0x15, 0xE0, 0xD3, 0x6E, 0x00 }.CopyTo(data, DirectDrawOffset);
        new byte[] { 0x0F, 0x84, 0x90, 0x00, 0x00, 0x00 }.CopyTo(data, ActivationOffset);
        // Code elsewhere, so that the checksum has more than zeros to add up, and up to where
        // .text ends in memory.
        for (int i = 0; i < 0x200; i++)
            data[CodeRaw + 0x200 + i] = (byte)(i * 37 + 11);
        for (int i = 0; i < (int)TextVirtualSize; i++)
            data[TextRaw + i] = (byte)(i * 53 + 7);
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
