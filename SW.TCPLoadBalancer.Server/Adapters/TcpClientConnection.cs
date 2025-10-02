using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.Extensions;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Adapters;
// TODO: WIP TcpClient wrapper
/*
public class TcpClientConnection(TcpClient tcpClient) : INetworkConnection
{
    private readonly TcpClient _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));

    public bool IsConnected => _tcpClient.Connected;

    public string RemoteEndpoint => _tcpClient.Client.GetRemoteSocketKey();

    public string LocalEndpoint => _tcpClient.Client.GetLocalSocketKey();

    public void Close() => _tcpClient.Close();

    public void Dispose()
    {
        _tcpClient?.Close();
        _tcpClient?.Dispose();
    }

    public Stream GetStream() => _tcpClient.GetStream();
}*/
