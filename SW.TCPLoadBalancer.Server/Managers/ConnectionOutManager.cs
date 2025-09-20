using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.DTOs;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Helpers;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Managers;

public interface IConnectionOutManager : IConnectionManager, IAsyncDisposable
{
    void AttachClientConnection(INetworkClient connection);
    void DetachClientConnection();
    Task StartConnectionMonitorAsync(ConnectionDetails backendConnectionDetails, bool isWatchDog, string connectionSuffix);
}

public partial class ConnectionOutManager(IServiceProvider serviceProvider,
    IOptions<ServerOptions> serverOptions,
    IConnectionsOutRegistry connectionsManager,
    ILogger<ConnectionInManager> logger) : IConnectionOutManager
{
    private readonly ServerOptions _serverOptions = serverOptions.Value;
    private readonly IConnectionsOutRegistry _connectionsOutRegistry = connectionsManager;
    private readonly ILogger<ConnectionInManager> _logger = logger;
    private readonly CancellationTokenSource _cancelTokenSrc = new();
    private readonly INetworkClient _detachedClientLogger = serviceProvider.GetRequiredService<ILogNetworkSend>();
    private TcpClient? _outClient;
    private INetworkClient? _inClient;
    private readonly AsyncAutoResetEvent _closeWait = new(false);

    public string? ConnectionKey { get; private set; }
    private string GetAlias() => _outClient?.Client.GetSocketKey() ?? "UNKNOWN";

    public async Task StartConnectionMonitorAsync(ConnectionDetails backendConnectionDetails, bool isWatchDog, string connectionSuffix)
    {
        do
        {
            _outClient = await ConnectRetryLoopAsync(backendConnectionDetails, _cancelTokenSrc.Token);
            if (_outClient != null)
            {
                ConnectionKey = $"{connectionSuffix}-{backendConnectionDetails.GetKey()}";
                _logger.LogInformation("[{alias}] Connected to backend", GetAlias());
                _connectionsOutRegistry.AddConnection(ConnectionKey, this);
                await StartReadAsync(_outClient, isWatchDog); // blocks until disconnection or cancellation
                                                              // await _connectionWait.WaitAsync();
                _outClient.DisposeSafely();
                await DisposeClientAsync(_inClient);
                _connectionsOutRegistry.RemoveConnection(ConnectionKey);
            }
        } while (!_cancelTokenSrc.IsCancellationRequested);
        _logger.LogDebug("{alias} Backend connection ending", GetAlias());

    }

    private static async Task DisposeClientAsync(INetworkClient? inClient)
    {
        if (inClient == null) return;
        await inClient.DisposeAsync();
    }

    private async Task StartReadAsync(TcpClient outClient, bool isWatchDog)
    {
        try
        {
            do
            {
                FrameState frameState = new();
                if (_inClient == null)
                {
                    _inClient = _detachedClientLogger;
                    _logger.LogWarning("{alias} No client connection attached to backend connection. Incoming data will be logged only", GetAlias());
                }

                await SocketHelper.ForwardingFramesAsync(frameState, outClient.GetStream(), _inClient);
                if (frameState.SocketError != null)
                {
                    _logger.LogError("{alias} Error sending bytes to client: {socketErr}. Remaining bytes lost {count}",
                        GetAlias(), frameState.SocketError, frameState.ByteCountRemaining);

                    if (!isWatchDog) // watchdog connections persist the lifecycle of the server, whereas connections on the clients behalf clean up after themselves
                    {
                        _logger.LogError("{alias} Backend connection cancelled", GetAlias());
                        _cancelTokenSrc.Cancel();
                    }
                    break; // break out to clean up backend connection
                }
                if (frameState.SourceClosed)
                {
                    _logger.LogError("[{alias}] Backend connection closed, returning", GetAlias());
                    _cancelTokenSrc.Cancel();
                }

            } while (!_cancelTokenSrc.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{alias} Error in read loop for backend connection - {Message}", GetAlias(), ex.Message);
        }
        _logger.LogDebug("{alias} Read loop ended for backend connection", GetAlias());
    }

    private async Task<TcpClient?> ConnectRetryLoopAsync(ConnectionDetails backendConnectionDetails, CancellationToken _cancelToken)
    {
        do
        {
            try
            {
                var outClient = new TcpClient();
                outClient.SetOptions(_serverOptions);
                await outClient.ConnectAsync(backendConnectionDetails.IPAddress!, backendConnectionDetails.Port, _cancelToken);
                return outClient;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to backend {IPAddress}:{Port} - {Message}. Wait {wait}ms ...", backendConnectionDetails.IPAddress, backendConnectionDetails.Port, ex.Message, _serverOptions.BackendReconnectWaitMs);
                await Task.Delay(_serverOptions.BackendReconnectWaitMs, _cancelToken);
            }
        } while (!_cancelTokenSrc.IsCancellationRequested);
        return null;
    }

    public async Task SendAsync(SendState sendState, byte[] buffer, int bytesRead)
    {
        if (_outClient == null || !_outClient.Connected)
        {
            sendState.Exception = new InvalidOperationException("Backend connection not available");
            return;
        }

        var socket = _outClient.Client;
        await SocketHelper.SendAsync(sendState, socket, buffer, bytesRead);
        if (sendState.Exception != null)
        {
            _logger.LogError(sendState.Exception, "{alias} Error sending bytes to backend - {Message}", GetAlias(), sendState.Exception.Message);
            await DisposeAsync(); // Handle own lifecycle. Dispose this connection manager on send error
        }
    }

    public void AttachClientConnection(INetworkClient connection)
    {
        _inClient = connection;
    }

    public void DetachClientConnection()
    {
        _inClient = _detachedClientLogger;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("{alias} Disposing connection out manager", GetAlias());
        _cancelTokenSrc.Cancel();
        _outClient.DisposeSafely();
        await _closeWait.WaitAsync();
        GC.SuppressFinalize(this);
    }
}