using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatRoom.Config;
using ChatRoom.Core.Auth;
using ChatRoom.Core.Net;
using ChatRoom.Core.Storage;
using ChatRoom.Models;
using ChatRoom.Views;
using Microsoft.Win32;

namespace ChatRoom.ViewModels
{
    /// <summary>
    /// 客户端面板逻辑：连接、加入/切换/退出房间、文本/文件/图片收发、认证门禁。
    /// 消息隔离双保险：服务端只按当前房间投递/通知，客户端再按 roomId 过滤渲染。
    /// </summary>
    public class ClientViewModel : ObservableObject
    {
        public ChatClient Client { get; private set; }

        private string _serverIp = "127.0.0.1";
        public string ServerIp { get => _serverIp; set { _serverIp = value; OnPropertyChanged(); } }

        private string _portText = AppConfig.DefaultPort.ToString();
        public string PortText { get => _portText; set { _portText = value; OnPropertyChanged(); } }

        private string _userName = "";
        public string UserName { get => _userName; set { _userName = value; OnPropertyChanged(); } }

        public string UserId => AuthService.GetUserId();

        /// <summary>本机会话 ID（服务器握手时下发，用于区分本人/他人）。</summary>
        public string MySessionId { get; private set; }

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }

        private bool _isAuthenticated;
        public bool IsAuthenticated { get => _isAuthenticated; set { _isAuthenticated = value; OnPropertyChanged(); OnPropertyChanged(nameof(AuthText)); } }

        public string AuthText => IsAuthenticated ? "已认证" : "未认证";
        public string StatusText => IsConnected ? "● 已连接" : "○ 未连接";

        public ObservableCollection<JoinedRoomItem> JoinedRooms { get; } = new ObservableCollection<JoinedRoomItem>();
        public ObservableCollection<object> Messages { get; } = new ObservableCollection<object>();
        public ObservableCollection<UserInfo> Users { get; } = new ObservableCollection<UserInfo>();

