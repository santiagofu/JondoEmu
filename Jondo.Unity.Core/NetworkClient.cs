using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Jondo.Unity.Core
{
    public class NetworkClient
    {
        private readonly Socket _socket;
        private readonly byte[] _buffer;

        public event Action<NetworkClient, byte[]> OnDataReceived;
        public event Action<NetworkClient> OnDisconnected;

        public NetworkClient(Socket socket)
        {
            _socket = socket;
            _buffer = new byte[8192];
        }

        public async Task ReceiveLoopAsync()
        {
            try
            {
                while (_socket.Connected)
                {
                    int bytesRead = await _socket.ReceiveAsync(_buffer, SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        break; // Disconnected
                    }

                    byte[] data = new byte[bytesRead];
                    Array.Copy(_buffer, data, bytesRead);
                    
                    OnDataReceived?.Invoke(this, data);
                }
            }
            catch (Exception)
            {
                // Ignored or logged
            }
            finally
            {
                Disconnect();
            }
        }

        public void Send(byte[] data)
        {
            if (_socket.Connected)
            {
                _socket.SendAsync(data, SocketFlags.None);
            }
        }

        public void Disconnect()
        {
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
                OnDisconnected?.Invoke(this);
            }
        }
    }
}
