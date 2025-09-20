using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Managers;
using SW.TCPLoadBalancer.Server.Options;
using SW.TCPLoadBalancer.Server.Registry;
using SW.TCPLoadBalancer.Server.Validators;
using SW.TCPLoadBalancer.Server.Workers;

namespace SW.TCPLoadBalancer.Server.Extensions;

public static class ServiceExtensions
{
    public static void AddBusinessServices(this IServiceCollection services, HostBuilderContext context)
    {
        services.AddHostedService<ServerWorker>();

        services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();
        services.AddOptions<ServerOptions>()
            .Bind(context.Configuration.GetSection(ServerOptions.Section))
            .ValidateOnStart();

        services.AddSingleton<ITCPServer, TCPServer>();
        services.AddSingleton<IConnectionsOutWatchdog, ConnectionsOutWatchdog>();
        services.AddSingleton<IConnectionOutSelector, ConnectionOutSelector>();
        services.AddSingleton<IConnectionsInRegistry, ConnectionsInRegistry>();
        services.AddSingleton<IConnectionsOutRegistry, ConnectionsOutRegistry>();
        services.AddTransient<ConnectionInManager>();
        services.AddTransient<ConnectionOutManager>();
        services.AddTransient<ILogNetworkSend, NetworkSendLog>();

    }
}
