using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SW.TCPLoadBalancer.Server.Extensions;
using SW.TCPLoadBalancer.Tests.Integration.Fakes;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Tests.Integration;

// TODO tests need a lot of work. Smaller units, modularise, reduce code duplication, etc...
[Collection("ServerCollection")]
public class ServerTests : IDisposable
{
    private readonly ServerFixture _fixture;

    public ServerTests(ServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.TestRunning = true;

        _fixture.Sut = CreateTestServerAsync();
    }

    [Fact]
    public async Task Start_GivenMultipleBackendConnections_ClientsShouldConnectToRandomBackends_AndBackendsShouldReceiveDataAndRespondToClient()
    {
        Log.Information("--------------Starting test: {name} --------------", nameof(Start_GivenMultipleBackendConnections_ClientsShouldConnectToRandomBackends_AndBackendsShouldReceiveDataAndRespondToClient));
        // Arrange
        await _fixture.StartTestHostAsync();

        CreateBackends(3);
        CreateClients(3);

        // Act/Assert
        _fixture.ConnectionsInRegistry.ActiveConnections.Should().BeEmpty();
        // wait for backend watchdog connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _fixture.ConnectionsOutRegistry.ActiveConnections.Count == 3);
        var watchdogBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).ToList();

        // connect clients
        await ConnectClientsAsync(_fixture.TestClients);

        // wait for clients to be registered to the backend
        await WaitForMatchAsync("ConnectionsIn ActiveConnections", () => _fixture.ConnectionsInRegistry.ActiveConnections.Count == 3);

