using System.Buffers.Binary;
using System.Text;
using BattleCity.Client;

namespace BattleCity.Tests;

public class ClientPatcherTests
{
    static readonly ServerEndpoints Local = new("127.0.0.1", 5731, new Uri("http://127.0.0.1:8080/"));

    static string HostIn(byte[] image) =>
        Encoding.ASCII.GetString(image, TestImage.HostOffset, 32).TrimEnd('\0');

    static int PortIn(byte[] image) => BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(TestImage.PortOffset + 4));

    [Fact]
    public void The_gate_host_is_replaced_and_padded_with_zeros()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        Assert.Equal("battle-city.net", HostIn(patched));
        Assert.All(patched.AsSpan(TestImage.HostOffset + 15, 17).ToArray(), b => Assert.Equal(0, b));
        Assert.Equal("version error.", Encoding.ASCII.GetString(patched, TestImage.HostOffset + 32, 14));
    }

    [Fact]
    public void Only_the_host_slot_the_two_code_patches_and_the_checksum_change_for_the_public_server()
    {
        var retail = TestImage.Build();
        var patched = ClientPatcher.Patch(retail, ServerEndpoints.Public);
        Assert.Equal(5730, PortIn(patched));
        for (int i = 0; i < retail.Length; i++)
        {
            bool slot = i >= TestImage.HostOffset && i < TestImage.HostOffset + 32;
            bool root = i == TestImage.RegistryOffset + 1;
            bool jump = i == TestImage.UpdaterOffset;
            bool checksum = i >= TestImage.ChecksumOffset && i < TestImage.ChecksumOffset + 4;
            if (!slot && !root && !jump && !checksum)
                Assert.True(retail[i] == patched[i], $"byte 0x{i:x} changed");
        }
        Assert.Equal(TestImage.ReferenceChecksum(patched, TestImage.ChecksumOffset),
                     BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(TestImage.ChecksumOffset)));
    }

    [Fact]
    public void The_registry_is_read_from_the_current_user()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        // push 0x80000001, HKEY_CURRENT_USER, where the retail client pushes 0x80000002.
        Assert.Equal([0x68, 0x01, 0x00, 0x00, 0x80], patched.AsSpan(TestImage.RegistryOffset, 5).ToArray());
    }

    [Fact]
    public void The_updaters_copy_is_jumped_over()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        // jmp 0x4149e7, where the retail client has jne 0x4149e7.
        Assert.Equal([0xEB, 0x3D], patched.AsSpan(TestImage.UpdaterOffset, 2).ToArray());
    }

    [Fact]
    public void Another_instruction_where_the_updaters_jump_should_be_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.UpdaterOffset] = 0x74;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void Another_instruction_where_the_registry_root_should_be_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.RegistryOffset + 1] = 0x03;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void The_input_is_left_as_it_was()
    {
        var retail = TestImage.Build();
        var copy = (byte[])retail.Clone();
        ClientPatcher.Patch(retail, Local);
        Assert.Equal(copy, retail);
    }

    [Fact]
    public void The_port_is_replaced()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), Local);
        Assert.Equal(5731, PortIn(patched));
        Assert.Equal("127.0.0.1", HostIn(patched));
    }

    [Fact]
    public void A_patched_client_can_be_patched_again()
    {
        var once = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        var twice = ClientPatcher.Patch(once, Local);
        Assert.Equal("127.0.0.1", HostIn(twice));
        Assert.Equal(ClientPatcher.Patch(TestImage.Build(), Local), twice);
    }

    [Fact]
    public void A_host_of_31_characters_fits_and_32_does_not()
    {
        var fits = new string('a', 27) + ".net";
        Assert.Equal(fits, HostIn(ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public with { GateHost = fits })));
        var tooLong = new string('a', 28) + ".net";
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public with { GateHost = tooLong }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad host")]
    [InlineData("-dash.net")]
    [InlineData("a..b")]
    [InlineData("host.net\n")]
    [InlineData("exämple.net")]
    public void A_name_that_is_no_host_name_is_refused(string host)
    {
        Assert.False(ClientPatcher.IsHostName(host));
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public with { GateHost = host }));
    }

    [Theory]
    [InlineData("battle-city.net")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("yugi-gate01.yugioh-online.net")]
    public void Host_names_and_addresses_are_accepted(string host) => Assert.True(ClientPatcher.IsHostName(host));

    [Fact]
    public void A_slot_with_bytes_after_the_name_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.HostOffset + 30] = (byte)'x';
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void An_empty_slot_is_refused()
    {
        var image = TestImage.Build();
        Array.Clear(image, TestImage.HostOffset, 32);
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void A_slot_holding_something_else_is_refused()
    {
        var image = TestImage.Build(host: "%s:%d bad");
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void Another_instruction_where_the_port_should_be_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.PortOffset + 3] = 0x10;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void A_port_out_of_range_is_refused(int port) =>
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public with { GatePort = port }));

    [RetailClientFact]
    public void The_retail_client_patches_to_the_public_client()
    {
        var retail = File.ReadAllBytes(RetailClient.Path!);
        Assert.Equal(SupportedClient.Sha256, SupportedClient.HashOf(retail));
        var patched = ClientPatcher.Patch(retail, ServerEndpoints.Public);
        Assert.Equal(SupportedClient.PublicSha256, SupportedClient.HashOf(patched));
    }

    [RetailClientFact]
    public void The_retail_checksum_is_zero_and_computes_as_pefile_does()
    {
        var image = new PeImage(File.ReadAllBytes(RetailClient.Path!));
        Assert.Equal(0u, image.StoredChecksum);
        // pefile's generate_checksum() on the same file.
        Assert.Equal(0x32AB65u, PeImage.Checksum(image.Bytes, image.ChecksumOffset));
        Assert.Equal(TestImage.ReferenceChecksum(image.Bytes, image.ChecksumOffset),
                     PeImage.Checksum(image.Bytes, image.ChecksumOffset));
    }
}
