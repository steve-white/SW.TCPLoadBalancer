using SW.TCPLoadBalancer.Server.Managers;

namespace SW.TCPLoadBalancer.Server.Registry;

public interface IConnectionsOutRegistry : IConnectionsRegistry<IConnectionOutManager> { }

public class ConnectionsOutRegistry(ILogger<ConnectionsOutRegistry> logger)
    : ConnectionsRegistry<IConnectionOutManager>(logger), IConnectionsOutRegistry
{ }
