using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.DTOs;
using System.Text;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface ILogNetworkSend : INetworkClient { }

/// <summary>
/// Defines a no-op network sender that can be used for logging backend data sends when there's no client attached.
/// </summary>
/// <param name="log"></param>
public class NetworkSendLog(ILogger<NetworkSendLog> log) : INetworkClient, ILogNetworkSend
{
    public Task SendAsync(SendState sendState, byte[] buffer, int bytesRead)
    {
        log.LogWarning("Message to client lost: '{msg}'", Encoding.UTF8.GetString(buffer, 0, bytesRead));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        log.LogDebug("Dispose client");
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
