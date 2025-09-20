using SW.TCPLoadBalancer.Server.Options;

namespace SW.TCPLoadBalancer.Server.Extensions;
public static class ConnectionDetailsExtensions
{
    public static string GetKey(this ConnectionDetails connectionDetails)
        => connectionDetails.IPAddress + ":" + connectionDetails.Port;
}
