using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Server.Registry;
using SW.TCPLoadBalancer.Tests.Integration.Fakes;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Tests.Integration;

public class ServerTests : IDisposable
{
    private const int TestPort = 3400;
    private readonly IHost _sut;
    private readonly Dictionary<int, FakeBackendServer> backends = new();
    private readonly Dictionary<int, TCPClient> clients = new();
    private IConnectionsInRegistry? _connectionsInRegistry;
    private IConnectionsOutRegistry? _connectionsOutRegistry;

    public ServerTests()
    {
        _sut = CreateTestServerAsync();
    }

    [Fact]
    public async Task Start_WhenValidBackendConnections_ClientsShouldConnectInRoundRobin_AndBackendsShouldReceiveData()
    {
        // Arrange
        await _sut.StartAsync();
        _connectionsInRegistry = _sut.Services.GetRequiredService<IConnectionsInRegistry>();
        _connectionsOutRegistry = _sut.Services.GetRequiredService<IConnectionsOutRegistry>();

        // start backend listeners
        backends.Add(1, CreateStartBackend(1, 3401));
        backends.Add(2, CreateStartBackend(2, 3402));
        backends.Add(3, CreateStartBackend(3, 3403));

        clients.Add(1, CreateClient());
        clients.Add(2, CreateClient());
        clients.Add(3, CreateClient());

        // Act/Assert

        _connectionsInRegistry.ActiveConnections.Should().BeEmpty();
        // wait for backend watchdog connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _connectionsOutRegistry.ActiveConnections.Count == 3);
        var watchdogBackendConnections = backends.SelectMany(x => x.Value.Clients).ToList();

        // connect clients
        await ConnectClientsAsync(clients);

        // wait for clients to be registered to the backend
        await WaitForMatchAsync("ConnectionsIn ActiveConnections", () => _connectionsInRegistry.ActiveConnections.Count == 3);

        // wait for backend client connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _connectionsOutRegistry.ActiveConnections.Count == 6);
        var clientBackendConnections = backends.SelectMany(x => x.Value.Clients).Except(watchdogBackendConnections).Select(x => x.Value).ToList();

        // fake backends should have 6 clients connected in total (3 watchdog + 3 clients)
        await WaitForMatchAsync("Fake Backend connections", () => backends.Values.SelectMany(x => x.Clients).Count() == 6);

        // send data from clients to backends and assert
        await clients[1].SendMessageAsync("Hello from client 1");
        await WaitForMatchAsync("Backend 1 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 1") != null);
        await WaitForMatchAsync("Client 1 backend response", () => clients[1].GetMessage() == ("Hello from client 1 - response"));

