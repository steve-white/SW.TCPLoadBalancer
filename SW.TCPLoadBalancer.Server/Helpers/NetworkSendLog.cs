using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.DTOs;
using System.Text;

namespace SW.TCPLoadBalancer.Server.Helpers;

public interface ILogNetworkSend : INetworkClient { }

/// <summary>
/// Defines a no-op network client that can be used for logging backend client reads when there's no input client attached.
/// </summary>
/// <param name="log"></param>
public class NetworkSendLog(ILogger<NetworkSendLog> log) : INetworkClient, ILogNetworkSend
{
    public Task SendAsync(SendState sendState, byte[] buffer, int bytesRead)
    {
        log.LogWarning("Client message discarded: '{msg}'", Encoding.UTF8.GetString(buffer, 0, bytesRead));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        log.LogDebug("Dispose noop client");
        GC.SuppressFinalize(this);
    }
}
