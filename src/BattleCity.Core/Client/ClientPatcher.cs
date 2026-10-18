using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace BattleCity.Client;

/// <summary>
/// Points a copy of the game's client at a server. Each change first checks the bytes it
/// replaces, so a build the launcher was not written for is refused rather than broken.
/// </summary>
public static partial class ClientPatcher
{
    // The gate's host name, the only game server address the client holds: the account, lobby
    // and duel servers are named by the gate's directory when the client connects. The slot is
    // 32 bytes, up to 0x71e084 where "version error." begins, and the connection constructor
    // measures the string when it runs (repne scasb at 0x67a9a6), so any name of up to 31
    // characters fits.
    internal const uint GateHostVa = 0x71E064;
    internal const int GateHostSlot = 0x20;
    internal const string RetailGateHost = "yugi-gate01.yugioh-online.net";

    // The gate's port, the immediate of mov word ptr [ebp+0x14], 0x1662 at 0x67a9f2.
    internal const uint GatePortVa = 0x67A9F2;
    internal const int RetailGatePort = 5730;
    static ReadOnlySpan<byte> GatePortOpcode => [0x66, 0xC7, 0x45, 0x14];

    // The client finds the folder it keeps its saves in, CommonDir, in the registry, and every
    // registry access it makes goes through the one RegCreateKeyExA at 0x4a40e0, whose root is
    // the push at 0x4a40db. That push names HKEY_LOCAL_MACHINE, where an installed game points
    // at its install folder. The copy names HKEY_CURRENT_USER instead, under the same key, which
    // the launcher points at a folder of its own (ClientKey).
    internal const uint RegistryRootVa = 0x4A40DB;
    static ReadOnlySpan<byte> PushLocalMachine => [0x68, 0x02, 0x00, 0x00, 0x80];
    static ReadOnlySpan<byte> PushCurrentUser => [0x68, 0x01, 0x00, 0x00, 0x80];

    // At start-up the client copies ygo.dat over ygo.exe in its working folder when their
    // last-write times differ, which is how Konami's updater replaced itself. The jne at 0x4149a8,
    // taken when there is no ygo.dat, becomes a jmp, so the copy never runs and never rewrites
    // ygo.exe in the game's folder.
    internal const uint UpdaterCopyVa = 0x4149A8;
    static ReadOnlySpan<byte> JumpIfNoUpdater => [0x75, 0x3D];
    static ReadOnlySpan<byte> JumpPastUpdater => [0xEB, 0x3D];

    // Before the client offers to activate a card, sub_5e2290 looks the card up in its routine
    // records, and it turns away one that has neither a main record (0x5e22af) nor a secondary
    // one: the je at 0x5e2338 leads to its refusal at 0x5e23ce, xor eax, eax and ret. Hundreds of
    // the client's cards have no record at all. Aimed at 0x5e23df instead, mov eax, 1 and ret, the
    // jump accepts such a card, so the client offers its activation and the server decides, as it
    // does for every other card. Online the client never runs a card's own routines (the records'
    // slots 1, 4 and 5 belong to the chain and resolution runners, which the server never starts),
    // so nothing runs that did not before.
    internal const uint RecordlessCheckVa = 0x5E2338;
    static ReadOnlySpan<byte> RefuseRecordless => [0x0F, 0x84, 0x90, 0x00, 0x00, 0x00];
    static ReadOnlySpan<byte> AcceptRecordless => [0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00];

    // The client makes its DirectDraw object in one place, the CoCreateInstance at 0x4db103 in
    // sub_4db0f0, and COM loads the system's ddraw.dll by its full path, so a DirectDraw wrapper
    // beside the client would never be used. The call goes instead to a stub in the zeros at the
    // end of .text, which loads ddraw.dll by name first: LoadLibraryA takes a wrapper from the
    // client's own folder when there is one there, and otherwise the system's DLL, the one COM
    // loads a moment later anyway, so without a wrapper the game runs as it always has. The stub
    // then jumps on to CoCreateInstance, which returns to the nop after the call, at 0x4db108.
    internal const uint DirectDrawCallVa = 0x4DB103;
    internal const uint WrapperStubVa = 0x6ECFD0;
    // call [0x6ed3e0], the import table's slot for CoCreateInstance.
    static ReadOnlySpan<byte> CallCoCreateInstance => [0xFF, 0x15, 0xE0, 0xD3, 0x6E, 0x00];
    // call 0x6ecfd0; nop
    static ReadOnlySpan<byte> CallWrapperStub => [0xE8, 0xC8, 0x1E, 0x21, 0x00, 0x90];
    static ReadOnlySpan<byte> WrapperStub =>
    [
        0x68, 0xE1, 0xCF, 0x6E, 0x00,        // push 0x6ecfe1, the name below
        0xFF, 0x15, 0x38, 0xD2, 0x6E, 0x00,  // call [0x6ed238], LoadLibraryA
        0xFF, 0x25, 0xE0, 0xD3, 0x6E, 0x00,  // jmp [0x6ed3e0], CoCreateInstance
        0x64, 0x64, 0x72, 0x61, 0x77, 0x2E, 0x64, 0x6C, 0x6C, 0x00,  // "ddraw.dll"
    ];

