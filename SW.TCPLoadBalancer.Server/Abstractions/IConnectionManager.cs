namespace SW.TCPLoadBalancer.Server.Abstractions;

public interface IConnectionManager : INetworkClient, IConnectionKey, IAsyncDisposable { }
