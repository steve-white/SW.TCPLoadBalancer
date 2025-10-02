using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SW.TCPLoadBalancer.Server.Registry;
using SW.TCPLoadBalancer.Tests.Integration.Fakes;
using System.Collections.Concurrent;

namespace SW.TCPLoadBalancer.Tests.Integration;

[CollectionDefinition("ServerCollection")]
public class ServerCollection : ICollectionFixture<ServerFixture> { }

public class ServerFixture : IDisposable
{
    public const int TestPort = 3400;
    public IHost? Sut { get; set; }
    public ConcurrentDictionary<int, FakeBackendServer> Backends { get; } = new();
    public ConcurrentDictionary<int, TCPClient> TestClients { get; } = new();

    public IConnectionsInRegistry? ConnectionsInRegistry { get; private set; }
    public IConnectionsOutRegistry? ConnectionsOutRegistry { get; private set; }
    public bool TestRunning { get; set; }

    public async Task StartTestHostAsync()
    {
        await Sut.StartAsync();
        ConnectionsInRegistry = Sut.Services.GetRequiredService<IConnectionsInRegistry>();
        ConnectionsOutRegistry = Sut.Services.GetRequiredService<IConnectionsOutRegistry>();
    }

    public void Dispose()
    {
        CloseBackends();
        CloseTestClients();
        CloseSut();
    }

    internal void CloseBackends()
    {
        Log.Debug("TEST Close {count} backends", Backends.Count);
        foreach ((_, var backend) in Backends)
        {
            try
            {
                backend.Stop();
            }
            catch (Exception ex) { Log.Error(ex, "Error closing backend"); }
        }
        Backends.Clear();
    }

    internal void CloseTestClients()
    {
        foreach ((_, var client) in TestClients)
        {
            try
            {
                client.Stop();
            }
            catch (Exception) { }
        }
        TestClients.Clear();
    }

    internal void CloseSut()
    {
        Sut?.StopAsync().GetAwaiter().GetResult();
        Sut?.Dispose();
    }
}
