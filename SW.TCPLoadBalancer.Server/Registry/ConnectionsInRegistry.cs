using SW.TCPLoadBalancer.Server.Managers;

namespace SW.TCPLoadBalancer.Server.Registry;

public interface IConnectionsInRegistry : IConnectionsRegistry<IConnectionInManager> { }

public class ConnectionsInRegistry(ILogger<ConnectionsInRegistry> logger)
    : ConnectionsRegistry<IConnectionInManager>(logger), IConnectionsInRegistry
{ }