    /// <summary>
    /// A copy of <paramref name="client"/> that connects to <paramref name="server"/>'s gate,
    /// keeps its saves where <see cref="Game.ClientKey"/> says, offers to activate the cards it
    /// has no routines for, and loads a DirectDraw wrapper put beside it.
    /// </summary>
    public static byte[] Patch(ReadOnlySpan<byte> client, ServerEndpoints server)
    {
        var image = new PeImage(client.ToArray());
        SetGateHost(image, server.GateHost);
        SetGatePort(image, server.GatePort);
        SetRegistryRoot(image);
        SkipUpdaterCopy(image);
        AcceptRecordlessActivation(image);
        LoadWrapperFirst(image);
        // The retail file leaves its checksum at zero; the copy gets the right one, as a valid
        // image has.
        image.UpdateChecksum();
        return image.Bytes;
    }

    internal static void SetGateHost(PeImage image, string host)
    {
        if (!IsHostName(host))
            throw new PatchException($"'{host}' is not a host name or an IPv4 address");
        if (host.Length > GateHostSlot - 1)
            throw new PatchException($"'{host}' is {host.Length} characters; the client holds at most {GateHostSlot - 1}");
        var slot = image.Read(GateHostVa, GateHostSlot);
        // The retail name or one written here before: a host name, then zeros to the slot's end.
        int end = slot.IndexOf((byte)0);
        if (end <= 0 || slot[end..].ContainsAnyExcept((byte)0) || !IsHostName(Encoding.ASCII.GetString(slot[..end])))
            throw new PatchException($"the gate's host name slot at 0x{GateHostVa:x} does not hold a host name: {Convert.ToHexString(slot)}");
        var replacement = new byte[GateHostSlot];
        Encoding.ASCII.GetBytes(host.AsSpan(), replacement);
        image.Write(GateHostVa, replacement);
    }

    internal static void SetGatePort(PeImage image, int port)
    {
        if (port is < 1 or > 0xFFFF)
            throw new PatchException($"port {port} is out of range");
        var instruction = image.Read(GatePortVa, 6);
        if (!instruction[..4].SequenceEqual(GatePortOpcode))
            throw new PatchException($"expected mov word ptr [ebp+0x14], imm16 at 0x{GatePortVa:x}, found {Convert.ToHexString(instruction)}");
        Span<byte> value = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(value, (ushort)port);
        image.Write(GatePortVa + 4, value);
    }

    internal static void SetRegistryRoot(PeImage image)
    {
        var instruction = image.Read(RegistryRootVa, PushLocalMachine.Length);
        if (instruction.SequenceEqual(PushCurrentUser))
            return;
        if (!instruction.SequenceEqual(PushLocalMachine))
            throw new PatchException($"expected push HKEY_LOCAL_MACHINE at 0x{RegistryRootVa:x}, found {Convert.ToHexString(instruction)}");
        image.Write(RegistryRootVa, PushCurrentUser);
    }

    internal static void SkipUpdaterCopy(PeImage image)
    {
        var instruction = image.Read(UpdaterCopyVa, JumpIfNoUpdater.Length);
        if (instruction.SequenceEqual(JumpPastUpdater))
            return;
        if (!instruction.SequenceEqual(JumpIfNoUpdater))
            throw new PatchException($"expected jne 0x4149e7 at 0x{UpdaterCopyVa:x}, found {Convert.ToHexString(instruction)}");
        image.Write(UpdaterCopyVa, JumpPastUpdater);
    }

    internal static void AcceptRecordlessActivation(PeImage image)
    {
        var instruction = image.Read(RecordlessCheckVa, RefuseRecordless.Length);
        if (instruction.SequenceEqual(AcceptRecordless))
            return;
        if (!instruction.SequenceEqual(RefuseRecordless))
            throw new PatchException($"expected je 0x5e23ce at 0x{RecordlessCheckVa:x}, found {Convert.ToHexString(instruction)}");
        image.Write(RecordlessCheckVa, AcceptRecordless);
    }

    internal static void LoadWrapperFirst(PeImage image)
    {
        var stub = image.Read(WrapperStubVa, WrapperStub.Length);
        if (!stub.SequenceEqual(WrapperStub))
        {
            if (stub.ContainsAnyExcept((byte)0))
                throw new PatchException($"expected zeros at 0x{WrapperStubVa:x}, found {Convert.ToHexString(stub)}");
            image.Write(WrapperStubVa, WrapperStub);
        }

        // In memory .text ends at 0x6ec792, where its header's size, 0x2eb792, runs out; its bytes
        // on disk go on to 0x6ed000, the rest of them padding. Given its size on disk instead, the
        // section takes the stub in.
        var text = image.SectionNamed(".text");
        ulong start = (ulong)image.ImageBase + text.VirtualAddress;
        ulong end = (ulong)WrapperStubVa + (uint)WrapperStub.Length;
        if (WrapperStubVa < start || end > start + text.RawSize)
            throw new PatchException($"the stub at 0x{WrapperStubVa:x} is not in the bytes of .text");
        if (end > start + text.VirtualSize)
            image.SetVirtualSize(".text", text.RawSize);

        var call = image.Read(DirectDrawCallVa, CallCoCreateInstance.Length);
        if (call.SequenceEqual(CallWrapperStub))
            return;
        if (!call.SequenceEqual(CallCoCreateInstance))
            throw new PatchException($"expected call [0x6ed3e0] at 0x{DirectDrawCallVa:x}, found {Convert.ToHexString(call)}");
        image.Write(DirectDrawCallVa, CallWrapperStub);
    }

    /// <summary>Whether <paramref name="name"/> is a host name or an IPv4 address.</summary>
    public static bool IsHostName(string name) => name.Length is > 0 and <= 253 && HostName().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\z")]
    private static partial Regex HostName();
}
