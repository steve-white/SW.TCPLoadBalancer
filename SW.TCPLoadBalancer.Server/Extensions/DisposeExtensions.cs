namespace SW.TCPLoadBalancer.Server.Extensions;

public static class CancellationTokenSourceExtensions
{
    public static void CancelDispose(this CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
