using Serilog;
using SW.TCPLoadBalancer.Server.Extensions;
using System.Net.Sockets;
using System.Text;

namespace SW.TCPLoadBalancer.Tests.Integration.Fakes;

public class ClientHandler(TcpClient tcpClient, int id, string clientKey) : IDisposable
{
    private const int BufferSize = 4096;
    private readonly TcpClient _tcpClient = tcpClient;
    private readonly NetworkStream _stream = tcpClient.GetStream();
    private readonly List<byte> _receivedData = new();
    private readonly object _dataLock = new();
    public int Id { get; private set; } = id;
    public string? Alias { get; private set; } = clientKey;
    public bool IsConnected => _tcpClient?.Connected ?? false;

    public async Task HandleAsync()
    {
        var buffer = new byte[BufferSize];

        try
        {
            _tcpClient.SetOptions(new Server.Options.ServerOptions());
            while (_tcpClient.Connected)
            {
                string receivedText = "";
                var bytesRead = await _stream.ReadAsync(buffer);
                if (bytesRead > 0)
                {
                    receivedText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Log.Debug("[FakeBackend-{id}] Received: '{txt}'", Id, receivedText);
                }
                if (bytesRead == 0)
                    break;

                lock (_dataLock)
                {
                    _receivedData.AddRange(buffer.Take(bytesRead));
                }
                var responseTxt = $"{receivedText} - response";
                var bufferResponse = Encoding.UTF8.GetBytes(responseTxt);
                Log.Debug("[FakeBackend-{id}] Responding: '{txt}'", Id, responseTxt);
                await WriteAsync(responseTxt);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[FakeBackend-{id}] Error in client read loop - {Message}", Id, ex.Message);
        }
        finally
        {
            Dispose();
        }
    }

    public async Task WriteAsync(string message)
    {
        if (!IsConnected) return;

        try
        {
            var data = Encoding.UTF8.GetBytes(message);
            await _stream.WriteAsync(data);
            await _stream.FlushAsync();
        }
        catch (Exception)
        {
            Log.Error("[FakeBackend-{id}] Error writing to client", Id);
            throw;
        }
    }

    public byte[] GetReceivedData()
    {
        lock (_dataLock)
        {
            return _receivedData.ToArray();
        }
    }

    public string GetReceivedText() => Encoding.UTF8.GetString(GetReceivedData());

    public void ClearReceivedData()
    {
        lock (_dataLock)
        {
            _receivedData.Clear();
        }
    }

    public void Dispose()
    {
        Log.Information("[FakeBackend-{id}] Client disconnecting from backend.", Id);
        try
        {
            _stream?.Dispose();
            _tcpClient?.Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FakeBackend-{id}] Error closing client", Id);
        }

    }
}

