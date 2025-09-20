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
        // TODO improve this. Don't rely on connectionKey prefix & substring to identify watchdog connections
        var watchdogConnections = _connectionsOutRegistry.ActiveConnections
            .Where(x => x.Value.ConnectionKey!.StartsWith("watchdog-")).ToList();
        if (_connectionsOutRegistry.ActiveConnections.IsEmpty)
            return null;

        var index = Environment.TickCount % watchdogConnections.Count;
        var connectionKey = watchdogConnections.ElementAt(index).Key.Substring(9);
        return _serverOptions.BackendConnections
            .Where(c => c.GetKey() == connectionKey).FirstOrDefault();
    }
}