        await clients[2].SendMessageAsync("Hello from client 2");
        await WaitForMatchAsync("Backend 2 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 2") != null);
        await WaitForMatchAsync("Client 2 backend response", () => clients[2].GetMessage() == ("Hello from client 2 - response"));

        await clients[3].SendMessageAsync("Hello from client 3");
        await WaitForMatchAsync("Backend 3 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 3") != null);
        await WaitForMatchAsync("Client 3 backend response", () => clients[3].GetMessage() == ("Hello from client 3 - response"));
    }


    [Fact]
    public async Task Start_WhenBackendConnectionDrops_ClientConnectionShouldDrop_NewClientsShouldConnectToNextAvailableBackend()
    {
        // Arrange
        await _sut.StartAsync();
        _connectionsInRegistry = _sut.Services.GetRequiredService<IConnectionsInRegistry>();
        _connectionsOutRegistry = _sut.Services.GetRequiredService<IConnectionsOutRegistry>();

        // start backend listeners
        backends.Add(1, CreateStartBackend(1, 3401));
        backends.Add(2, CreateStartBackend(2, 3402));
        backends.Add(3, CreateStartBackend(3, 3403));

        clients.Add(1, CreateClient());
        clients.Add(2, CreateClient());
        clients.Add(3, CreateClient());

        // Act/Assert
        _connectionsInRegistry.ActiveConnections.Should().BeEmpty();
        // wait for backend watchdog connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _connectionsOutRegistry.ActiveConnections.Count == 3);
        var watchdogBackendConnections = backends.SelectMany(x => x.Value.Clients).ToList();

        // connect clients
        await ConnectClientsAsync(clients);

        // fake backends should have 6 clients connected in total (3 watchdog + 3 clients)
        await WaitForMatchAsync("Fake Backend connections", () => backends.Values.SelectMany(x => x.Clients).Count() == 6);
        var clientBackendConnections = backends.SelectMany(x => x.Value.Clients).Except(watchdogBackendConnections).Select(x => x.Value).ToList();

        // send data from client 1 to backend and assert
        await clients[1].SendMessageAsync("Hello 1 from client 1");

        FakeBackendServer.ClientHandler? firstBackend = null;
        await WaitForMatchAsync("Hello 1 from client 1", () => (firstBackend = GetBackendReceivedData(clientBackendConnections, "Hello 1 from client 1")) != null);
        await WaitForMatchAsync("Hello 1 from client 1 - response", () => clients[1].GetMessage() == ("Hello 1 from client 1 - response"));
        firstBackend.Should().NotBeNull();

        // drop backend connection for client 1
        firstBackend.Dispose();
        backends.Remove(firstBackend.Id);

        await SendClientMessageConsumeErrorAsync(clients[1], "Failed message for client 1");
        await WaitForMatchAsync("Client 1 close", () => !clients[1].Client.Connected);

        // create new client connection
        clients.Add(4, CreateClient());
        await ConnectStartClientAsync(clients[4]);
        await clients[4].SendMessageAsync("Hello 1 from client 4");

        // ensure client 4 connects to a different, active, backend and gets a response
        await WaitForMatchAsync("Hello 1 from client 4 - response", () => clients[4].GetMessage() == ("Hello 1 from client 4 - response"));
    }

    #region Helpers

    private static async Task SendClientMessageConsumeErrorAsync(TCPClient client, string txt)
    {
        try
        {
            await client.SendMessageAsync(txt);
        }
        catch (Exception) { }
    }

    private static FakeBackendServer.ClientHandler? GetBackendReceivedData(List<FakeBackendServer.ClientHandler> clientBackendConnections, string txtExpected)
    {
        foreach (var backend in clientBackendConnections)
        {
            var txt = backend.GetReceivedText();
            if (txt.Contains(txtExpected))
                return (backend);
            // TODO assert other backends have explicitly not received this data
        }
        return null;
    }
    private static async Task WaitForMatchAsync(string alias, Func<bool> expr, int waitTimeoutMs = 2000)
    {
        var endTime = DateTime.UtcNow.AddMilliseconds(waitTimeoutMs);
        while (DateTime.UtcNow < endTime)
        {
            if (expr.Invoke()) return;
            await Task.Delay(500);
        }
        throw new Exception($"'{alias}' condition not met within timeout"); // TODO return a better exception type
    }


    private static async Task ConnectClientsAsync(Dictionary<int, TCPClient> clients)
    {
        foreach (var client in clients.Values)
        {
            await ConnectStartClientAsync(client);
        }
    }

    private static async Task ConnectStartClientAsync(TCPClient client)
    {
        await client.Client.ConnectAsync("127.0.0.1", TestPort);
        Log.Debug("Test client connected to {port}", TestPort);
        client.StartRead();
    }

    private TCPClient CreateClient()
    {
        var client = new TcpClient();
        client.SetOptions(new()
        {
            ReceiveTimeoutMs = 5000,
            SendTimeoutMs = 5000,
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192
        });
        return new TCPClient(_sut.Services.GetRequiredService<ILogger<TCPClient>>(), client);
    }

    public static FakeBackendServer CreateStartBackend(int id, int port)
    {
        var backend = new FakeBackendServer(id, port);
        backend.Start();
        return backend;
    }

    public static IHost CreateTestServerAsync()
    {
        var builder = Host.CreateDefaultBuilder()
             .ConfigureAppConfiguration((context, config) =>
             {
                 config.AddJsonFile("testappsettings.json", optional: false, reloadOnChange: false);
             })
             .UseSerilog((context, configuration) =>
             {
                 configuration.ReadFrom.Configuration(context.Configuration);
             })
            .ConfigureServices((context, services) =>
            {
                services.AddBusinessServices(context);
            });

        var host = builder.Build();
        return host;
    }

    public void Dispose()
    {
        // TODO dispose backends, clients, IHost

        GC.SuppressFinalize(this);
    }

    #endregion Helpers
}
