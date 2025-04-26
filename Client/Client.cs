using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    class TCPClient 
    {
        private static TcpClient _client;
        private static NetworkStream _stream;
        private static readonly string server = "127.0.0.1"; // 服务器地址
        private static readonly int port = 4782; // 服务器端口
        // 连接成功回调
        public event Action Connected;
        // 断开连接回调
        public event Action Disconnected;
        // 消息接收回调
        public event Action<string> MessageReceived;

        public async Task StartConnectAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _client = new TcpClient();
                    await _client.ConnectAsync(server, port);
                    Connected?.Invoke();
                    _stream = _client.GetStream();
                    await ReceiveMessagesAsync(_stream, cancellationToken);
                }
                catch (SocketException)
                {
                    Console.WriteLine($"连接{server}失败，正在重试...");
                }
            }
        }

        private async Task ReceiveMessagesAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[256];
            int bytesRead;
            try
            {
                while (!cancellationToken.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    MessageReceived?.Invoke(message);
                }
            }
            catch (IOException)
            {
                Disconnected?.Invoke();
            }
        }

        public async void OnConnected()
        {
            if (_client.Connected)
            {
                await Task.Delay(100); // 等待100毫秒，确保真正的连接成功
                if (_client.Connected)
                    Console.WriteLine($"{server}连接成功！");
            }
        }

        public void OnDisconnected()
        {
            Console.WriteLine($"{server}连接断开！");
            _client?.Close();
        }

        public void OnMessageReceived(string message)
        {
            Console.WriteLine("收到" + server + "消息: {0}", message);
        }
    }
    class Client
    {
        private static CancellationTokenSource _cancellationTokenSource;
        static void Main(string[] args)
        {
            TCPClient tCPClient = new TCPClient();
            _cancellationTokenSource = new CancellationTokenSource();
            tCPClient.Connected += tCPClient.OnConnected;
            tCPClient.Disconnected += tCPClient.OnDisconnected;
            tCPClient.MessageReceived += tCPClient.OnMessageReceived;

            Task.Run(() => tCPClient.StartConnectAsync(_cancellationTokenSource.Token));
            Console.ReadKey();
            //通知取消所有SOCKET任务
            _cancellationTokenSource.Cancel();
        }


    }
}
