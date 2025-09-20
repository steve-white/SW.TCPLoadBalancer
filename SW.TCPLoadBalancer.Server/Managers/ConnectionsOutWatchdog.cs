using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Options;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface IConnectionsOutWatchdog : IAsyncDisposable
{
    void StartWatchdog();
}

public class ConnectionsOutWatchdog(IServiceProvider serviceProvider,
    IOptions<ServerOptions> serverOptions,
    ILogger<ConnectionsOutWatchdog> logger) : IConnectionsOutWatchdog
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ServerOptions _serverOptions = serverOptions.Value;
    private readonly ILogger<ConnectionsOutWatchdog> _logger = logger;
    private IServiceScope? _serviceScope;
    private readonly List<IServiceScope> _handlerScopes = new();
    private readonly List<Task> _runningTasks = new();
    private int _disposed = 0;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1) return;

        foreach (var handlerScope in _handlerScopes)
        {
            handlerScope?.Dispose();
        }
        _handlerScopes.Clear();

        _logger.LogDebug("Watchdog: wait for all tasks to complete");
        await Task.WhenAll(_runningTasks);
        _logger.LogDebug("Watchdog: all tasks complete");

        _serviceScope?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void StartWatchdog()
    {
        _logger.LogInformation("Starting {count} backend watchdogs", _serverOptions.BackendConnections.Count);
        _serviceScope = _serviceProvider.CreateScope();
        foreach (var backendConnectionDetails in _serverOptions.BackendConnections)
        {
            var handlerScope = _serviceScope.ServiceProvider.CreateScope();
            _handlerScopes.Add(handlerScope);
            var outWatchDogHandler = handlerScope.ServiceProvider.GetRequiredService<IConnectionOutManager>();
            outWatchDogHandler.Initialise(backendConnectionDetails, isWatchDog: true, connectionSuffix: "watchdog");

            // handles its own life cycle
            _runningTasks.Add(outWatchDogHandler.StartConnectionMonitorAsync());
        }
    }
}
