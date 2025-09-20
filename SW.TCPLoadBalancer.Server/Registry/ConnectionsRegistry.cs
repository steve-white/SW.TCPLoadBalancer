using SW.TCPLoadBalancer.Server.Abstractions;
using System.Collections.Concurrent;

namespace SW.TCPLoadBalancer.Server.Registry;

public interface IConnectionsRegistry<TManager>
{
    ConcurrentDictionary<string, TManager> ActiveConnections { get; }
    void AddConnection(string key, TManager value);
    void RemoveConnection(string key);
    void Clear();
}

public class ConnectionsRegistry<TManager>(ILogger<ConnectionsRegistry<TManager>> logger)
    : IConnectionsRegistry<TManager> where TManager : IConnectionManager
{
    public ConcurrentDictionary<string, TManager> ActiveConnections { get; } = new();
    private readonly ILogger<ConnectionsRegistry<TManager>> _logger = logger;

    public void AddConnection(string key, TManager value)
    {
        if (!ActiveConnections.TryAdd(key, value))
            throw new ArgumentException($"Connection with key {key} already exists");

        // count is for debug, may be out of date at this point
        _logger.LogInformation("[{Key}] Connection added. Count: {count}", key, ActiveConnections.Count);
    }

    public void RemoveConnection(string key)
    {
        if (ActiveConnections.TryRemove(key, out _))
        {
            _logger.LogInformation("[{key}] Connection removed. Count: {count}", key, ActiveConnections.Count);// count is for debug
        }
        else
        {
            _logger.LogWarning("[{Key}] Attempted to remove non-existent connection", key);
        }
    }

    public void Clear()
    {
        ActiveConnections.Clear();
    }
}
