using SW.TCPLoadBalancer.Server.DTOs;

namespace SW.TCPLoadBalancer.Server.Abstractions;

public interface INetworkClient : IDisposable
{
    Task SendAsync(SendState sendState, byte[] buffer, int bytesRead);
}
