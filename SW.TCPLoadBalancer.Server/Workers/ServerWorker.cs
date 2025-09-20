using Microsoft.Extensions.Hosting;

namespace SW.TCPLoadBalancer.Server.Workers;

public class ServerWorker(ILogger<ServerWorker> logger,
    ITCPServer server) : BackgroundService
{
    private readonly ILogger<ServerWorker> _logger = logger;
    private readonly ITCPServer _server = server;

    protected override Task ExecuteAsync(CancellationToken cancelToken)
    {
        try
        {
            _logger.LogInformation("--------- Starting TCPLoadBalancer ---------");
            return _server.StartAsync(cancelToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error starting server - {Message}", ex.Message);
            throw;
        }
    }
}