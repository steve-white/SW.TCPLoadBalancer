namespace SW.TCPLoadBalancer.Server.DTOs;

public readonly struct SendResult
{
    public int BytesSent { get; }
    public Exception? Exception { get; }

    public SendResult() { }
    public SendResult(int bytesSent, Exception? exception)
    {
        BytesSent = bytesSent;
        Exception = exception;
    }
}
