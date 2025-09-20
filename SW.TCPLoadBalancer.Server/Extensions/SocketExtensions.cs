using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Extensions;

public static class SocketExtensions
{
    public static string GetSocketKey(this Socket socket)
    {
        // Can throw disposed
        return socket.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
    }

    public static void DisposeSafely(this TcpClient? outClient)
    {
        try
        {
            outClient?.Dispose();
        }
        catch (Exception) { }
    }
}
