using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Tests.Integration.Fakes;

public class FakeBackendServer(int id, int port)
{
    public ConcurrentDictionary<string, ClientHandler> Clients { get; } = new();

    private readonly int _id = id;
    private readonly int _port = port;
    private readonly ManualResetEventSlim closeWait = new(false);
    private bool _isRunning;
    private TcpListener? _listener;
    private readonly ConcurrentBag<Task> clientTasks = new();
    private Task? _startTask;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FakeBackend-{id}] Error starting backend server on port {port}: {Message}", _id, _port, ex.Message);
            throw;
        }

        _startTask = Task.Run(async () =>
        {
            _isRunning = true;

            while (_isRunning)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    if (!_isRunning) break;

                    var clientKey = $"{tcpClient.Client.RemoteEndPoint?.ToString() ?? "UNKNOWN"}-{tcpClient.Client.LocalEndPoint?.ToString() ?? "UNKNOWN"}";
                    var clientHandler = new ClientHandler(tcpClient, _id, clientKey);
                    if (!Clients.TryAdd(clientKey, clientHandler))
                    {
                        Log.Warning("[FakeBackend-{id}-{ip}] Could not add client to dictionary", _id, clientKey);
                        tcpClient.Close();
                        continue;
                    }
                    Log.Information("[FakeBackend-{id}-{ip}] Client connected to backend. Count: {count}", _id, clientKey, Clients.Count);
                    clientTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await clientHandler.HandleAsync();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Error in client read loop: {msg}", ex.Message);
                        }
                        finally
                        {
                            if (!Clients.TryRemove(clientKey, out _))
                            {
                                Log.Warning("[FakeBackend-{id}-{ip}] Could not remove client from dictionary", _id, clientKey);
                            }

                            Log.Information("[FakeBackend-{id}-{ip}] Client disconnected from backend. Count: {count}", _id, clientKey, Clients.Count);
                        }
                    }));
                }
                catch (Exception)
                {
                    Log.Information("[FakeBackend-{id}] Listener break...", _id);
                    break;
                }
            }

            foreach ((_, var client) in Clients.ToList())
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex) { Log.Error(ex, "Error closing backend client"); }
            }
            await Task.WhenAll(clientTasks.ToArray());

            closeWait.Set();
        });
    }

    public void Stop()
    {
        Log.Information("[FakeBackend-{id}] Stopping backend server", _id);
        try
        {
            _isRunning = false;
            try
            {
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FakeBackend-{id}] Error stopping listener: {Message}", _id, ex.Message);
            }
            _listener?.Dispose();
            _startTask?.Wait();
            closeWait.Wait();
            Clients.Clear();
            Log.Information("[FakeBackend-{id}] Stopped backend server", _id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FakeBackend-{id}] Error stopping backend server: {Message}", _id, ex.Message);
        }
    }

    public async Task WriteToAllClientsAsync(string message)
    {
        var tasks = Clients.Select(c => c.Value).Where(c => c.IsConnected)
                           .Select(c => c.WriteAsync(message))
                           .ToArray();
        await Task.WhenAll(tasks);
    }
}
