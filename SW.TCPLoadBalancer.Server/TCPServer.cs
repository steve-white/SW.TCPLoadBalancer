using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Managers;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;
using System.Net;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server;

public interface ITCPServer
{
    Task StartAsync(CancellationToken cancellationToken);
}

public class TCPServer(ILogger<TCPServer> logger,
    IServiceProvider serviceProvider,
    IConnectionsOutWatchdog connectionsOutManager,
    IConnectionsInRegistry connectionsInRegistry,
    IOptions<ServerOptions> serverOptions) : ITCPServer, IAsyncDisposable
{
    private readonly ILogger<TCPServer> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IConnectionsOutWatchdog _connectionsOutManager = connectionsOutManager;
    private readonly IConnectionsInRegistry _connectionsInRegistry = connectionsInRegistry;
    private readonly ServerOptions _serverOptions = serverOptions.Value;

    private TcpListener? _listener;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _connectionsOutManager.StartWatchdogAsync(); // start async connection watchdog to backend servers
        _listener = StartListener(); // start listening for incoming connections
        await AcceptLoopAsync(_listener, cancellationToken); // start accepting incoming connections
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
        _ = (IPEndPoint)listener.LocalEndpoint;
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                TcpClient? tcpClient = await listener.AcceptTcpClientAsync(stopToken);
                tcpClient.SetOptions(_serverOptions);

                var inClientHandler = _serviceProvider.GetRequiredService<ConnectionInManager>();
                _ = inClientHandler.StartConnectionTasksAsync(tcpClient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Problem accepting connection");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _listener?.Stop();
            await CloseConnectionsInAsync();
            await _connectionsOutManager.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping the listener.");
        }
        GC.SuppressFinalize(this);
    }

    public async Task CloseConnectionsInAsync()
    {
        foreach ((_, var connection) in _connectionsInRegistry.ActiveConnections)
        {
            await connection.DisposeAsync();
        }
    }
}
