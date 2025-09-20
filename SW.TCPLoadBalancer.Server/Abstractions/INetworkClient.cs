using SW.TCPLoadBalancer.Server.DTOs;

namespace SW.TCPLoadBalancer.Server.Abstractions;

public interface INetworkClient : IAsyncDisposable
{
    Task SendAsync(SendState sendState, byte[] buffer, int bytesRead);
}
