using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface IConnectionOutSelector
{
    public ConnectionDetails? GetAvailableBackendConnectionDetails();
}

public class ConnectionOutSelector(
    IOptions<ServerOptions> serverOptions,
    IConnectionsOutRegistry connectionsOutRegistry) : IConnectionOutSelector
{
    private readonly ServerOptions _serverOptions = serverOptions.Value;
    private readonly IConnectionsOutRegistry _connectionsOutRegistry = connectionsOutRegistry;

    public ConnectionDetails? GetAvailableBackendConnectionDetails()
    {
        // TODO improve this. Don't rely on connectionKey prefix & substring to identify watchdog connections.
        // possibly maintain a separate registry of watchdog connections
        var watchdogConnections = _connectionsOutRegistry.ActiveConnections
            .Where(x => x.Value.ConnectionKey!.StartsWith("watchdog-")).ToList();
        if (_connectionsOutRegistry.ActiveConnections.IsEmpty)
            return null;

        var index = Environment.TickCount % watchdogConnections.Count;
        var connectionKey = GetBackendConnectionAlias(watchdogConnections.ElementAt(index).Key);
        return _serverOptions.BackendConnections
            .Where(c => c.GetKey() == connectionKey).FirstOrDefault();
    }

    private static string GetBackendConnectionAlias(string key)
    {
        return key.Substring(key.LastIndexOf('-') + 1);
    }
}
