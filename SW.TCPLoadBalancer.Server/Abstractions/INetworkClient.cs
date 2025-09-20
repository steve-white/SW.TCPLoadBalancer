using SW.TCPLoadBalancer.Server.DTOs;
using SW.TCPLoadBalancer.Server.Helpers;

namespace SW.TCPLoadBalancer.Server.Abstractions;

public interface INetworkClient : IAsyncDisposable
{
    Task SendAsync(SendState sendState, byte[] buffer, int bytesRead);
}
