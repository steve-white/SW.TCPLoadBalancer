using Serilog;
using System.Net.Sockets;
using System.Text;

namespace SW.TCPLoadBalancer.Tests.Integration.Fakes;

public partial class FakeBackendServer
{
    public class ClientHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        public int Id { get; private set; }
        private readonly NetworkStream _stream;
        private readonly List<byte> _receivedData = new();
        private object _dataLock = new();

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public ClientHandler(TcpClient tcpClient, int id)
        {
            _tcpClient = tcpClient;
            Id = id;
            _stream = tcpClient.GetStream();
        }

        public async Task HandleAsync()
        {
            var buffer = new byte[BufferSize];

            try
            {
                while (_tcpClient.Connected)
                {
                    string receivedText = "";
                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
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

                    await _stream.WriteAsync(bufferResponse, 0, bufferResponse.Length);
                    await _stream.FlushAsync();
                }
            }
            catch (Exception)
            {
                // Connection closed or error occurred
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
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }
            catch (Exception)
            {
                // Handle write error
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
            _stream?.Dispose();
            _tcpClient?.Close();
            Log.Information("[FakeBackend-{id}] Client disconnecting from backend.", Id);
        }
    }
}
