namespace SW.TCPLoadBalancer.Server.Options;

public class ServerOptions
{
    public const string Section = "ServerOptions";
    public string ListenInterface { get; set; } = "0.0.0.0";
    public int ListenPort { get; set; } = 3401;
    public int ConnectionBacklog { get; set; } = 128;
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public int SendTimeoutMs { get; set; } = 5000;
    public int BackendReconnectWaitMs { get; set; } = 5000;
    public int ReceiveBufferSize { get; set; } = 16384;
    public int SendBufferSize { get; set; } = 16384;
    public List<ConnectionDetails> BackendConnections { get; set; } = new();
}
