using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Tests.Integration.Fakes;

public partial class FakeBackendServer(int id, int port)
{
    private const int BufferSize = 4096;
    public ConcurrentDictionary<string, ClientHandler> Clients { get; } = new();

    private readonly int _id = id;
    private readonly int _port = port;
    private readonly ManualResetEventSlim closeWait = new(false);
    private bool _isRunning;
    private TcpListener? _listener;

    public void Start()
    {
        // TODO we can do better than this fire & forget
        _ = Task.Run(async () =>
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            while (_isRunning)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();

                    var clientHandler = new ClientHandler(tcpClient, _id);
                    Clients.TryAdd(tcpClient.Client.RemoteEndPoint?.ToString(), clientHandler);
                    Log.Information("[FakeBackend-{id}] Client connected to backend. Count: {count}", _id, Clients.Count);
                    _ = Task.Run(async () => await clientHandler.HandleAsync());
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
            closeWait.Set();
        });
    }

    public void Stop()
    {
        Log.Information("[FakeBackend-{id}] Stopping backend server", _id);
        _isRunning = false;
        _listener?.Stop();

        foreach ((_, var client) in Clients.ToList())
        {
            client.Dispose();
        }
        Clients.Clear();
        closeWait.Wait();
        Log.Information("[FakeBackend-{id}] Stopped backend server", _id);
    }

    public async Task WriteToAllClientsAsync(string message)
    {
        var tasks = Clients.Select(c => c.Value).Where(c => c.IsConnected)
                           .Select(c => c.WriteAsync(message))
                           .ToArray();
        await Task.WhenAll(tasks);
    }
}
