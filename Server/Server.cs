using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        private static TcpListener _server;
        private static bool _isRunning;

        static void Main(string[] args)
        {
            int port = 4782; // 服务器端口
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();
            _isRunning = true;

            Console.WriteLine("服务器已启动，等待客户端连接...");

            Task.Run(() => AcceptClientsAsync());

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
            _isRunning = false;
            _server.Stop();
        }

        private static async Task AcceptClientsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = await _server.AcceptTcpClientAsync();
                    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    Console.WriteLine("客户端已连接！客户端IP: {0}", clientIp);
                    await Task.Run(() => HandleClientAsync(client)); // 为每个客户端连接创建一个新的任务
                }
                catch (ObjectDisposedException)
                {
                    // 服务器已停止
                    break;
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[256];
            int bytesRead;
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            try
            {
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine("收到消息: {0} 来自客户端IP: {1}", message, clientIp);

                    // 发送响应回客户端
                    string response = "消息已收到";
                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseData, 0, responseData.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("处理客户端时发生错误: {0}", ex.Message);
            }
            finally
            {
                client.Close();
                Console.WriteLine("客户端已断开连接！客户端IP: {0}", clientIp);
            }
        }
    }
}
