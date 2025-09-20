using SW.TCPLoadBalancer.Server.Options;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Extensions;

public static class TcpClientExtensions
{
    public static void SetOptions(this TcpClient client, ServerOptions options)
    {
        client.ReceiveTimeout = options.ReceiveTimeoutMs;
        client.SendTimeout = options.SendTimeoutMs;
        client.ReceiveBufferSize = options.ReceiveBufferSize;
        client.SendBufferSize = options.SendBufferSize;
        client.NoDelay = true; // lower latency for small messages
    }
}