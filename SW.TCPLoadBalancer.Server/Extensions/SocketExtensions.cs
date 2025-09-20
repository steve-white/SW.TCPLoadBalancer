using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Extensions;

public static class SocketExtensions
{
    public static string GetRemoteSocketKey(this Socket socket)
    {
        // Can throw disposed
        return socket.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
    }

    public static string GetLocalSocketKey(this Socket socket)
    {
        // Can throw disposed
        return socket.LocalEndPoint?.ToString() ?? Guid.NewGuid().ToString();
    }

    public static void CloseSafely(this TcpClient? outClient)
    {
        try
        {
            outClient?.Close();
        }
        catch (Exception) { }
    }
}
