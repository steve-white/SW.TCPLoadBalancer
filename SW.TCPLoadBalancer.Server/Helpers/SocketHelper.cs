using SW.TCPLoadBalancer.Server.Abstractions;
using SW.TCPLoadBalancer.Server.DTOs;
using System.Net.Sockets;

namespace SW.TCPLoadBalancer.Server.Helpers;

public static class SocketHelper
{
    private const int BufferSize = 16384;

    public static async Task ForwardBufferedDataAsync(FrameState frameState, NetworkStream src, INetworkClient dst)
    {
        frameState.Reset();
        byte[] buffer = new byte[BufferSize];
        int bytesRead;
        SendState sendState = new();

        // TODO Use ArrayPool to avoid repeated allocations?

        // NB: Closing of the underlying streams/sockets will cause a break from the read loop
        while ((bytesRead = await src.ReadAsync(buffer)) > 0)
        {
            frameState.ByteCountRemaining = bytesRead;
            sendState.Reset();

            await dst.SendAsync(sendState, buffer, bytesRead);
            frameState.ByteCountRemaining -= sendState.BytesSent;
            if (sendState.Exception != null)
            {
                frameState.SocketError = sendState.Exception.Message;
                
                byte[] newBuffer = new byte[frameState.ByteCountRemaining];
                Array.Copy(buffer, sendState.BytesSent, newBuffer, 0, frameState.ByteCountRemaining);
                frameState.BytesRemaining = buffer;
                return;
            }
        }
        frameState.SourceClosed = true;
    }

    public static async Task SendAsync(SendState sendState, Socket socket, byte[] buffer, int bytesRead)
    {
        sendState.Reset();
        try
        {
            while (sendState.BytesSent < bytesRead)
            {
                int sent = await socket.SendAsync(
                    new ArraySegment<byte>(buffer, sendState.BytesSent, bytesRead - sendState.BytesSent),
                    SocketFlags.None);
                sendState.BytesSent += sent;
            }
        }
        catch (Exception ex)
        {
            sendState.Exception = ex; // exceptions are heavyweight, so just store it for later inspection
        }
    }
}