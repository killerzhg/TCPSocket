using System;
using System.Text;
using System.Threading.Tasks;
using TcpClientLibrary;

namespace TcpClientDemo
{
    // 使用自定义TCP客户端的示例程序
    class Program
    {
        static async Task Main(string[] args)
        {
            string serverIp = "127.0.0.1";
            int serverPort = 4782;
            string clientId = "Client001";

            // 创建自定义TCP客户端
            using (var client = new CustomTcpClient(serverIp, serverPort, clientId))
            {
                // 注册事件处理
                //client.Connected += (sender, e) => Console.WriteLine("事件：已连接到服务器");
                //client.Disconnected += (sender, e) => Console.WriteLine("事件：已断开与服务器的连接");
                //client.MessageReceived += (sender, e) => Console.WriteLine($"事件：收到消息 - {e.Message}");
                //client.ErrorOccurred += (sender, e) => Console.WriteLine($"事件：发生错误 - {e.ErrorMessage}");

                // 设置连接超时时间
                client.ConnectionTimeout = 3000; // 3秒

                // 启动客户端
                client.Start();

                Console.WriteLine($"尝试连接到 {serverIp}:{serverPort}...");
                Console.WriteLine("命令：");
                Console.WriteLine("  send <消息> - 发送消息");
                Console.WriteLine("  exit - 退出");

                bool running = true;
                while (running)
                {
                    string input = Console.ReadLine();
                    if (string.IsNullOrEmpty(input))
                        continue;

                    if (input.StartsWith("send ", StringComparison.OrdinalIgnoreCase))
                    {
                        string message = input.Substring(5);
                        try
                        {
                            if (client.IsConnected)
                            {
                                await client.SendMessageAsync(message);
                            }
                            else
                            {
                                Console.WriteLine("未连接到服务器，无法发送消息");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"发送失败: {ex.Message}");
                        }
                    }
                    else if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        running = false;
                    }
                    else
                    {
                        Console.WriteLine("未知命令");
                    }
                }

                // 客户端会在using块结束时自动释放资源
            }

            Console.WriteLine("程序已退出");
        }
    }

    public class CustomTcpClient : TcpClientBase
    {
        // 可以添加自定义属性
        public string ClientId { get; }

        // 自定义构造函数
        public CustomTcpClient(string serverIp, int serverPort, string clientId)
            : base(serverIp, serverPort)
        {
            ClientId = clientId;
        }

        // 重写连接成功处理方法
        protected override void OnConnected()
        {
            Console.WriteLine($"客户端 {ClientId} 已连接到服务器 {ServerIp}:{ServerPort}");

            // 发送客户端标识
            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500); // 稍微延迟，确保连接稳定
                    await SendMessageAsync($"CLIENT_ID:{ClientId}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送客户端ID时出错: {ex.Message}");
            }

            // 调用基类方法，确保事件被触发
            base.OnConnected();
        }

        // 重写断开连接处理方法
        protected override void OnDisconnected()
        {
            Console.WriteLine($"客户端 {ClientId} 已断开与服务器的连接");

            // 调用基类方法，确保事件被触发
            base.OnDisconnected();
        }

        // 重写发送数据后的处理方法
        protected override void OnAfterSendData(byte[] data)
        {
            string message = Encoding.UTF8.GetString(data);
            Console.WriteLine($"客户端 {ClientId} 已发送: {message}");
        }

        // 处理接收数据
        protected override void ProcessReceivedData(byte[] data, int length)
        {
            string message = Encoding.UTF8.GetString(data, 0, length);
            Console.WriteLine($"客户端 {ClientId} 收到消息: {message}");

            // 处理特殊消息
            if (message.StartsWith("PING"))
            {
                try
                {
                    Task.Run(async () => await SendMessageAsync("PONG"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"回复PING消息时出错: {ex.Message}");
                }
            }

            // 这个简单示例中，我们只是将数据转换为字符串并调用基类方法
            base.ProcessReceivedData(data, length);
        }
    }
}