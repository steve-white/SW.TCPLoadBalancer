using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Managers;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime;

namespace SW.TCPLoadBalancer.Server;

public interface ITCPServer
{
    Task StartAsync(CancellationToken cancellationToken);
}

public class TCPServer(ILogger<TCPServer> logger,
    IServiceProvider serviceProvider,
    IConnectionsOutWatchdog connectionsOutManager,
    IConnectionsInRegistry connectionsInRegistry,
    IConnectionsOutRegistry connectionsOutRegistry,
    IOptions<ServerOptions> serverOptions) : ITCPServer, IAsyncDisposable
{
    private readonly ILogger<TCPServer> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IConnectionsOutWatchdog _connectionsOutWatchDog = connectionsOutManager;
    private readonly IConnectionsInRegistry _connectionsInRegistry = connectionsInRegistry;
    private readonly IConnectionsOutRegistry _connectionsOutRegistry = connectionsOutRegistry;
    private readonly ServerOptions _serverOptions = serverOptions.Value;

    private TcpListener? _listener;
    private Task? _acceptTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private IServiceScope? _serverScope;
    private readonly ConcurrentBag<Task> _inClientTasks = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Server GC: " + GCSettings.IsServerGC);
        _logger.LogDebug("Latency Mode: " + GCSettings.LatencyMode);

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _serverScope = _serviceProvider.CreateAsyncScope();

        _connectionsOutWatchDog.StartWatchdog(); // start async connection watchdog to backend servers
        _listener = StartListener(); // start listening for incoming connections
        _acceptTask = AcceptLoopAsync(_listener, _cancellationTokenSource.Token); // start accepting incoming connections
        return _acceptTask;
    }

    private TcpListener StartListener()
    {
        var listenAddress = ParseIpAddress(_serverOptions.ListenInterface);

        _listener = new TcpListener(listenAddress, _serverOptions.ListenPort);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: _serverOptions.ConnectionBacklog);
        _logger.LogInformation("Server started on {Interface}:{Port}", _serverOptions.ListenInterface, _serverOptions.ListenPort);

        return _listener;
    }

    private static IPAddress ParseIpAddress(string listenInterface)
    {
        if (!IPAddress.TryParse(listenInterface, out var ipAddress))
        {
            throw new ArgumentException($"Invalid ListenInterface: {listenInterface}");
        }
        return ipAddress;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                TcpClient? tcpClient = await listener.AcceptTcpClientAsync(stopToken);
                tcpClient.SetOptions(_serverOptions);
                var inClientHandler = _serverScope!.ServiceProvider.GetRequiredService<IConnectionInManager>();
                _inClientTasks.Add(inClientHandler.StartConnectionTasksAsync(_serverScope, tcpClient)); // handler manages its own lifecycle
            }
            catch (Exception ex)
            {
                if (!stopToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, $"Problem accepting connection");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_cancellationTokenSource != null
                && !_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.CancelDispose();
            _listener?.Stop();

            await _acceptTask.WaitForCompletionAsync();
            await _connectionsOutWatchDog.DisposeAsync();

            CloseConnectionsIn();
            CloseConnectionsOut();
            await _inClientTasks.WhenAll();
            _inClientTasks.Clear();
            _serverScope?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping the listener.");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
        }
    }

    private void CloseConnectionsOut()
    {
        foreach ((_, var connection) in _connectionsOutRegistry.ActiveConnections)
        {
            connection.Dispose();
        }
        _connectionsOutRegistry.Clear();
    }

    private void CloseConnectionsIn()
    {
        foreach ((_, var connection) in _connectionsInRegistry.ActiveConnections)
        {
            connection.Dispose();
        }
        _connectionsInRegistry.Clear();
    }
}
