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

    /// <summary>
    /// A copy of <paramref name="client"/> that connects to <paramref name="server"/>'s gate and
    /// keeps its saves where <see cref="Game.ClientKey"/> says.
    /// </summary>
    public static byte[] Patch(ReadOnlySpan<byte> client, ServerEndpoints server)
    {
        var image = new PeImage(client.ToArray());
        SetGateHost(image, server.GateHost);
        SetGatePort(image, server.GatePort);
        SetRegistryRoot(image);
        SkipUpdaterCopy(image);
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

    /// <summary>Whether <paramref name="name"/> is a host name or an IPv4 address.</summary>
    public static bool IsHostName(string name) => name.Length is > 0 and <= 253 && HostName().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\z")]
    private static partial Regex HostName();
}
