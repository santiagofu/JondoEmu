using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jondo.Unity.Core
{
    public class NetworkServer
    {
        private Socket _listenSocket;
        private CancellationTokenSource _cts;

        public event Action<NetworkClient> OnClientConnected;

        public void Start(string ip, int port)
        {
            _cts = new CancellationTokenSource();
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
            _listenSocket.Listen(100);

            Console.WriteLine($"[Network] Server started on {ip}:{port}");
            
            _ = AcceptLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listenSocket?.Close();
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var socket = await _listenSocket.AcceptAsync(token);
                    var client = new NetworkClient(socket);
                    
                    Console.WriteLine($"[Network] Client connected from {socket.RemoteEndPoint}");
                    OnClientConnected?.Invoke(client);
                    
                    _ = client.ReceiveLoopAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Server stopped
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Network] Accept error: {ex.Message}");
            }
        }
    }
}
