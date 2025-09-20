namespace SW.TCPLoadBalancer.Server.DTOs;

public struct FrameState
{
    public FrameState()
    {
    }

    public int ByteCountRemaining { get; set; } = 0;
    public string? SocketError { get; set; }
    public byte[]? BytesRemaining { get; set; }
    public bool SourceClosed { get; internal set; } = false;

    public void Reset()
    {
        ByteCountRemaining = 0;
        SocketError = null;
        BytesRemaining = null;
        SourceClosed = false;
    }
}
