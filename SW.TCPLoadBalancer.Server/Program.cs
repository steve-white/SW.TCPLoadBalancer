using Microsoft.Extensions.Hosting;
using Serilog;
using SW.TCPLoadBalancer.Server.Extensions;

var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, configuration) =>
    {
        configuration.ReadFrom.Configuration(context.Configuration);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddBusinessServices(context);
    });

var host = builder.Build();
await host.RunAsync();
