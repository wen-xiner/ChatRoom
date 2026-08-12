using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatRoom.Core.Net
{
    /// <summary>
    /// WebSocket 客户端（用系统自带 ClientWebSocket）。
    /// 后台收消息，大消息自动把分片拼起来。
    /// </summary>
    public class ChatClient : IDisposable
    {
        /// <summary>单帧数据块大小（与服务端一致，上传/下载分块用）。</summary>
        public const int ChunkSize = 256 * 1024;

        private readonly object _sendLock = new object();

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private bool _running;
        // 是否用户主动断开：true 时收包循环退出不再触发 OnDisconnected（不然弹窗打扰）
        private bool _userDisconnect;

        public bool IsConnected { get; private set; }
        public string ServerIp { get; private set; }
        public int ServerPort { get; private set; }

        /// <summary>日志。</summary>
        public event Action<string> Log;
        /// <summary>收到协议消息（在接收线程触发，调用方要自己切回 UI 线程）。</summary>
        public event Action<WsMessage> OnMessage;
        /// <summary>连接成功（HelloAck 由调用方自己确认）。</summary>
        public event Action OnConnected;
        /// <summary>连接断开。</summary>
        public event Action OnDisconnected;

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            if (IsConnected) return true;
            _userDisconnect = false; // 新连接重置，断了要能再通知
            ServerIp = ip;
            ServerPort = port;
            try
            {
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                var uri = new Uri($"ws://{ip}:{port}");
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(8));
                    await _ws.ConnectAsync(uri, cts.Token);
                }
                IsConnected = true;
                _running = true;
                OnConnected?.Invoke();
                _ = Task.Run(ReceiveLoopAsync);
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"连接失败: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        public void Send(WsMessage msg)
        {
            SendRaw(msg.ToJson());
        }

        public void SendRaw(string json)
        {
            lock (_sendLock)
            {
                try
                {
                    if (_ws == null || _ws.State != WebSocketState.Open) return;
                    var bytes = Encoding.UTF8.GetBytes(json);
                    _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"发送失败: {ex.Message}");
                }
            }
        }

        public void Disconnect()
        {
            _userDisconnect = true; // 用户主动断开，收包循环退出时不触发 OnDisconnected
            _running = false;
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1000);
            }
            catch { }
            Cleanup();
            IsConnected = false;
        }

        public void Dispose() => Disconnect();

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (_running && _ws != null && _ws.State == WebSocketState.Open)
                {
                    string text;
                    using (var ms = new MemoryStream())
                    {
                        while (true)
                        {
                            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                _running = false;
                                break;
                            }
                            ms.Write(buffer, 0, result.Count);
                            if (result.EndOfMessage) break;
                        }
                        if (!_running) break;
                        text = Encoding.UTF8.GetString(ms.ToArray());
                    }

                    var msg = WsMessage.FromJson(text);
                    if (msg != null)
                        OnMessage?.Invoke(msg);
                }
            }
            catch (Exception ex)
            {
                if (_running) Log?.Invoke($"接收异常: {ex.Message}");
            }
            finally
            {
                // 之前按 _running 判断：服务器发 Close 帧时循环体先置 _running=false，
                // finally 里就漏触发 OnDisconnected，客户端一直显示"已连接"、发送静默丢失。
                // 现在一律清理，只有用户主动断开（_userDisconnect）才不通知。
                _running = false;
                IsConnected = false;
                if (!_userDisconnect)
                    OnDisconnected?.Invoke();
            }
        }

        private void Cleanup()
        {
            try { _ws?.Dispose(); } catch { }
            try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
            _ws = null;
            _cts = null;
        }
    }
}
