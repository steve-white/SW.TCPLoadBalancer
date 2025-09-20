namespace SW.TCPLoadBalancer.Server.DTOs;

public struct SendState
{
    public int BytesSent { get; set; }
    public Exception? Exception { get; set; }

    public SendState() { }

    public void Reset()
    {
        BytesSent = 0;
        Exception = null;
    }
}
