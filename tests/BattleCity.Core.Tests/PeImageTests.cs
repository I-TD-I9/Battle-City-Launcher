using BattleCity.Client;

namespace BattleCity.Tests;

public class PeImageTests
{
    [Fact]
    public void Addresses_map_to_their_section_on_disk()
    {
        var image = new PeImage(TestImage.Build());
        Assert.Equal(0x400000u, image.ImageBase);
        Assert.Equal(TestImage.HostOffset, image.Offset(TestImage.HostVa, 32));
        Assert.Equal(TestImage.PortOffset, image.Offset(TestImage.PortVa, 6));
        Assert.Equal(TestImage.ChecksumOffset, image.ChecksumOffset);
    }

    [Fact]
    public void Padding_past_a_sections_size_in_memory_maps_to_its_bytes_on_disk()
    {
        var image = new PeImage(TestImage.Build());
        Assert.Equal(TestImage.StubOffset, image.Offset(TestImage.StubVa, 27));
        // The last byte on disk, and none past it.
        Assert.Equal(TestImage.TextRaw + 0xFFF, image.Offset(0x6ECFFF, 1));
        Assert.Throws<PatchException>(() => image.Offset(0x6ECFFF, 2));
    }

    [Fact]
    public void Sections_are_found_by_name()
    {
        var image = new PeImage(TestImage.Build());
        Assert.Equal(new PeImage.Section(".data", 0x5000, TestImage.DataRva, 0x1000, TestImage.DataRaw),
                     image.SectionNamed(".data"));
        Assert.Equal(new PeImage.Section(".text", TestImage.TextVirtualSize, TestImage.TextRva, 0x1000, TestImage.TextRaw),
                     image.SectionNamed(".text"));
        Assert.Throws<PatchException>(() => image.SectionNamed(".rsrc"));
        Assert.Throws<PatchException>(() => image.SectionNamed(".TEXT"));
    }

    [Fact]
    public void A_name_two_sections_share_is_refused()
    {
        var data = TestImage.Build();
        // .text5 becomes a second .text.
        data[TestImage.TextVirtualSizeOffset - 40 - 8 + 5] = 0;
        Assert.Throws<PatchException>(() => new PeImage(data).SectionNamed(".text"));
    }

    [Fact]
    public void SetVirtualSize_writes_the_sections_header()
    {
        var data = TestImage.Build();
        var image = new PeImage(data);
        image.SetVirtualSize(".text", 0x1000);
        Assert.Equal(0x1000u, image.SectionNamed(".text").VirtualSize);
        Assert.Equal([0x00, 0x10, 0x00, 0x00], data.AsSpan(TestImage.TextVirtualSizeOffset, 4).ToArray());
        Assert.Equal(0x1000u, new PeImage(data).SectionNamed(".text").VirtualSize);
        // Nothing else in the header moves.
        var before = TestImage.Build();
        for (int i = 0; i < 0x400; i++)
            if (i < TestImage.TextVirtualSizeOffset || i >= TestImage.TextVirtualSizeOffset + 4)
                Assert.True(before[i] == data[i], $"byte 0x{i:x} changed");
        Assert.Equal(TestImage.StubOffset, image.Offset(TestImage.StubVa, 27));
    }

    [Fact]
    public void An_address_outside_every_section_is_refused()
    {
        var image = new PeImage(TestImage.Build());
        Assert.Throws<PatchException>(() => image.Offset(0x400000, 4));
        Assert.Throws<PatchException>(() => image.Offset(0x900000, 4));
    }

    [Fact]
    public void Bytes_that_exist_only_in_memory_are_refused()
    {
        var image = new PeImage(TestImage.Build());
        uint pastDisk = 0x400000 + TestImage.DataRva + 0x1000;
        Assert.Throws<PatchException>(() => image.Offset(pastDisk, 4));
        // Straddling the end of the section's bytes on disk.
        Assert.Throws<PatchException>(() => image.Offset(pastDisk - 2, 4));
    }

    [Theory]
    [InlineData(0, (byte)'X')]
    [InlineData(TestImage.PeOffset, (byte)'X')]
    public void Not_a_PE_file_is_refused(int at, byte value)
    {
        var data = TestImage.Build();
        data[at] = value;
        Assert.Throws<PatchException>(() => new PeImage(data));
    }

    [Fact]
    public void A_64_bit_program_is_refused()
    {
        var data = TestImage.Build();
        data[TestImage.PeOffset + 4] = 0x64;
        data[TestImage.PeOffset + 5] = 0x86;
        Assert.Throws<PatchException>(() => new PeImage(data));
    }

    [Fact]
    public void A_truncated_file_is_refused()
    {
        Assert.Throws<PatchException>(() => new PeImage(TestImage.Build()[..0x100]));
        Assert.Throws<PatchException>(() => new PeImage(new byte[16]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_checksum_matches_a_second_algorithm(int extra)
    {
        var data = TestImage.Build();
        Array.Resize(ref data, data.Length + extra);
        for (int i = 0; i < extra; i++)
            data[^(i + 1)] = (byte)(0xF0 + i);
        new Random(7).NextBytes(data.AsSpan(TestImage.DataRaw + 0x100, 0x400));
        Assert.Equal(TestImage.ReferenceChecksum(data, TestImage.ChecksumOffset),
                     PeImage.Checksum(data, TestImage.ChecksumOffset));
    }

    [Fact]
    public void The_checksum_leaves_its_own_field_out()
    {
        var data = TestImage.Build();
        uint before = PeImage.Checksum(data, TestImage.ChecksumOffset);
        data[TestImage.ChecksumOffset] = 0x5A;
        data[TestImage.ChecksumOffset + 3] = 0xA5;
        Assert.Equal(before, PeImage.Checksum(data, TestImage.ChecksumOffset));
    }

    [Fact]
    public void UpdateChecksum_writes_it_into_the_header()
    {
        var image = new PeImage(TestImage.Build());
        image.UpdateChecksum();
        Assert.NotEqual(0u, image.StoredChecksum);
        Assert.Equal(TestImage.ReferenceChecksum(image.Bytes, TestImage.ChecksumOffset), image.StoredChecksum);
    }
}