        private string _activeRoomId;
        public string ActiveRoomId { get => _activeRoomId; private set { _activeRoomId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveRoomTitle)); } }
        public string ActiveRoomTitle => ActiveRoomId == null ? "未加入房间" : $"当前房间 #{ActiveRoomId}";

        private string _joinRoomInput = "";
        public string JoinRoomInput { get => _joinRoomInput; set { _joinRoomInput = value; OnPropertyChanged(); } }

        private string _inputText = "";
        public string InputText { get => _inputText; set { _inputText = value; OnPropertyChanged(); } }

        private string _statusDetail = "";
        public string StatusDetail { get => _statusDetail; set { _statusDetail = value; OnPropertyChanged(); } }

        // 按房间隔离的客户端缓存
        private readonly Dictionary<string, List<object>> _roomMessages = new Dictionary<string, List<object>>();
        private readonly Dictionary<string, List<UserInfo>> _roomUsers = new Dictionary<string, List<UserInfo>>();
        private readonly Dictionary<string, JoinedRoomItem> _joinedIndex = new Dictionary<string, JoinedRoomItem>();
        private readonly Dictionary<string, string> _roomNames = new Dictionary<string, string>();
        private readonly Dictionary<string, FileMessageItem> _fileItems = new Dictionary<string, FileMessageItem>();
        private readonly Dictionary<string, ImageMessageItem> _imageItems = new Dictionary<string, ImageMessageItem>();
        // 进行中的下载：fileId -> (roomId, fileName, kind, chunks)
        private readonly Dictionary<string, DownloadSession> _downloads = new Dictionary<string, DownloadSession>();

        private readonly ClientConfig _clientConfig;
        private readonly List<string> _savedRooms = new List<string>();

        public ClientViewModel()
        {
            IsAuthenticated = AuthService.IsAuthenticated();
            _clientConfig = JsonStore.Load<ClientConfig>(AppConfig.ClientConfigPath);
            _serverIp = _clientConfig.ServerIp;
            _portText = _clientConfig.Port.ToString();
            _userName = _clientConfig.UserName;
            if (_clientConfig.JoinedRooms != null)
                _savedRooms.AddRange(_clientConfig.JoinedRooms);
        }

        // ---------------- 连接 ----------------

        public async void Connect()
        {
            if (IsConnected) return;
            if (!int.TryParse(PortText.Trim(), out var port) || port <= 0 || port > 65535)
            {
                StatusDetail = "端口无效（1~65535）。";
                return;
            }
            if (string.IsNullOrWhiteSpace(UserName))
            {
                StatusDetail = "请输入显示用户名。";
                return;
            }

            Client = new ChatClient();
            Client.Log += msg => UiHelper.Dispatch(() => StatusDetail = msg);
            Client.OnConnected += () => UiHelper.Dispatch(() =>
            {
                Client.Send(WsMessage.Create(WsMessageType.Hello, new { userId = UserId, userName = UserName.Trim() }));
            });
            Client.OnDisconnected += () => UiHelper.Dispatch(() =>
            {
                IsConnected = false;
                StatusDetail = "连接已断开";
            });
            Client.OnMessage += msg => UiHelper.Dispatch(() => HandleMessage(msg));

            var ok = await Client.ConnectAsync(ServerIp.Trim(), port);
            if (ok)
            {
                IsConnected = true;
                IsAuthenticated = AuthService.IsAuthenticated();
                StatusDetail = $"已连接 {ServerIp.Trim()}:{port}，正在握手...";
            }
        }

        public void Disconnect()
        {
            Client?.Disconnect();
            Client = null;
            IsConnected = false;
            JoinedRooms.Clear();
            _joinedIndex.Clear();
            _roomMessages.Clear();
            _roomUsers.Clear();
            _roomNames.Clear();
            Messages.Clear();
            Users.Clear();
            ActiveRoomId = null;
            SaveClientConfig();
            StatusDetail = "已断开连接";
        }

        // ---------------- 房间管理 ----------------

        public void JoinRoom()
        {
            var roomId = JoinRoomInput.Trim();
            if (roomId.Length < 4 || roomId.Length > 6 || !roomId.All(char.IsDigit))
            {
                StatusDetail = "房间号必须为 4~6 位数字。";
                return;
            }
            if (!IsConnected) { StatusDetail = "请先连接服务器。"; return; }
            Client.Send(WsMessage.Create(WsMessageType.JoinRoom, new { roomId }));
            JoinRoomInput = "";
            StatusDetail = $"正在加入房间 {roomId}...";
        }

        /// <summary>退出指定房间。已关闭房间直接本地移除，不发消息给服务器。</summary>
        public void LeaveRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return;
            var item = GetJoined(roomId);
            if (item != null && !item.Available)
            {
                RemoveJoinedRoom(roomId);
                StatusDetail = $"已退出已关闭的房间 {roomId}";
                return;
            }
            if (!IsConnected) { StatusDetail = "请先连接服务器。"; return; }
            Client.Send(WsMessage.Create(WsMessageType.LeaveRoom, new { roomId }));
            // 主动退出后本地立即移除（服务器端已同步移除，不会回 UserLeft 给本人）
            RemoveJoinedRoom(roomId);
            StatusDetail = $"已退出房间 {roomId}";
        }

        public void LeaveCurrentRoom()
        {
            if (!string.IsNullOrEmpty(ActiveRoomId))
                LeaveRoom(ActiveRoomId);
        }

        public void SetActiveRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId) || roomId == ActiveRoomId) return;
            ActiveRoomId = roomId;
            Client?.Send(WsMessage.Create(WsMessageType.SetActiveRoom, new { roomId }));

            // 切换显示该房间消息（其它房间消息不上屏，隔离）
            Messages.Clear();
            if (_roomMessages.TryGetValue(roomId, out var list))
                foreach (var m in list) Messages.Add(m);

            var item = GetJoined(roomId);
            if (item != null) item.HasNewMessage = false;

            RefreshUsers(roomId);
        }

        private void RefreshUsers(string roomId)
        {
            Users.Clear();
            if (_roomUsers.TryGetValue(roomId, out var list))
            {
                foreach (var u in list.OrderByDescending(x => x.Online).ThenBy(x => x.UserName))
                {
                    u.IsMe = u.SessionId == MySessionId;
                    Users.Add(u);
                }
            }
        }

        private JoinedRoomItem GetJoined(string roomId)
            => _joinedIndex.TryGetValue(roomId, out var item) ? item : null;

        private void RemoveJoinedRoom(string roomId)
        {
            if (_joinedIndex.TryGetValue(roomId, out var item))
            {
                JoinedRooms.Remove(item);
                _joinedIndex.Remove(roomId);
            }
            _roomMessages.Remove(roomId);
            _roomUsers.Remove(roomId);
            _roomNames.Remove(roomId);
            if (ActiveRoomId == roomId)
            {
                ActiveRoomId = null;
                Messages.Clear();
                Users.Clear();
            }
            if (_savedRooms.Remove(roomId))
                SaveClientConfig();
        }

        // ---------------- 文本 ----------------

        public void SendText()
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId))
            {
                StatusDetail = "请先连接并加入房间。";
                return;
            }
            var content = InputText.Trim();
            if (content.Length == 0) return;
            Client.Send(WsMessage.Create(WsMessageType.Chat, new { roomId = ActiveRoomId, content }));
            InputText = "";
        }

        // ---------------- 文件 / 图片 ----------------

        public async void SendFile()
        {
            if (!EnsureAuthForTransfer()) return;
            if (string.IsNullOrEmpty(ActiveRoomId)) { StatusDetail = "请先加入房间。"; return; }
            var dlg = new OpenFileDialog { Title = "选择要发送的文件" };
            if (dlg.ShowDialog() != true) return;
            await Task.Run(() => UploadFile(dlg.FileName, isImage: false));
        }

        public async void SendImage()
        {
            if (!EnsureAuthForTransfer()) return;
            if (string.IsNullOrEmpty(ActiveRoomId)) { StatusDetail = "请先加入房间。"; return; }
            var dlg = new OpenFileDialog { Title = "选择要发送的图片", Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() != true) return;
            await Task.Run(() => UploadFile(dlg.FileName, isImage: true));
        }

        private void UploadFile(string path, bool isImage)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var fileId = Guid.NewGuid().ToString("N");
                var totalChunks = (int)Math.Ceiling(bytes.Length / (double)ChatClient.ChunkSize);
                if (totalChunks == 0) totalChunks = 1;

                Client.Send(WsMessage.Create(WsMessageType.UploadStart, new
                {
                    roomId = ActiveRoomId,
                    fileId,
                    fileName = Path.GetFileName(path),
                    totalChunks,
                    isImage
                }));
                for (var i = 0; i < totalChunks; i++)
                {
                    var offset = i * ChatClient.ChunkSize;
                    var len = Math.Min(ChatClient.ChunkSize, bytes.Length - offset);
                    var part = new byte[len];
                    Array.Copy(bytes, offset, part, 0, len);
                    Client.Send(WsMessage.Create(WsMessageType.FileChunk, new
                    {
                        roomId = ActiveRoomId,
                        fileId,
                        chunkIndex = i,
                        totalChunks,
                        dataBase64 = Convert.ToBase64String(part)
                    }));
                }
            }
            catch (Exception ex)
            {
                UiHelper.Dispatch(() => StatusDetail = "发送失败: " + ex.Message);
            }
        }

        public void DownloadFile(FileMessageItem item)
        {
            if (item == null) return;
            if (!EnsureAuthForTransfer()) return;
            item.StateText = "下载中...";
            var fileId = item.Data.FileId;
            _downloads[fileId] = new DownloadSession { RoomId = item.Data.RoomId, FileName = item.Data.FileName, Kind = "file" };
            Client.Send(WsMessage.Create(WsMessageType.FileRequest, new { roomId = item.Data.RoomId, fileId, kind = "file" }));
        }

        /// <summary>查看图片原图（认证后可查看/复制/下载）。</summary>
        public void ViewImage(ImageMessageItem item)
        {
            if (item == null) return;
            if (!EnsureAuthForTransfer()) return;
            StatusDetail = "正在获取原图...";
            var fileId = item.Data.FileId;
            _downloads[fileId] = new DownloadSession { RoomId = item.Data.RoomId, FileName = item.Data.FileName, Kind = "image" };
            Client.Send(WsMessage.Create(WsMessageType.FileRequest, new { roomId = item.Data.RoomId, fileId, kind = "image" }));
        }

        // ---------------- 认证 ----------------

        private bool EnsureAuthForTransfer()
        {
            if (AuthService.IsAuthenticated())
            {
                IsAuthenticated = true;
                return true;
            }
            ShowRegistration();
            return false;
        }

        /// <summary>打开注册窗口；注册成功刷新认证状态（占位图换缩略图）。</summary>
        public void ShowRegistration()
        {
            var dlg = new RegistrationDialog { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                RefreshAuthenticatedState();
                StatusDetail = "注册成功，已启用 图片/文件 收发。";
            }
        }

        public void RefreshAuthenticatedState()
        {
            IsAuthenticated = AuthService.IsAuthenticated();
            if (IsAuthenticated)
            {
                foreach (var kv in _imageItems.ToList())
                    kv.Value.ImageSource = BuildImageSource(kv.Value.Data, true);
            }
        }

        // ---------------- 收包处理 ----------------

        private void HandleMessage(WsMessage msg)
        {
            if (!Enum.TryParse(msg.type, out WsMessageType t)) return;
            switch (t)
            {
                case WsMessageType.HelloAck:
                    MySessionId = msg.payload["sessionId"]?.ToString();
                    StatusDetail = "服务器握手成功。";
                    RejoinSavedRooms();
                    break;
                case WsMessageType.Error: StatusDetail = msg.payload["reason"]?.ToString() ?? "服务器返回错误"; break;
                case WsMessageType.JoinAck: HandleJoinAck(msg); break;
                case WsMessageType.UserJoined: HandleUserJoined(msg); break;
                case WsMessageType.UserLeft: HandleUserLeft(msg); break;
                case WsMessageType.RoomClosed: HandleRoomClosed(msg); break;
                case WsMessageType.Kick: HandleKick(msg); break;
                case WsMessageType.RoomNotify:
                    var rn = msg.payload["roomId"]?.ToString();
                    if (rn != null && rn != ActiveRoomId)
                    {
                        var it = GetJoined(rn);
                        if (it != null) it.HasNewMessage = true;
                    }
                    break;
                case WsMessageType.Chat: HandleChat(msg); break;
                case WsMessageType.FileMeta: HandleFileMeta(msg); break;
                case WsMessageType.ImageMeta: HandleImageMeta(msg); break;
                case WsMessageType.FileData: HandleFileData(msg); break;
            }
        }

        private void HandleJoinAck(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (roomId == null) return;
            if (!(bool)(msg.payload["ok"] ?? false))
            {
                StatusDetail = $"加入房间 {roomId} 失败：{msg.payload["reason"]}";
                // 自动重连的已存房间若已不存在，从保存列表移除，避免残留
                if (_savedRooms.Remove(roomId))
                    SaveClientConfig();
                return;
            }

            var name = msg.payload["name"]?.ToString() ?? roomId;
            _roomNames[roomId] = name;
            _roomMessages[roomId] = new List<object>();
            _roomUsers[roomId] = new List<UserInfo>();

            var users = msg.payload["users"]?.ToObject<List<UserInfo>>() ?? new List<UserInfo>();
            _roomUsers[roomId].AddRange(users);

            var history = msg.payload["history"]?.ToObject<List<ChatMessage>>() ?? new List<ChatMessage>();
            foreach (var h in history)
                _roomMessages[roomId].Add(ToChatItem(h));

            var item = GetJoined(roomId);
            if (item == null)
            {
                item = new JoinedRoomItem { RoomId = roomId, Name = name, OnlineCount = users.Count };
                _joinedIndex[roomId] = item;
                JoinedRooms.Add(item);
            }
            else
            {
                item.Name = name;
                item.OnlineCount = users.Count;
                item.Available = true;
            }

            if (!_savedRooms.Contains(roomId))
            {
                _savedRooms.Add(roomId);
                SaveClientConfig();
            }

            SetActiveRoom(roomId);
            StatusDetail = $"已加入房间 {roomId}（{name}）";
        }

        private void HandleUserJoined(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var user = msg.payload["user"]?.ToObject<UserInfo>();
            if (roomId == null || user == null) return;

            // 按 SessionId 去重（同一机器多实例共享 UserId，不能用 UserId 判重）
            user.IsMe = user.SessionId == MySessionId;
            if (_roomUsers.TryGetValue(roomId, out var list) && !list.Any(u => u.SessionId == user.SessionId))
                list.Add(user);

            var item = GetJoined(roomId);
            if (item != null) item.OnlineCount = _roomUsers.TryGetValue(roomId, out var ul) ? ul.Count : item.OnlineCount;

            if (roomId == ActiveRoomId)
            {
                RefreshUsers(roomId);
                AppendMessage(new SystemMessageItem { Text = $"用户 {user.UserName} 加入了房间" });
            }
            else if (item != null)
            {
                item.HasNewMessage = true;
            }
        }

        private void HandleUserLeft(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var sessionId = msg.payload["sessionId"]?.ToString();
            if (roomId == null || string.IsNullOrEmpty(sessionId)) return;

            var leftName = sessionId;
            if (_roomUsers.TryGetValue(roomId, out var list))
            {
                var removed = list.FirstOrDefault(u => u.SessionId == sessionId);
                if (removed != null)
                {
                    list.Remove(removed);
                    leftName = removed.UserName;
                }
            }

            var item = GetJoined(roomId);
            if (item != null) item.OnlineCount = _roomUsers.TryGetValue(roomId, out var ul) ? ul.Count : item.OnlineCount;

            if (roomId == ActiveRoomId)
            {
                RefreshUsers(roomId);
                AppendMessage(new SystemMessageItem { Text = $"用户 {leftName} 离开了房间" });
            }
            else if (item != null)
            {
                item.HasNewMessage = true;
            }
        }

        private void HandleRoomClosed(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (roomId == null) return;
            var item = GetJoined(roomId);
            if (item != null)
            {
                item.Available = false;
                item.OnlineCount = 0;
            }
            if (roomId == ActiveRoomId)
            {
                AppendMessage(new SystemMessageItem { Text = $"房间 {roomId} 已被服务器关闭" });
                Users.Clear();
            }
            StatusDetail = $"房间 {roomId} 已被关闭";
        }

        /// <summary>被服务器踢出房间：本地立即移除。</summary>
        private void HandleKick(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var reason = msg.payload["reason"]?.ToString();
            if (string.IsNullOrEmpty(roomId)) return;
            AppendMessage(new SystemMessageItem { Text = $"你已被移出房间 {roomId}：{reason ?? "被服务器踢出"}" });
            RemoveJoinedRoom(roomId);
            StatusDetail = $"你已被移出房间 {roomId}";
        }

        private void HandleChat(WsMessage msg)
        {
            var chat = msg.payload.ToObject<ChatMessage>();
            if (chat == null || string.IsNullOrEmpty(chat.RoomId)) return;

            // 隔离：只渲染当前房间消息；其它房间只标红点
            if (chat.RoomId == ActiveRoomId)
            {
                var item = ToChatItem(chat);
                _roomMessages[chat.RoomId].Add(item);
                AppendMessage(item);
            }
            else
            {
                var it = GetJoined(chat.RoomId);
                if (it != null) it.HasNewMessage = true;
            }
        }

        private void HandleFileMeta(WsMessage msg)
        {
            var fm = msg.payload.ToObject<FileMessage>();
            if (fm == null) return;
            var item = new FileMessageItem { Data = fm, IsMine = fm.SenderSessionId == MySessionId };
            _fileItems[fm.FileId] = item;

            if (fm.RoomId == ActiveRoomId)
            {
                _roomMessages[fm.RoomId].Add(item);
                AppendMessage(item);
            }
            else
            {
                var it = GetJoined(fm.RoomId);
                if (it != null) it.HasNewMessage = true;
            }
        }

        private void HandleImageMeta(WsMessage msg)
        {
            var im = msg.payload.ToObject<ImageMessage>();
            if (im == null) return;
            var item = new ImageMessageItem { Data = im, IsMine = im.SenderSessionId == MySessionId, ImageSource = BuildImageSource(im, IsAuthenticated) };
            _imageItems[im.FileId] = item;

            if (im.RoomId == ActiveRoomId)
            {
                _roomMessages[im.RoomId].Add(item);
                AppendMessage(item);
            }
            else
            {
                var it = GetJoined(im.RoomId);
                if (it != null) it.HasNewMessage = true;
            }
        }

        private void HandleFileData(WsMessage msg)
        {
            var fileId = msg.payload["fileId"]?.ToString();
            var chunkIndex = (int)(msg.payload["chunkIndex"] ?? 0);
            var totalChunks = (int)(msg.payload["totalChunks"] ?? 0);
            var dataBase64 = msg.payload["dataBase64"]?.ToString();
            if (fileId == null || dataBase64 == null) return;
            if (!_downloads.TryGetValue(fileId, out var dl)) return;

            dl.Chunks.Add(Convert.FromBase64String(dataBase64));
            if (dl.Chunks.Count >= totalChunks)
            {
                _downloads.Remove(fileId);
                var bytes = Concat(dl.Chunks);
                SaveDownloaded(dl, bytes, fileId);
            }
        }

        private void SaveDownloaded(DownloadSession dl, byte[] bytes, string fileId)
        {
            try
            {
                if (dl.Kind == "image")
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), "ChatRoom_" + fileId + "_" + SanitizeName(dl.FileName));
                    File.WriteAllBytes(tempPath, bytes);
                    var viewer = new ImageViewerDialog(tempPath, dl.FileName) { Owner = Application.Current?.MainWindow };
                    viewer.ShowDialog();
                }
                else
                {
                    var dlg = new SaveFileDialog { FileName = SanitizeName(dl.FileName), Title = "保存文件" };
                    if (dlg.ShowDialog() == true)
                        File.WriteAllBytes(dlg.FileName, bytes);
                }
                if (_fileItems.TryGetValue(fileId, out var item))
                    item.StateText = "已下载";
                StatusDetail = "下载完成。";
            }
            catch (Exception ex)
            {
                StatusDetail = "保存失败: " + ex.Message;
            }
        }

        private void AppendMessage(object item) => Messages.Add(item);

        /// <summary>握手成功后自动重连上次加入的房间（房间不存在则服务端返回失败）。</summary>
        private void RejoinSavedRooms()
        {
            foreach (var roomId in _savedRooms.ToList())
                Client?.Send(WsMessage.Create(WsMessageType.JoinRoom, new { roomId }));
        }

        private void SaveClientConfig()
        {
            _clientConfig.ServerIp = ServerIp;
            _clientConfig.Port = int.TryParse(PortText.Trim(), out var p) ? p : AppConfig.DefaultPort;
            _clientConfig.UserName = UserName;
            _clientConfig.JoinedRooms = _savedRooms.ToList();
            JsonStore.Save(AppConfig.ClientConfigPath, _clientConfig);
        }

        private ChatMessageItem ToChatItem(ChatMessage chat)
            => new ChatMessageItem { Data = chat, IsMine = chat.SenderSessionId == MySessionId };

        // ---------------- 工具 ----------------

        private static BitmapSource BuildImageSource(ImageMessage im, bool authenticated)
        {
            if (authenticated && !string.IsNullOrEmpty(im.ThumbBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(im.ThumbBase64);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { /* 解码失败回落占位图 */ }
            }
            return PlaceholderImage();
        }

        /// <summary>未认证/无缩略图时的统一占位图。</summary>
        private static BitmapSource PlaceholderImage()
        {
            const int w = 140, h = 100;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            var px = new byte[w * h * 4];
            for (var i = 0; i < px.Length; i += 4)
            {
                px[i] = 214; px[i + 1] = 214; px[i + 2] = 214; px[i + 3] = 255; // 浅灰
            }
            bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        private static byte[] Concat(List<byte[]> chunks)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var c in chunks) ms.Write(c, 0, c.Length);
                return ms.ToArray();
            }
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private class DownloadSession
        {
            public string RoomId;
            public string FileName;
            public string Kind; // file / image
            public List<byte[]> Chunks = new List<byte[]>();
        }
    }
}
