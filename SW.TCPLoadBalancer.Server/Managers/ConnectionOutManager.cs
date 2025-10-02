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

public interface IConnectionOutManager : IConnectionManager, IDisposable
{
    void AttachClientConnection(IConnectionInManager connection);
    Task StartConnectionMonitorAsync();
    void Initialise(ConnectionDetails backendConnectionDetails, bool isWatchDog, string connectionSuffix);
    void DetachClientConnection();
}

public class ConnectionOutManager(ILogNetworkSend detachedClientLogger,
    IOptions<ServerOptions> serverOptions,
    IConnectionsOutRegistry connectionsManager,
    ILogger<ConnectionInManager> logger) : IConnectionOutManager
{
    private readonly ServerOptions _serverOptions = serverOptions.Value;
    private readonly IConnectionsOutRegistry _connectionsOutRegistry = connectionsManager;
    private readonly ILogger<ConnectionInManager> _logger = logger;
    private readonly CancellationTokenSource _cancelTokenSrc = new();
    private readonly INetworkClient _detachedClientLogger = detachedClientLogger;
    private readonly AsyncManualResetEvent _closeWait = new(false);
    private readonly AsyncManualResetEvent _outClientInitWait = new(false);

    private ConnectionDetails? _backendConnectionDetails;
    private bool _isWatchDog;
    private TcpClient? _outClient;
    private INetworkClient? _inClient;
    private string? _alias;
    private int _disposed = 0;
    private string? _connectionSuffix;

    public string? ConnectionKey { get; private set; }

    private string Alias
    {
        get
        {
            if (_alias != null) return _alias;
            _alias = $"{_outClient?.Client.GetLocalSocketKey()}-{_outClient?.Client.GetRemoteSocketKey()}";
            return _alias ?? "UNKNOWN";
        }
    }

    public void Initialise(ConnectionDetails backendConnectionDetails, bool isWatchDog, string connectionSuffix)
    {
        _backendConnectionDetails = backendConnectionDetails;
        _isWatchDog = isWatchDog;
        _connectionSuffix = connectionSuffix;
    }

    public async Task StartConnectionMonitorAsync()
    {
        try
        {
            do
            {
                if (_backendConnectionDetails == null) throw new InvalidOperationException("ConnectionOutManager not initialised");

                _outClient = await ConnectRetryLoopAsync(_backendConnectionDetails, _cancelTokenSrc.Token);
                if (_outClient != null)
                {
                    _outClientInitWait.Set(); // Notify any waiting SendAsync calls that the outClient is ready

                    ConnectionKey = $"{_connectionSuffix}-{_outClient.Client.GetLocalSocketKey()}-{_backendConnectionDetails.GetKey()}";
                    _connectionsOutRegistry.AddConnection(ConnectionKey, this);
                    _logger.LogInformation("[{alias}] Watchdog connected to backend", Alias);

                    await StartReadAsync(_outClient, _isWatchDog); // blocks until disconnection or cancellation

                    _outClient.CloseSafely();
                    DisposeInClient(_inClient);
                    _connectionsOutRegistry.RemoveConnection(ConnectionKey);
                }
            } while (!_cancelTokenSrc.IsCancellationRequested);
            _logger.LogDebug("{alias} Watchdog backend connection ending", Alias);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{alias} Error in connection monitor loop - {Message}", Alias, ex.Message);
        }
        finally
        {
            _closeWait.Set();
        }
    }

    private static void DisposeInClient(INetworkClient? inClient)
    {
        if (inClient == null) return;
        inClient.Dispose();
    }

    private async Task StartReadAsync(TcpClient outClient, bool isWatchDog) // TODO decouple TcpClient
    {
        try
        {
            do
            {
                FrameState frameState = new();
                if (_inClient == null)
                {
                    _inClient = _detachedClientLogger;
                    _logger.LogWarning("{alias} No client connection attached to backend connection. Incoming data will be logged only", Alias);
                }

                await SocketHelper.ForwardBufferedDataAsync(frameState, outClient.GetStream(), _inClient);
                if (frameState.SocketError != null)
                {
                    _logger.LogError("{alias} Error sending bytes to client: {socketErr}. Remaining bytes lost {count}",
                        Alias, frameState.SocketError, frameState.ByteCountRemaining);

                    if (!isWatchDog) // watchdog connections persist the lifecycle of the server, whereas connections on the clients behalf clean up after themselves
                    {
                        _logger.LogError("{alias} Backend connection cancelled", Alias);
                        _cancelTokenSrc.CancelDispose();
                    }
                    break; // break out to clean up backend connection
                }
                if (frameState.SourceClosed)
                {
                    _logger.LogError("[{alias}] Backend connection closed, returning", Alias);
                    _cancelTokenSrc.CancelDispose();
                }

            } while (!_cancelTokenSrc.IsCancellationRequested);
            _logger.LogDebug("{alias} Read loop ended for backend connection", Alias);
        }
        catch (Exception ex)
        {
            _logger.LogError("{alias} Error in read loop for backend connection - {Message}", Alias, ex.Message);
            if (!isWatchDog)
            {
                _cancelTokenSrc.CancelDispose();
            }
        }

    }
    private async Task<TcpClient?> ConnectRetryLoopAsync(ConnectionDetails backendConnectionDetails, CancellationToken _cancelToken)
    {
        do
        {
            try
            {
                var outClient = new TcpClient(); // TODO decouple TcpClient creation via factory for testing
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
        await _outClientInitWait.WaitAsync(_cancelTokenSrc.Token);
        if (_outClient == null || !_outClient.Connected)
        {
            sendState.Exception = new InvalidOperationException("Backend connection not available");
            return;
        }

        var socket = _outClient.Client;
        await SocketHelper.SendAsync(sendState, socket, buffer, bytesRead);
        if (sendState.Exception != null)
        {
            _logger.LogError(sendState.Exception, "{alias} Error sending bytes to backend - {Message}", Alias, sendState.Exception.Message);
            Dispose(); // Handle own lifecycle. Dispose this connection manager on send error
        }
    }

    public void AttachClientConnection(IConnectionInManager connection)
    {
        _logger.LogDebug("[{alias}-{hc}] Attach client connection to backend. Client: {key}", _backendConnectionDetails!.GetKey(), this.GetHashCode(), connection.ConnectionKey);
        _inClient = connection;
    }

    public void Dispose()
    {
        //  thread-safe check-and-set
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1) return;

        _logger.LogDebug("[{alias}] {hc} Disposing connection out manager", this.GetHashCode(), _backendConnectionDetails!.GetKey());
        _cancelTokenSrc.CancelDispose();
        _outClientInitWait.Set();

        _outClient.CloseSafely();
        _closeWait.Wait(); // TODO use Async version
        _logger.LogDebug("[{alias}] {hc} Disposed connection out manager", this.GetHashCode(), _backendConnectionDetails!.GetKey());
    }

    public void DetachClientConnection()
    {
        _inClient = null;
        _outClientInitWait.Reset();
    }
}