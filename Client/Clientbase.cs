using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpClientLibrary
{
    /// <summary>
    /// TCP客户端基类，提供基本的TCP连接管理、自动重连和消息收发功能
    /// </summary>
    public abstract class TcpClientBase : IDisposable
    {
        #region 私有字段

        private TcpClient _client;
        private NetworkStream _stream;
        private readonly string _serverIp;
        private readonly int _serverPort;
        private bool _isConnected;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private readonly object _lockObj = new object();
        private readonly int _reconnectInterval;
        private readonly int _bufferSize;
        private bool _disposed = false;

        #endregion

        #region 公开属性

        /// <summary>
        /// 获取当前连接状态
        /// </summary>
        public bool IsConnected
        {
            get { return _isConnected; }
            private set { _isConnected = value; }
        }

        /// <summary>
        /// 获取服务器IP地址
        /// </summary>
        public string ServerIp => _serverIp;

        /// <summary>
        /// 获取服务器端口
        /// </summary>
        public int ServerPort => _serverPort;

        /// <summary>
        /// 获取或设置连接超时时间(毫秒)
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;

        /// <summary>
        /// 获取是否正在运行(尝试保持连接)
        /// </summary>
        public bool IsRunning => _isRunning;

        #endregion

        #region 事件

        /// <summary>
        /// 连接建立成功时触发
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// 连接断开时触发
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// 接收到消息时触发
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// 发生错误时触发
        /// </summary>
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化TCP客户端基类
        /// </summary>
        /// <param name="serverIp">服务器IP地址</param>
        /// <param name="serverPort">服务器端口</param>
        /// <param name="reconnectInterval">重连间隔(毫秒)，默认5000</param>
        /// <param name="bufferSize">接收缓冲区大小，默认4096</param>
        protected TcpClientBase(string serverIp, int serverPort, int reconnectInterval = 5000, int bufferSize = 4096)
        {
            _serverIp = serverIp ?? throw new ArgumentNullException(nameof(serverIp));

            if (serverPort <= 0 || serverPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(serverPort), "端口号必须在1-65535之间");

            _serverPort = serverPort;
            _reconnectInterval = reconnectInterval;
            _bufferSize = bufferSize;
            _isRunning = false;
            _isConnected = false;
        }

        #endregion

        #region 公开方法

        /// <summary>
        /// 启动客户端，开始尝试连接
        /// </summary>
        public virtual void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _cts = new CancellationTokenSource();

            // 启动连接任务
            Task.Run(async () =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (!IsConnected)
                        {
                            await ConnectAsync();
                        }

                        await Task.Delay(_reconnectInterval, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        //RaiseErrorEvent($"连接过程中出现异常: {ex.Message}", ex);
                        await Task.Delay(1000, _cts.Token);
                    }
                }
            }, _cts.Token);

            OnAfterStart();
        }

        /// <summary>
        /// Start方法执行后的回调，供子类重写
        /// </summary>
        protected virtual void OnAfterStart() { }

        /// <summary>
        /// 停止客户端，断开连接并停止重连
        /// </summary>
        public virtual void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Disconnect();

            OnAfterStop();
        }

        /// <summary>
        /// Stop方法执行后的回调，供子类重写
        /// </summary>
        protected virtual void OnAfterStop() { }

        /// <summary>
        /// 主动断开当前连接
        /// </summary>
        public virtual void Disconnect()
        {
            lock (_lockObj)
            {
                if (IsConnected)
                {
                    IsConnected = false;

                    try
                    {
                        _stream?.Close();
                        _client?.Close();
                    }
                    catch (Exception ex)
                    {
                        RaiseErrorEvent($"断开连接时出错: {ex.Message}", ex);
                    }
                    finally
                    {
                        _stream = null;
                        _client = null;
                        OnDisconnected();
                    }
                }
            }
        }

        /// <summary>
        /// 异步发送消息
        /// </summary>
        /// <param name="message">要发送的消息</param>
        /// <returns>发送任务</returns>
        public async Task SendMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentNullException(nameof(message));

            await SendDataAsync(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// 异步发送二进制数据
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <returns>发送任务</returns>
        public async Task SendDataAsync(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));

            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("客户端未连接");

            try
            {
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();

                OnAfterSendData(data);
            }
            catch (Exception ex)
            {
                RaiseErrorEvent($"发送数据时出错: {ex.Message}", ex);
                Disconnect();
                throw;
            }
        }

        /// <summary>
        /// 发送数据后的回调，供子类重写
        /// </summary>
        /// <param name="data">已发送的数据</param>
        protected virtual void OnAfterSendData(byte[] data) { }

        #endregion

        #region 受保护的虚方法

        /// <summary>
        /// 处理接收到的数据，供子类重写
        /// </summary>
        /// <param name="data">接收到的数据</param>
        /// <param name="length">数据长度</param>
        protected virtual void ProcessReceivedData(byte[] data, int length)
        {
            string message = Encoding.UTF8.GetString(data, 0, length);
            OnMessageReceived(message);
        }

        /// <summary>
        /// 连接成功后调用，供子类重写
        /// </summary>
        protected virtual void OnConnected()
        {
            Connected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 连接断开后调用，供子类重写
        /// </summary>
        protected virtual void OnDisconnected()
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 接收到消息后调用，供子类重写
        /// </summary>
        /// <param name="message">接收到的消息</param>
        protected virtual void OnMessageReceived(string message)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        /// <summary>
        /// 触发错误事件
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="exception">异常对象</param>
        protected void RaiseErrorEvent(string errorMessage, Exception exception = null)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs(errorMessage, exception));
        }

        #endregion

        #region 私有方法

        private async Task ConnectAsync()
        {
            try
            {
                // 先断开现有连接
                Disconnect();

                // 创建新的连接
                _client = new TcpClient();

                // 使用带超时的连接方法
                using (var timeoutCts = new CancellationTokenSource(ConnectionTimeout))
                {
                    var connectTask = _client.ConnectAsync(_serverIp, _serverPort);
                    await Task.WhenAny(connectTask, Task.Delay(ConnectionTimeout));

                    if (!connectTask.IsCompleted)
                    {
                        throw new TimeoutException($"连接超时(>{ConnectionTimeout}ms)");
                    }

                    if (connectTask.IsFaulted && connectTask.Exception != null)
                    {
                        throw connectTask.Exception.InnerException ?? connectTask.Exception;
                    }
                }

                _stream = _client.GetStream();

                IsConnected = true;
                OnConnected();

                // 启动消息接收任务
                StartReceiving();
            }
            catch (Exception ex)
            {
                RaiseErrorEvent($"连接服务器失败: {ex.Message}", ex);
                IsConnected = false;
                throw;
            }
        }

        private void StartReceiving()
        {
            Task.Run(async () =>
            {
                byte[] buffer = new byte[_bufferSize];

                try
                {
                    while (IsConnected && _isRunning)
                    {
                        if (_stream.CanRead)
                        {
                            int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                            if (bytesRead > 0)
                            {
                                // 处理接收到的数据
                                ProcessReceivedData(buffer, bytesRead);
                            }
                            else
                            {
                                // 服务器关闭连接
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消操作，正常退出
                }
                catch (Exception ex)
                {
                    RaiseErrorEvent($"接收消息时出错: {ex.Message}", ex);
                }
                finally
                {
                    // 确保断开处理
                    if (IsConnected)
                    {
                        Disconnect();
                    }
                }
            }, _cts.Token);
        }

        #endregion

        #region IDisposable 实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    Stop();
                    _cts?.Dispose();
                    _stream?.Dispose();
                    _client?.Dispose();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~TcpClientBase()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 消息接收事件参数
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 获取接收到的消息
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 初始化消息接收事件参数
        /// </summary>
        /// <param name="message">接收到的消息</param>
        public MessageReceivedEventArgs(string message)
        {
            Message = message;
        }
    }

    /// <summary>
    /// 错误事件参数
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 获取错误消息
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 获取异常对象
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// 初始化错误事件参数
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="exception">异常对象</param>
        public ErrorEventArgs(string errorMessage, Exception exception = null)
        {
            ErrorMessage = errorMessage;
            Exception = exception;
        }
    }
}