using System.Net.Sockets;

namespace BattleCity;

/// <summary>Whether this machine can reach the game's server.</summary>
public static class ServerProbe
{
    /// <summary>
    /// Whether a connection to the gate opens within <paramref name="timeout"/>. It is closed
    /// straight away, which the gate takes as a client that left before saying anything. The
    /// gate rather than the sign-up API, because the game's ports are what a player's network
    /// might block.
    /// </summary>
    public static async Task<bool> GateOpensAsync(ServerEndpoints server, TimeSpan timeout,
                                                  CancellationToken cancel = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout);
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(server.GateHost, server.GatePort, limit.Token);
            return true;
        }
        catch (Exception e) when ((e is SocketException or OperationCanceledException) && !cancel.IsCancellationRequested)
        {
            return false;
        }
    }
}
