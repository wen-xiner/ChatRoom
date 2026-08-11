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
    /// <summary>客户端面板逻辑：连接、进出房间、收发文本/文件/图片、认证门禁。
    /// 消息双重隔离：服务端只投当前房间，客户端再按 roomId 过滤，双保险。</summary>
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

        /// <summary>本机会话 ID（握手时下发，区分本人/他人）。</summary>
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

        // 按房间隔离的缓存
        private readonly Dictionary<string, List<object>> _roomMessages = new Dictionary<string, List<object>>();
        private readonly Dictionary<string, List<UserInfo>> _roomUsers = new Dictionary<string, List<UserInfo>>();
        private readonly Dictionary<string, JoinedRoomItem> _joinedIndex = new Dictionary<string, JoinedRoomItem>();
        private readonly Dictionary<string, string> _roomNames = new Dictionary<string, string>();
        private readonly Dictionary<string, FileMessageItem> _fileItems = new Dictionary<string, FileMessageItem>();
        private readonly Dictionary<string, ImageMessageItem> _imageItems = new Dictionary<string, ImageMessageItem>();
        // 进行中的下载：fileId → 下载会话
        private readonly Dictionary<string, DownloadSession> _downloads = new Dictionary<string, DownloadSession>();

        private readonly ClientConfig _clientConfig;

        // 记下加过的房间（存 client-config.json），连接后只显示不自动进，点了再进
        private readonly List<string> _savedRooms = new List<string>();

        // 上滚加载更早历史：防抖 + 无更多标记
        private bool _loadingHistory;
        private bool _noMoreHistory;

        public ClientViewModel()
        {
            IsAuthenticated = AuthService.IsAuthenticated();
            _clientConfig = JsonStore.Load<ClientConfig>(AppConfig.ClientConfigPath);
            _serverIp = _clientConfig.ServerIp;
            _portText = _clientConfig.Port.ToString();
            _userName = _clientConfig.UserName;
            // 按账号加载各自加过的房间，别让同机的多个账号串了
            if (_clientConfig.JoinedRoomsByUser != null &&
                _clientConfig.JoinedRoomsByUser.TryGetValue(AuthService.GetUserId(), out var userRooms) &&
                userRooms != null)
                _savedRooms.AddRange(userRooms);
        }

        // ---- 连接 ----

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

        // ---- 房间管理 ----

        public void JoinRoom() => JoinRoom(JoinRoomInput.Trim());

        /// <summary>加入指定房间（也用于重新加入历史房间）。</summary>
        public void JoinRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return;
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

        /// <summary>连接后把上次加过的房间摆出来，不自动进，点了再进。</summary>
        private void RestoreSavedRooms()
        {
            foreach (var roomId in _savedRooms.ToList())
            {
                if (GetJoined(roomId) != null) continue;
                var item = new JoinedRoomItem
                {
                    RoomId = roomId,
                    Name = roomId,
                    OnlineCount = 0,
                    Joined = false
                };
                // 这里必须同步写 _joinedIndex，不然收到 JoinAck 时 GetJoined 找不到，会重复加条目
                _joinedIndex[roomId] = item;
                JoinedRooms.Add(item);
            }
        }

        /// <summary>退出房间；已关闭的本地直接移除，不发消息。</summary>
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
            // 主动退出本地立即移除（服务器不会回 UserLeft）
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
            _loadingHistory = false;
            _noMoreHistory = false;
            Client?.Send(WsMessage.Create(WsMessageType.SetActiveRoom, new { roomId }));

            // 只显示当前房间消息（其它房间不上屏）
            Messages.Clear();
            if (_roomMessages.TryGetValue(roomId, out var list))
                foreach (var m in list) Messages.Add(m);

            var item = GetJoined(roomId);
            if (item != null) item.HasNewMessage = false;

            RefreshUsers(roomId);
        }

        /// <summary>向上滚到顶时叫它，去服务器拉更早的消息。</summary>
        public void LoadMoreHistory()
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId)) return;
            if (_loadingHistory || _noMoreHistory) return;
            var oldest = OldestMessageTime();
            if (!oldest.HasValue) return;
            _loadingHistory = true;
            Client.Send(WsMessage.Create(WsMessageType.HistoryRequest,
                new { roomId = ActiveRoomId, beforeTime = oldest.Value.ToString("o") }));
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

        /// <summary>移除房间记录与保存项（加入失败时清理）。</summary>
        private void RemoveSavedRoom(string roomId)
        {
            var item = GetJoined(roomId);
            if (item != null)
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

        // ---- 文本 ----

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

        // ---- 文件 / 图片 ----

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

        /// <summary>看原图（认证过了才能看/复制/下载）。</summary>
        public void ViewImage(ImageMessageItem item)
        {
            if (item == null) return;
            if (!EnsureAuthForTransfer()) return;
            StatusDetail = "正在获取原图...";
            var fileId = item.Data.FileId;
            _downloads[fileId] = new DownloadSession { RoomId = item.Data.RoomId, FileName = item.Data.FileName, Kind = "image" };
            Client.Send(WsMessage.Create(WsMessageType.FileRequest, new { roomId = item.Data.RoomId, fileId, kind = "image" }));
        }

        // ---- 认证 ----

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

        /// <summary>打开注册窗口；成功后刷新认证状态。</summary>
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

        // ---- 收包处理 ----

        private void HandleMessage(WsMessage msg)
        {
            if (!Enum.TryParse(msg.type, out WsMessageType t)) return;
            switch (t)
            {
                case WsMessageType.HelloAck:
                    MySessionId = msg.payload["sessionId"]?.ToString();
                    StatusDetail = "服务器握手成功。";
                    RestoreSavedRooms(); // 显示上次加过的房间（不自动加入）
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
                case WsMessageType.HistoryAck: HandleHistoryAck(msg); break;
            }
        }

        private void HandleJoinAck(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (roomId == null) return;
            if (!(bool)(msg.payload["ok"] ?? false))
            {
                StatusDetail = $"加入房间 {roomId} 失败：{msg.payload["reason"]}";
                // 历史恢复的房间加不进去就从列表里删掉，别残留
                RemoveSavedRoom(roomId);
                return;
            }

            var name = msg.payload["name"]?.ToString() ?? roomId;
            _roomNames[roomId] = name;
            _roomMessages[roomId] = new List<object>();
            _roomUsers[roomId] = new List<UserInfo>();

            var users = msg.payload["users"]?.ToObject<List<UserInfo>>() ?? new List<UserInfo>();
            _roomUsers[roomId].AddRange(users);

            var history = msg.payload["history"]?.ToObject<List<HistoryMessage>>() ?? new List<HistoryMessage>();
            foreach (var h in history)
            {
                var histItem = ToHistoryItem(h);
                if (histItem != null) _roomMessages[roomId].Add(histItem);
            }

            var item = GetJoined(roomId);
            if (item == null)
            {
                item = new JoinedRoomItem { RoomId = roomId, Name = name, OnlineCount = users.Count(u => u.Online) };
                _joinedIndex[roomId] = item;
                JoinedRooms.Add(item);
            }
            else
            {
                item.Name = name;
                item.OnlineCount = users.Count(u => u.Online);
                item.Available = true;
            }
            item.Joined = true; // 历史恢复的条目加入成功后才算真正已加入

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

            // 按 SessionId 去重（同机多实例共享 UserId，不能按它判重）
            user.IsMe = user.SessionId == MySessionId;
            if (_roomUsers.TryGetValue(roomId, out var list))
            {
                // 同一个人重新上线，清掉之前的灰色离线条目
                list.RemoveAll(u => u.UserId == user.UserId && !u.Online);
                if (!list.Any(u => u.SessionId == user.SessionId))
                    list.Add(user);
            }

            var item = GetJoined(roomId);
            if (item != null) item.OnlineCount = OnlineCountOf(roomId);

            if (roomId == ActiveRoomId)
            {
                RefreshUsers(roomId);
                // 自己加进来的提示不给自己看，免得一直挂着；别人正常看
                if (!user.IsMe)
                    AppendMessage(new SystemMessageItem { Text = $"用户 {user.UserName} 加入了房间" });
            }
            else if (item != null)
            {
                item.HasNewMessage = true;
            }
        }

        /// <summary>在线人数（只算 Online=true）。</summary>
        private int OnlineCountOf(string roomId)
            => _roomUsers.TryGetValue(roomId, out var list) ? list.Count(u => u.Online) : 0;

        private void HandleUserLeft(WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var sessionId = msg.payload["sessionId"]?.ToString();
            var offline = (bool)(msg.payload["offline"] ?? false);
            var userId = msg.payload["userId"]?.ToString();
            var userName = msg.payload["userName"]?.ToString();
            if (roomId == null) return;

            var leftName = userName ?? sessionId;
            if (_roomUsers.TryGetValue(roomId, out var list))
            {
                if (offline)
                {
                    // 断线就是离线：把这个会话标成离线，人还留在列表里，显示灰点
                    var entry = list.FirstOrDefault(u => u.SessionId == sessionId);
                    if (entry != null)
                    {
                        entry.Online = false;
                        if (!string.IsNullOrEmpty(entry.UserName)) leftName = entry.UserName;
                    }
                    else if (!string.IsNullOrEmpty(userId))
                    {
                        // 没有该会话条目时，按 userId 补一个离线条目
                        list.RemoveAll(u => u.UserId == userId && !u.Online);
                        list.Add(new UserInfo { SessionId = sessionId, UserId = userId, UserName = userName, Online = false });
                    }
                }
                else
                {
                    // 主动退出或被踢 → 真移除
                    var removed = list.FirstOrDefault(u => u.SessionId == sessionId);
                    if (removed != null)
                    {
                        list.Remove(removed);
                        if (!string.IsNullOrEmpty(removed.UserName)) leftName = removed.UserName;
                    }
                }
            }

            var item = GetJoined(roomId);
            if (item != null) item.OnlineCount = OnlineCountOf(roomId);

            if (roomId == ActiveRoomId)
            {
                RefreshUsers(roomId);
                AppendMessage(new SystemMessageItem { Text = offline ? $"用户 {leftName} 已离线" : $"用户 {leftName} 离开了房间" });
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

        /// <summary>被踢出房间，本地立刻把他移除。</summary>
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

            // 隔离：当前房间才上屏，别的房间只标红点
            if (chat.RoomId == ActiveRoomId)
            {
                var item = ToChatItem(chat);
                // 去重：join 那会儿同一消息可能历史跟实时广播各来一遍
                if (IsDuplicateMessage(chat.RoomId, item)) return;
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
                if (IsDuplicateMessage(fm.RoomId, item)) return;
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
                if (IsDuplicateMessage(im.RoomId, item)) return;
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

        private void HandleHistoryAck(WsMessage msg)
        {
            _loadingHistory = false;
            var roomId = msg.payload["roomId"]?.ToString();
            if (roomId != ActiveRoomId) return;
            var messages = msg.payload["messages"]?.ToObject<List<HistoryMessage>>() ?? new List<HistoryMessage>();
            if (messages.Count == 0) { _noMoreHistory = true; return; }

            var items = new List<object>();
            foreach (var h in messages)
            {
                var item = ToHistoryItem(h);
                if (item != null) items.Add(item);
            }
            if (items.Count == 0) { _noMoreHistory = true; return; }

            // 倒序头插保持时间升序（顶部最早），头插不触发自动滚底
            if (_roomMessages.TryGetValue(roomId, out var cache))
            {
                for (var i = items.Count - 1; i >= 0; i--)
                    cache.Insert(0, items[i]);
            }
            for (var i = items.Count - 1; i >= 0; i--)
                Messages.Insert(0, items[i]);
            StatusDetail = $"已加载更早的历史（共 {items.Count} 条）";
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

        private void SaveClientConfig()
        {
            _clientConfig.ServerIp = ServerIp;
            _clientConfig.Port = int.TryParse(PortText.Trim(), out var p) ? p : AppConfig.DefaultPort;
            _clientConfig.UserName = UserName;
            _clientConfig.JoinedRoomsByUser[AuthService.GetUserId()] = _savedRooms.ToList();
            JsonStore.Save(AppConfig.ClientConfigPath, _clientConfig);
        }

        private ChatMessageItem ToChatItem(ChatMessage chat)
            => new ChatMessageItem { Data = chat, IsMine = chat.SenderSessionId == MySessionId };

        /// <summary>把统一历史消息还原为可渲染的消息项（文本/文件/图片）。</summary>
        private object ToHistoryItem(HistoryMessage h)
        {
            switch (h.Kind)
            {
                case "file":
                    var fm = h.Payload?.ToObject<FileMessage>();
                    if (fm == null) return null;
                    var fitem = new FileMessageItem { Data = fm, IsMine = fm.SenderSessionId == MySessionId };
                    _fileItems[fm.FileId] = fitem;
                    return fitem;
                case "image":
                    var im = h.Payload?.ToObject<ImageMessage>();
                    if (im == null) return null;
                    var iitem = new ImageMessageItem
                    {
                        Data = im,
                        IsMine = im.SenderSessionId == MySessionId,
                        ImageSource = BuildImageSource(im, IsAuthenticated)
                    };
                    _imageItems[im.FileId] = iitem;
                    return iitem;
                default:
                    var chat = h.Payload?.ToObject<ChatMessage>();
                    return chat == null ? null : ToChatItem(chat);
            }
        }

        /// <summary>当前渲染列表中最早一条真实消息的发送时间（系统消息无时间则跳过）。</summary>
        private DateTime? OldestMessageTime()
        {
            DateTime? oldest = null;
            foreach (var m in Messages)
            {
                var t = MessageTime(m);
                if (t.HasValue && (!oldest.HasValue || t.Value < oldest.Value))
                    oldest = t;
            }
            return oldest;
        }

        private static DateTime? MessageTime(object item)
        {
            if (item is ChatMessageItem cm) return cm.Data?.SentTime;
            if (item is FileMessageItem fm) return fm.Data?.SentTime;
            if (item is ImageMessageItem im) return im.Data?.SentTime;
            return null;
        }

        /// <summary>缓存中是否已存在相同消息（防 join 窗口期 history 与实时广播重叠导致重复）。</summary>
        private bool IsDuplicateMessage(string roomId, object item)
        {
            if (!_roomMessages.TryGetValue(roomId, out var list)) return false;
            if (item is ChatMessageItem cm && cm.Data != null)
                return list.Any(x => x is ChatMessageItem o && o.Data != null
                    && o.Data.SenderSessionId == cm.Data.SenderSessionId
                    && o.Data.SentTime == cm.Data.SentTime
                    && o.Data.Content == cm.Data.Content);
            if (item is FileMessageItem fm && fm.Data != null)
                return list.Any(x => x is FileMessageItem o && o.Data != null && o.Data.FileId == fm.Data.FileId);
            if (item is ImageMessageItem im && im.Data != null)
                return list.Any(x => x is ImageMessageItem o && o.Data != null && o.Data.FileId == im.Data.FileId);
            return false;
        }

        // ---- 工具 ----

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

        /// <summary>未认证/无缩略图时的占位图。</summary>
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
