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

    // call [0x6ed238], LoadLibraryA, with "ddraw.dll", then jmp [0x6ed3e0], CoCreateInstance.
    static readonly byte[] Stub =
    [
        0x68, 0xE1, 0xCF, 0x6E, 0x00, 0xFF, 0x15, 0x38, 0xD2, 0x6E, 0x00, 0xFF, 0x25, 0xE0, 0xD3, 0x6E, 0x00,
        .. "ddraw.dll\0"u8,
    ];

    [Fact]
    public void Only_the_patched_bytes_and_the_checksum_change_for_the_public_server()
    {
        var retail = TestImage.Build();
        var patched = ClientPatcher.Patch(retail, ServerEndpoints.Public);
        Assert.Equal(5730, PortIn(patched));
        static bool In(int i, int start, int count) => i >= start && i < start + count;
        for (int i = 0; i < retail.Length; i++)
        {
            bool slot = In(i, TestImage.HostOffset, 32);
            bool root = i == TestImage.RegistryOffset + 1;
            bool jump = i == TestImage.UpdaterOffset;
            bool activation = i == TestImage.ActivationOffset + 2;
            bool call = In(i, TestImage.DirectDrawOffset, 6);
            bool stub = In(i, TestImage.StubOffset, Stub.Length);
            bool size = In(i, TestImage.TextVirtualSizeOffset, 4);
            bool checksum = In(i, TestImage.ChecksumOffset, 4);
            if (!slot && !root && !jump && !activation && !call && !stub && !size && !checksum)
                Assert.True(retail[i] == patched[i], $"byte 0x{i:x} changed");
        }
        Assert.Equal(TestImage.ReferenceChecksum(patched, TestImage.ChecksumOffset),
                     BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(TestImage.ChecksumOffset)));
    }

    [Fact]
    public void A_recordless_cards_activation_is_accepted()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        // je 0x5e23df, the activation check's acceptance, where the retail client has je 0x5e23ce.
        Assert.Equal([0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00], patched.AsSpan(TestImage.ActivationOffset, 6).ToArray());
    }

    [Fact]
    public void Another_instruction_where_the_activation_check_jumps_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.ActivationOffset + 2] = 0x91;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void DirectDraw_is_made_through_a_stub_that_loads_the_wrapper_first()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        // call 0x6ecfd0; nop, where the retail client has call [0x6ed3e0].
        Assert.Equal([0xE8, 0xC8, 0x1E, 0x21, 0x00, 0x90], patched.AsSpan(TestImage.DirectDrawOffset, 6).ToArray());
        Assert.Equal(Stub, patched.AsSpan(TestImage.StubOffset, Stub.Length).ToArray());
        int target = (int)TestImage.DirectDrawVa + 5 + BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(TestImage.DirectDrawOffset + 1));
        Assert.Equal(TestImage.StubVa, (uint)target);
        // The name the stub pushes is its own last ten bytes.
        Assert.Equal(TestImage.StubVa + 17, BinaryPrimitives.ReadUInt32LittleEndian(Stub.AsSpan(1)));
    }

    [Fact]
    public void Text_is_widened_in_memory_to_take_the_stub_in()
    {
        var patched = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        var text = new PeImage(patched).SectionNamed(".text");
        Assert.Equal(0x1000u, text.VirtualSize);
        Assert.Equal(text.RawSize, text.VirtualSize);
        Assert.True(TestImage.StubVa + Stub.Length <= 0x400000 + text.VirtualAddress + text.VirtualSize);
    }

    [Fact]
    public void A_text_section_already_covering_the_stub_keeps_its_size()
    {
        var image = TestImage.Build();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(TestImage.TextVirtualSizeOffset), 0xFF0);
        var patched = ClientPatcher.Patch(image, ServerEndpoints.Public);
        Assert.Equal(0xFF0u, new PeImage(patched).SectionNamed(".text").VirtualSize);
    }

    [Fact]
    public void Another_instruction_where_DirectDraw_is_made_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.DirectDrawOffset + 2] = 0xE4;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(26)]
    public void Anything_but_zeros_where_the_stub_goes_is_refused(int at)
    {
        var image = TestImage.Build();
        image[TestImage.StubOffset + at] = 0xCC;
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void A_client_without_a_text_section_is_refused()
    {
        var image = TestImage.Build();
        image[TestImage.TextVirtualSizeOffset - 8 + 1] = (byte)'T';
        Assert.Throws<PatchException>(() => ClientPatcher.Patch(image, ServerEndpoints.Public));
    }

    [Fact]
    public void Patching_the_public_client_again_changes_nothing()
    {
        var once = ClientPatcher.Patch(TestImage.Build(), ServerEndpoints.Public);
        Assert.Equal(once, ClientPatcher.Patch(once, ServerEndpoints.Public));
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
        Assert.Equal(65, retail.Zip(patched).Count(pair => pair.First != pair.Second));
        Assert.Equal(patched, ClientPatcher.Patch(patched, ServerEndpoints.Public));
    }

    [RetailClientFact]
    public void The_retail_client_gets_each_new_patch_where_it_belongs()
    {
        var retail = new PeImage(File.ReadAllBytes(RetailClient.Path!));
        Assert.Equal([0x0F, 0x84, 0x90, 0x00, 0x00, 0x00], retail.Read(ClientPatcher.RecordlessCheckVa, 6).ToArray());
        Assert.Equal([0xFF, 0x15, 0xE0, 0xD3, 0x6E, 0x00], retail.Read(ClientPatcher.DirectDrawCallVa, 6).ToArray());
        Assert.Equal(new byte[Stub.Length], retail.Read(ClientPatcher.WrapperStubVa, Stub.Length).ToArray());
        var text = retail.SectionNamed(".text");
        Assert.Equal((0x1000u, 0x2EB792u, 0x2EC000u), (text.VirtualAddress, text.VirtualSize, text.RawSize));

        var patched = new PeImage(ClientPatcher.Patch(retail.Bytes, ServerEndpoints.Public));
        Assert.Equal([0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00], patched.Read(ClientPatcher.RecordlessCheckVa, 6).ToArray());
        Assert.Equal([0xE8, 0xC8, 0x1E, 0x21, 0x00, 0x90], patched.Read(ClientPatcher.DirectDrawCallVa, 6).ToArray());
        Assert.Equal(Stub, patched.Read(ClientPatcher.WrapperStubVa, Stub.Length).ToArray());
        Assert.Equal(0x2EC000u, patched.SectionNamed(".text").VirtualSize);
        Assert.Equal(PeImage.Checksum(patched.Bytes, patched.ChecksumOffset), patched.StoredChecksum);
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
