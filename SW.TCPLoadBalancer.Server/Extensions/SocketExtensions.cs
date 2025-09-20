using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Extensions;

public static class SocketExtensions
{
    // TODO remove?
    public static bool IsConnected(this Socket socket)
    {
        try
        {
            return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch (SocketException)
        {
            return false;
        }
    }

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
