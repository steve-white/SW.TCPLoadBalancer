using Microsoft.Extensions.Options;
using SW.TCPLoadBalancer.Server.Options;
using System.Net;

namespace SW.TCPLoadBalancer.Server.Validators;

public class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        var failures = new List<string>();

        if (options.ListenPort < 1 || options.ListenPort > 65535)
        {
            failures.Add("ListenPort must be between 1 and 65535");
        }

        if (options.ConnectionBacklog < 1 || options.ConnectionBacklog > 1000)
        {
            failures.Add("ConnectionBacklog must be between 1 and 1000");
        }

        if (options.BackendConnections?.Count == 0)
        {
            failures.Add("No BackendConnections set");
        }
        else
        {
            int position = 0;
            foreach (var backendDetail in options.BackendConnections!)
            {
                if (!IPAddress.TryParse(backendDetail.IPAddress, out _))
                    failures.Add($"[{position}] IPAddress must be valid");
                if (backendDetail.Port == default)
                    failures.Add($"[{position}] Port must be set");
                position++;
            }
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }
}