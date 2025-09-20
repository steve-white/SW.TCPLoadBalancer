using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface IConnectionsOutWatchdog : IAsyncDisposable
{
    Task StartWatchdogAsync();
}

public class ConnectionsOutWatchdog(IServiceProvider serviceProvider,
    IOptions<ServerOptions> serverOptions,
    IConnectionsOutRegistry connectionsOutRegistry,
    ILogger<ConnectionsOutWatchdog> logger) : IConnectionsOutWatchdog
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ServerOptions _serverOptions = serverOptions.Value;
    private readonly IConnectionsOutRegistry _connectionsOutRegistry = connectionsOutRegistry;
    private readonly ILogger<ConnectionsOutWatchdog> _logger = logger;

    public async ValueTask DisposeAsync()
    {
        foreach ((_, var handler) in _connectionsOutRegistry.ActiveConnections)
        {
            await handler.DisposeAsync();
        }
        _connectionsOutRegistry.Clear();
        GC.SuppressFinalize(this);
    }

    public Task StartWatchdogAsync()
    {
        _logger.LogInformation("Starting {count} backend connection managers", _serverOptions.BackendConnections.Count);

        foreach (var backendConnectionDetails in _serverOptions.BackendConnections)
        {
            var outClientHandler = _serviceProvider.GetRequiredService<ConnectionOutManager>();
            _ = outClientHandler.StartConnectionMonitorAsync(backendConnectionDetails, isWatchDog: true, connectionSuffix: "watchdog");
        }
        return Task.CompletedTask;
    }
}
