using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.DTOs;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Helpers;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface IConnectionInManager : IConnectionManager { }

public class ConnectionInManager(IConnectionsInRegistry connectionsInRegistry,
    IConnectionsOutWatchdog connectionsOutManager,
    IConnectionOutSelector connectionOutSelector,
    IServiceProvider serviceProvider,
    ILogger<ConnectionInManager> logger) : IConnectionInManager
{
    private readonly IConnectionsInRegistry _connectionsInRegistry = connectionsInRegistry;
    private readonly IConnectionsOutWatchdog _connectionssOutManager = connectionsOutManager;
    private readonly IConnectionOutSelector _connectionOutSelector = connectionOutSelector;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<ConnectionInManager> _logger = logger;
    private TcpClient? _tcpClient;
    private string? _remoteEndpoint;
    private readonly CancellationTokenSource _cancelTokenSrc = new();
    private readonly AsyncAutoResetEvent _closeWait = new(false);

    public string ConnectionKey => _remoteEndpoint!;

    public async Task StartConnectionTasksAsync(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        try
        {
            _remoteEndpoint = tcpClient.Client.GetSocketKey(); // can throw disposed
            _logger.LogDebug("[{alias}] Incoming client connection", _remoteEndpoint);
            _connectionsInRegistry.AddConnection(_remoteEndpoint, this);

            await StartReadAsync(tcpClient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Problem processing connection");
        }
        finally
        {
            _closeWait.Set();
            await DisposeAsync();
        }
    }


    public ConnectionOutManager CreateConnectionOutManager(ConnectionDetails backendConnectionDetails, string connectionSuffix)
    {
        var outClientHandler = _serviceProvider.GetRequiredService<ConnectionOutManager>();
        _ = outClientHandler.StartConnectionMonitorAsync(backendConnectionDetails, isWatchDog: false, connectionSuffix); // handler manages its own lifecycle
        return outClientHandler;
    }


    private async Task StartReadAsync(TcpClient tcpClient)
    {
        try
        {
            FrameState frameState = new();
            do
            {
                // TODO we should probably use connection pooling & management here instead of per client backend connection.
                // This would be somewhat easier implement do if we had a known framing protocol
                var backendConnectionDetails = _connectionOutSelector.GetAvailableBackendConnectionDetails();
                if (backendConnectionDetails != null)
                {
                    var connectionSuffix = tcpClient.Client.GetSocketKey();
                    var outConnection = CreateConnectionOutManager(backendConnectionDetails, connectionSuffix);
                    outConnection.AttachClientConnection(this);
                    using var stream = tcpClient.GetStream();

                    await SocketHelper.ForwardingFramesAsync(frameState, stream, outConnection);

                    if (frameState.SocketError != null)
                    {
                        _logger.LogError("[{alias}] Error sending bytes '{socketErr}'. Remaining bytes lost {count}",
                            _remoteEndpoint, frameState.SocketError, frameState.ByteCountRemaining);
                        // TODO: try to resend remaining bytes from frameState on next available connection.
                        // Run out of time for this now.

                        // Create a new connection to backend on next loop
                    }
                    if (frameState.SourceClosed)
                    {
                        _logger.LogError("[{alias}] Client connection closed, returning", _remoteEndpoint);
                        _cancelTokenSrc.Cancel();
                    }
                }
                else
                {
                    _logger.LogWarning("[{alias}] No backend connection(s) available, dropping incoming connection", _remoteEndpoint);
                    break;
                }
            } while (!_cancelTokenSrc.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{alias}] Error in read loop for client connection - {Message}", _remoteEndpoint, ex.Message);
        }
        _logger.LogDebug("[{alias}] Read loop ended for client", _remoteEndpoint);
    }

    public async ValueTask DisposeAsync()
    {
        _cancelTokenSrc.Cancel();
        try
        {
            _tcpClient?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing client connection: {Message}", ex.Message);
        }
        if (_remoteEndpoint != null)
        {
            _connectionsInRegistry.RemoveConnection(_remoteEndpoint);
        }
        await _closeWait.WaitAsync();
        GC.SuppressFinalize(this);
    }

    public async Task SendAsync(SendState sendState, byte[] buffer, int bytesRead)
    {
        if (_tcpClient == null) return;
        if (!_tcpClient.Connected) return;

        var socket = _tcpClient.Client;

        await SocketHelper.SendAsync(sendState, socket, buffer, bytesRead);
        if (sendState.Exception != null)
        {
            _logger.LogError(sendState.Exception, "{alias} Error sending bytes to client: {Message}. Remaining bytes lost {count}",
                _remoteEndpoint, sendState.Exception.Message, bytesRead - sendState.BytesSent);
            await DisposeAsync(); // close connection on error
        }
    }
}