        // wait for backend client connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _fixture.ConnectionsOutRegistry.ActiveConnections.Count == 6);
        var clientBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).Except(watchdogBackendConnections).Select(x => x.Value).ToList();

        // fake backends should have 6 clients connected in total (3 watchdog + 3 clients)
        await WaitForMatchAsync("Fake Backend connections", () => _fixture.Backends.Values.SelectMany(x => x.Clients).Count() == 6);

        // send data from clients to backends and assert
        await _fixture.TestClients[1].SendMessageAsync("Hello from client 1");
        await WaitForMatchAsync("Backend 1 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 1") != null);
        await WaitForMatchAsync("Client 1 backend response", () => _fixture.TestClients[1].GetMessage() == ("Hello from client 1 - response"));

        await _fixture.TestClients[2].SendMessageAsync("Hello from client 2");
        await WaitForMatchAsync("Backend 2 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 2") != null);
        await WaitForMatchAsync("Client 2 backend response", () => _fixture.TestClients[2].GetMessage() == ("Hello from client 2 - response"));

        await _fixture.TestClients[3].SendMessageAsync("Hello from client 3");
        await WaitForMatchAsync("Backend 3 client hello", () => GetBackendReceivedData(clientBackendConnections, "Hello from client 3") != null);
        await WaitForMatchAsync("Client 3 backend response", () => _fixture.TestClients[3].GetMessage() == ("Hello from client 3 - response"));
    }

    [Fact]
    public async Task Start_GivenABackendConnectionDrops_ClientConnectionShouldDrop_AndNewClientsShouldConnectToNextAvailableBackend()
    {
        Log.Information("-------------- Starting test: {name} --------------", nameof(Start_GivenABackendConnectionDrops_ClientConnectionShouldDrop_AndNewClientsShouldConnectToNextAvailableBackend));
        // Arrange
        await _fixture.StartTestHostAsync();

        CreateBackends(3);
        CreateClients(3);

        // Act/Assert
        _fixture.ConnectionsInRegistry.ActiveConnections.Should().BeEmpty();

        // wait for backend watchdog connections to be established
        await WaitForMatchAsync("WatchDog ActiveConnections", () => _fixture.ConnectionsOutRegistry.ActiveConnections.Count == 3);
        var watchdogBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).ToList();
        Log.Information("Out connections established: {count}", watchdogBackendConnections.Count);

        // connect clients
        await ConnectClientsAsync(_fixture.TestClients);
        await Task.Delay(1000); // give some time for clients to start connecting

        Log.Information("Check out connections since client connections");
        // fake backends should have 6 clients connected in total (3 watchdog + 3 clients)
        await WaitForMatchAsync("Fake Backend connections", () => _fixture.Backends.Values.SelectMany(x => x.Clients).Count() == 6);
        var clientBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).Except(watchdogBackendConnections).Select(x => x.Value).ToList();

        // send data from client 1 to backend and assert
        await _fixture.TestClients[1].SendMessageAsync("Hello 1 from client 1");

        ClientHandler? firstBackend = null;
        await WaitForMatchAsync("Hello 1 from client 1", () => (firstBackend = GetBackendReceivedData(clientBackendConnections, "Hello 1 from client 1")) != null);
        await WaitForMatchAsync("Hello 1 from client 1 - response", () => _fixture.TestClients[1].GetMessage() == "Hello 1 from client 1 - response");
        firstBackend.Should().NotBeNull();

        // drop backend connection for client 1
        firstBackend.Dispose();

        await SendClientMessageConsumeErrorAsync(_fixture.TestClients[1], "Failed message for client 1");
        await WaitForMatchAsync("Client 1 close", () => !_fixture.TestClients[1].Client.Connected);

        // create new client connection
        CreateClient(4);

        await ConnectStartClientAsync(_fixture.TestClients[4]);
        await _fixture.TestClients[4].SendMessageAsync("Hello 1 from client 4");

        // ensure client 4 connects to a different, active, backend and gets a response
        await WaitForMatchAsync("Hello 1 from client 4 - response", () => _fixture.TestClients[4].GetMessage() == ("Hello 1 from client 4 - response"));
    }

    [Fact]
    public async Task Start_WithManyClientConnectionsCycling_WhenABackendConnectionDrops_ClientConnectionShouldDrop_AndNewClientsShouldConnectToNextAvailableBackend()
    {
        Log.Information("-------------- Starting test: {name} --------------", nameof(Start_WithManyClientConnectionsCycling_WhenABackendConnectionDrops_ClientConnectionShouldDrop_AndNewClientsShouldConnectToNextAvailableBackend));

        // Arrange        
        await _fixture.StartTestHostAsync();

        CreateBackends(3);
        var clients = CreateClients(3);

        using var cancelTokenSrc = new CancellationTokenSource();
        try
        {
            // Act/Assert

            _fixture.ConnectionsInRegistry.ActiveConnections.Should().BeEmpty();
            // continuously create and destroy _fixture.TestClients
            CreateClientsContinuously(cancelTokenSrc.Token);
            await Task.Delay(500); // give some time for some clients to connect

            // wait for backend watchdog connections to be established
            await WaitForMatchAsync("WatchDog ActiveConnections", () => _fixture.ConnectionsOutRegistry.ActiveConnections.Count == 3);
            var watchdogBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).ToList();

            // connect clients
            await ConnectClientsAsync(clients);

            // fake backends should have 6 clients connected in total (3 watchdog + 3 clients)
            await WaitForMatchAsync("Fake Backend connections", () => _fixture.Backends.Values.SelectMany(x => x.Clients).Count() >= 6);
            var clientBackendConnections = _fixture.Backends.SelectMany(x => x.Value.Clients).Except(watchdogBackendConnections).Select(x => x.Value).ToList();

            // send data from client 1 to backend and assert
            await _fixture.TestClients[1].SendMessageAsync("Hello 1 from client 1");

            ClientHandler? firstBackend = null;
            await WaitForMatchAsync("Hello 1 from client 1", () => (firstBackend = GetBackendReceivedData(clientBackendConnections, "Hello 1 from client 1")) != null);
            await WaitForMatchAsync("Hello 1 from client 1 - response", () => _fixture.TestClients[1].GetMessage() == "Hello 1 from client 1 - response");
            firstBackend.Should().NotBeNull();

            // drop backend connection for client 1
            firstBackend.Dispose();

            await SendClientMessageConsumeErrorAsync(_fixture.TestClients[1], "Failed message for client 1");
            await WaitForMatchAsync("Client 1 close", () => !_fixture.TestClients[1].Client.Connected);

            // create new client connection
            CreateClient(4);

            await ConnectStartClientAsync(_fixture.TestClients[4]);
            await _fixture.TestClients[4].SendMessageAsync("Hello 1 from client 4");

            // ensure client 4 connects to a different, active, backend and gets a response
            await WaitForMatchAsync("Hello 1 from client 4 - response", () => _fixture.TestClients[4].GetMessage() == ("Hello 1 from client 4 - response"));
        }
        finally
        {
            foreach ((_, var client) in clients) client.Stop();
            cancelTokenSrc.CancelDispose();
        }
    }

    #region Helpers

    private void CreateBackends(int backendCount)
    {
        if (backendCount == 0) throw new ArgumentException("backendCount must be > 0");

        for (int count = 1; count <= backendCount; count++)
        {
            if (!_fixture.Backends.TryAdd(count, CreateStartBackend(count, 3400 + count)))
                throw new Exception("Failed to add backend to list");
        }
    }

    private IDictionary<int, TCPClient> CreateClients(int clientCount)
    {
        Dictionary<int, TCPClient> clients = new();
        for (int count = 1; count <= clientCount; count++)
        {
            clients.Add(count, CreateClient(count));
        }

        return clients;
    }

    private void CreateClientsContinuously(CancellationToken cancelToken, int waitBetweenMs = 500)
    {
        var clientsTasks = Task.Run(async () =>
        {
            var clientId = new Random().Next(1000);
            while (!cancelToken.IsCancellationRequested)
            {
                await Task.Delay(waitBetweenMs, cancelToken);
                try
                {
                    var client = CreateClient(clientId);
                    await ConnectStartClientAsync(client);
                    await client.SendMessageAsync($"Hello from client {clientId}");
                    client.Stop();
                    _fixture.TestClients.TryRemove(clientId - 1, out _);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Client error");
                }
            }
        });
    }

    private static async Task SendClientMessageConsumeErrorAsync(TCPClient client, string txt)
    {
        try
        {
            await client.SendMessageAsync(txt);
        }
        catch (Exception) { }
    }

    private static ClientHandler? GetBackendReceivedData(List<ClientHandler> clientBackendConnections, string txtExpected)
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

    private static async Task WaitForMatchAsync(string alias, Func<bool> expr, int waitTimeoutMs = 20000)
    {
        var endTime = DateTime.UtcNow.AddMilliseconds(waitTimeoutMs);
        while (DateTime.UtcNow < endTime)
        {
            if (expr.Invoke()) return;
            await Task.Delay(1000);
        }
        throw new Exception($"'{alias}' condition not met within timeout"); // TODO return a better exception type
    }


    private static async Task ConnectClientsAsync(IDictionary<int, TCPClient> clients)
    {
        foreach (var client in clients.Values)
        {
            await ConnectStartClientAsync(client);
        }
    }

    private static async Task ConnectStartClientAsync(TCPClient client)
    {
        await client.Client.ConnectAsync("127.0.0.1", ServerFixture.TestPort);
        Log.Debug("Test client connected to port: {port}", ServerFixture.TestPort);
        client.StartRead();
    }

    private TCPClient CreateClient(int id)
    {
        var tcpClient = new TcpClient();
        tcpClient.SetOptions(new()
        {
            ReceiveTimeoutMs = 5000,
            SendTimeoutMs = 5000,
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192
        });

        var client = new TCPClient(_fixture.Sut.Services.GetRequiredService<ILogger<TCPClient>>(), tcpClient, id);
        _fixture.TestClients.TryAdd(id, client);
        return client;
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
                 config.Sources.Clear();
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
        _fixture.CloseBackends();
        _fixture.CloseTestClients();
        _fixture.CloseSut();
        GC.SuppressFinalize(this);
    }

    #endregion Helpers
}
