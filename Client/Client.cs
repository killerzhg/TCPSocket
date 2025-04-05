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
    class Client
    {
        private static TcpClient _client;
        private static NetworkStream _stream;
        private static CancellationTokenSource _cancellationTokenSource;
        private static readonly string server = "127.0.0.1"; // 服务器地址
        private static readonly int port = 4782; // 服务器端口

        static void Main(string[] args)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => ConnectAsync(_cancellationTokenSource.Token));
            Console.ReadKey();
            //通知取消所有SOCKET任务
            _cancellationTokenSource.Cancel();
        }

        private static async Task ConnectAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _client = new TcpClient();
                    await _client.ConnectAsync(server, port);
                    OnConnected();
                    _stream = _client.GetStream();
                    await ReceiveMessagesAsync(_stream, cancellationToken);
                }
                catch (SocketException)
                {
                    Console.WriteLine($"连接{server}失败，正在重试...");
                }
            }
        }

        private static async Task ReceiveMessagesAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[256];
            int bytesRead;
            try
            {
                while (!cancellationToken.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    OnMessageReceived(message);
                }
            }
            catch (IOException)
            {
                OnDisconnected();
            }
        }

        private static async void OnConnected()
        {
            if (_client.Connected)
            {
                await Task.Delay(100); // 等待100毫秒，确保真正的连接成功
                if (_client.Connected)
                    Console.WriteLine($"{server}连接成功！");
            }
        }

        private static void OnDisconnected()
        {
            Console.WriteLine($"{server}连接断开！");
            _client?.Close();
        }

        private static void OnMessageReceived(string message)
        {
            Console.WriteLine("收到"+server+"消息: {0}", message);
        }
    }
}
