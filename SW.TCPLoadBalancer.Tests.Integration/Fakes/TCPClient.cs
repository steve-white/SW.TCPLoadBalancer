using Microsoft.Extensions.Logging;
using Serilog;
using System.Net.Sockets;
using System.Text;

namespace SW.TCPLoadBalancer.Tests.Integration.Fakes;

public class TCPClient(ILogger<TCPClient> logger,
    TcpClient tcpClient)
{
    private readonly ILogger<TCPClient> _logger = logger;
    private readonly List<byte> _receivedData = new();
    private readonly object _dataLock = new();

    public TcpClient Client { get; } = tcpClient;

    public async Task SendAsync(byte[] data, int offset, int count)
    {
        var stream = Client.GetStream();
        await stream.WriteAsync(data, offset, count);
        await stream.FlushAsync();
    }

    public async Task SendMessageAsync(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        await SendAsync(data, 0, data.Length);
    }

    public void Stop()
    {
        Log.Debug("Stopping TCP client");
        Client?.Close();
    }

    public string GetMessage()
    {
        lock (_dataLock)
        {
            return Encoding.UTF8.GetString(_receivedData.ToArray());
        }
    }

    internal void StartRead()
    {
        _ = Task.Run(async () =>
        {
            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];

            try
            {
                using var stream = Client.GetStream();

                while (Client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                        break;

                    _logger.LogInformation("[{ip}] Client received: '{txt}'",
                        tcpClient.Client.RemoteEndPoint?.ToString(), Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    lock (_dataLock)
                    {
                        _receivedData.AddRange(buffer.Take(bytesRead));
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    Client?.Close();
                    Client?.Dispose();
                }
                catch (Exception)
                {
                }
            }
        });
    }
}
