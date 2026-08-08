using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Fleck;
using ChatRoom.Config;
using ChatRoom.Core.Rooms;
using ChatRoom.Core.Users;
using ChatRoom.Models;

namespace ChatRoom.Core.Net
{
    /// <summary>
    /// WebSocket 聊天服务器（Fleck）。
    /// 职责：连接管理、握手校验（userId 长度）、房间路由与消息隔离、文件/图片存取。
    /// 所有 UI 无关，事件经 Log/StateChanged 上抛，由 ViewModel 转发到 UI 线程。
    /// </summary>
    public class ChatServer : IDisposable
    {
        public int Port { get; }
        public bool IsRunning { get; private set; }
        public string LocalIp { get; }

        /// <summary>0000 房间每小时发言上限（0 = 不限）。</summary>
        public int PublicRoomHourlyLimit { get; set; } = AppConfig.DefaultPublicRoomHourlyLimit;

        public RoomManager Rooms { get; } = new RoomManager();
        public SessionManager Sessions { get; } = new SessionManager();
        public BlacklistStore Blacklist { get; } = new BlacklistStore();

        /// <summary>日志。</summary>
        public event Action<string> Log;
        /// <summary>状态变化（房间/在线人数等），UI 刷新用。</summary>
        public event Action StateChanged;

        private WebSocketServer _server;
        private const int ChunkSize = 256 * 1024; // 每帧 256KB

        private readonly ConcurrentDictionary<string, PendingUpload> _pendingUploads =
            new ConcurrentDictionary<string, PendingUpload>();

        // 0000 发言限流：userId -> 该小时内的发言时间戳
        private readonly object _limitLock = new object();
        private readonly Dictionary<string, List<DateTime>> _publicSpeechTimes =
            new Dictionary<string, List<DateTime>>();

        public ChatServer(int port)
        {
            Port = port;
            LocalIp = GetLocalIpv4();
        }

        // ---------------- 生命周期 ----------------

        public void Start()
        {
            if (IsRunning) return;
            _server = new WebSocketServer($"ws://0.0.0.0:{Port}");
            _server.Start(connection =>
            {
                var session = new ServerSession(connection);
                Sessions.Add(session);

                connection.OnMessage = text => OnText(session, text);
                connection.OnBinary = _ => { /* 本项目统一走 JSON + base64 */ };
                connection.OnClose = () => OnClosed(session);
                connection.OnError = ex => Log?.Invoke($"[{session.UserName}] 连接异常: {ex.Message}");
            });
            IsRunning = true;
            Log?.Invoke($"服务器已启动，监听端口 {Port}（{LocalIp}），0000 公共房间已就绪");
            RaiseStateChanged();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            try { _server?.Dispose(); } catch { }
            _server = null;
            Sessions.Clear();
            _pendingUploads.Clear();
            IsRunning = false;
            Log?.Invoke("服务器已停止");
            RaiseStateChanged();
        }

        public void Dispose() => Stop();

        /// <summary>关闭指定房间并通知成员。返回 false 表示失败（0000 或不存在）。</summary>
        public bool CloseRoom(string roomId, out string error)
        {
            if (!Rooms.TryClose(roomId, out var room, out error)) return false;
            foreach (var m in room.Members.ToList())
            {
                m.JoinedRooms.TryRemove(roomId, out _);
                if (m.ActiveRoomId == roomId) m.ActiveRoomId = null;
                m.Send(WsMessage.Create(WsMessageType.RoomClosed, new { roomId }));
            }
            Log?.Invoke($"房间 {roomId} 已关闭");
            RaiseStateChanged();
            return true;
        }

        /// <summary>创建房间。roomIdOrEmpty 为空则随机生成。</summary>
        public bool CreateRoom(string roomIdOrEmpty, string name, out string roomId, out string error)
        {
            roomId = string.IsNullOrWhiteSpace(roomIdOrEmpty) ? Rooms.GenerateRandomId() : roomIdOrEmpty.Trim();
            if (!Rooms.TryCreate(roomId, name, out _, out error)) return false;
            Log?.Invoke($"已创建房间 {roomId}（{name}）");
            RaiseStateChanged();
            return true;
        }

        // ---------------- 收包分发 ----------------

        private void OnText(ServerSession s, string text)
        {
            var msg = WsMessage.FromJson(text);
            if (msg == null || !Enum.TryParse(msg.type, out WsMessageType t)) return;

            switch (t)
            {
                case WsMessageType.Hello: HandleHello(s, msg); break;
                case WsMessageType.JoinRoom: HandleJoin(s, msg); break;
                case WsMessageType.LeaveRoom: HandleLeave(s, msg); break;
                case WsMessageType.SetActiveRoom: HandleSetActive(s, msg); break;
                case WsMessageType.Chat: HandleChat(s, msg); break;
                case WsMessageType.UploadStart: HandleUploadStart(s, msg); break;
                case WsMessageType.FileChunk: HandleFileChunk(s, msg); break;
                case WsMessageType.FileRequest: HandleFileRequest(s, msg); break;
            }
        }

        private void OnClosed(ServerSession s)
        {
            LeaveAllRooms(s);
            foreach (var kv in _pendingUploads.Where(x => x.Value.SenderConnectionId == s.ConnectionId).ToList())
                _pendingUploads.TryRemove(kv.Key, out _);
            Sessions.Remove(s);
            Log?.Invoke($"{s.UserName} 断开连接");
            RaiseStateChanged();
        }

        // ---------------- 握手 ----------------

        private void HandleHello(ServerSession s, WsMessage msg)
        {
            if (s.Registered) return;
            var userId = msg.payload["userId"]?.ToString();
            var userName = msg.payload["userName"]?.ToString();

            // 服务端只检查用户 ID 长度（应为 20 位）
            if (string.IsNullOrEmpty(userId) || userId.Length != AppConfig.UserIdLength)
            {
                s.Send(WsMessage.Create(WsMessageType.Error, new { reason = "用户 ID 长度不正确（应为 20 位），拒绝连接。" }));
                try { s.Connection.Close(); } catch { }
                Sessions.Remove(s);
                return;
            }

            s.UserId = userId;
            s.UserName = string.IsNullOrWhiteSpace(userName) ? userId.Substring(0, 4) : userName.Trim();
            s.Registered = true;
            // 下发 sessionId：客户端用它区分"本人/他人"（同机多实例共享 UserId）
            s.Send(WsMessage.Create(WsMessageType.HelloAck, new { userId = s.UserId, sessionId = s.ConnectionId }));
            Log?.Invoke($"客户端接入: {s.UserName} ({s.UserId}) @ {s.ClientIp}");
            RaiseStateChanged();
        }

        // ---------------- 房间 ----------------

        private void HandleJoin(ServerSession s, WsMessage msg)
        {
            if (!s.Registered) return;
            var roomId = msg.payload["roomId"]?.ToString();
            if (string.IsNullOrWhiteSpace(roomId)) return;

            var room = Rooms.Get(roomId);
            if (room == null || room.IsClosed)
            {
                s.Send(WsMessage.Create(WsMessageType.JoinAck, new { roomId, ok = false, reason = "房间不存在或已被关闭。" }));
                return;
            }
            if (Blacklist.IsBanned(roomId, s.UserId, s.ClientIp))
            {
                s.Send(WsMessage.Create(WsMessageType.JoinAck, new { roomId, ok = false, reason = "你已被该房间拉入黑名单，无法加入。" }));
                return;
            }
            if (room.Capacity > 0 && room.OnlineCount >= room.Capacity)
            {
                s.Send(WsMessage.Create(WsMessageType.JoinAck, new { roomId, ok = false, reason = "房间人数已满。" }));
                return;
            }

            room.AddMember(s);
            s.JoinedRooms[roomId] = 0;
            if (string.IsNullOrEmpty(s.ActiveRoomId)) s.ActiveRoomId = roomId;

            var users = room.Members
                .Select(m => new UserInfo
                {
                    SessionId = m.ConnectionId,
                    UserId = m.UserId,
                    UserName = m.UserName,
                    Online = true
                })
                .ToList();
            var history = room.GetRecent(AppConfig.HistoryCountOnJoin);

            s.Send(WsMessage.Create(WsMessageType.JoinAck, new { roomId, ok = true, name = room.Name, users, history }));

            // 新用户加入通知（消息隔离：只在房间内广播；带 sessionId 供客户端按会话去重）
            RouteToRoom(room, WsMessage.Create(WsMessageType.UserJoined,
                new
                {
                    roomId,
                    user = new UserInfo { SessionId = s.ConnectionId, UserId = s.UserId, UserName = s.UserName, Online = true }
                }));

            Log?.Invoke($"{s.UserName} 加入房间 {roomId}");
            RaiseStateChanged();
        }

        private void HandleLeave(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (string.IsNullOrWhiteSpace(roomId)) return;
            var room = Rooms.Get(roomId);
            if (room == null) return;

            room.RemoveMember(s);
            s.JoinedRooms.TryRemove(roomId, out _);
            if (s.ActiveRoomId == roomId) s.ActiveRoomId = null;
            RouteToRoom(room, WsMessage.Create(WsMessageType.UserLeft, new { roomId, userId = s.UserId, sessionId = s.ConnectionId }));
            Log?.Invoke($"{s.UserName} 退出房间 {roomId}");
            RaiseStateChanged();
        }

        private void HandleSetActive(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (roomId != null && s.JoinedRooms.ContainsKey(roomId))
            {
                s.ActiveRoomId = roomId;
                RaiseStateChanged();
            }
        }

        private void LeaveAllRooms(ServerSession s)
        {
            foreach (var roomId in s.JoinedRooms.Keys.ToList())
            {
                var room = Rooms.Get(roomId);
                if (room == null) continue;
                room.RemoveMember(s);
                RouteToRoom(room, WsMessage.Create(WsMessageType.UserLeft, new { roomId, userId = s.UserId, sessionId = s.ConnectionId }));
            }
            s.JoinedRooms.Clear();
        }

        // ---------------- 文本聊天 + 消息隔离核心 ----------------

        private void HandleChat(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var content = msg.payload["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(content)) return;

            // 隔离：只能向自己已加入的房间发言
            if (!s.JoinedRooms.ContainsKey(roomId)) return;
            var room = Rooms.Get(roomId);
            if (room == null || room.IsClosed) return;

            // 可选：0000 公共房间发言上限
            if (roomId == AppConfig.PublicRoomId && PublicRoomHourlyLimit > 0 && !TryConsumePublicLimit(s.UserId))
            {
                s.Send(WsMessage.Create(WsMessageType.Error, new { reason = $"发言过于频繁，0000 房间每人每小时上限 {PublicRoomHourlyLimit} 条。" }));
                return;
            }

            var chat = new ChatMessage
            {
                RoomId = roomId,
                SenderUserId = s.UserId,
                SenderName = s.UserName,
                SenderSessionId = s.ConnectionId,
                Content = content.Trim(),
                SentTime = DateTime.Now
            };
            room.PushRecent(chat);
            RouteToRoom(room, WsMessage.Create(WsMessageType.Chat, chat));
        }

        /// <summary>
        /// 消息隔离核心：
        /// 完整消息只发给「当前正在查看该房间」的成员；
        /// 已加入但未在查看的成员只收到 RoomNotify（房间号，无内容）。
        /// 未加入的成员（其他房间用户）完全收不到。
        /// </summary>
        private void RouteToRoom(Room room, WsMessage msg)
        {
            foreach (var m in room.Members.ToList())
            {
                if (!m.IsAvailable) continue;
                if (m.ActiveRoomId == room.RoomId)
                    m.Send(msg);
                else
                    m.Send(WsMessage.Create(WsMessageType.RoomNotify, new { roomId = room.RoomId }));
            }
        }

        // ---------------- 文件 / 图片 ----------------

        private void HandleUploadStart(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var fileId = msg.payload["fileId"]?.ToString();
            var fileName = msg.payload["fileName"]?.ToString();
            var totalChunks = (int)(msg.payload["totalChunks"] ?? 0);
            var isImage = (bool)(msg.payload["isImage"] ?? false);

            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(fileId) ||
                string.IsNullOrWhiteSpace(fileName) || totalChunks <= 0) return;
            if (!s.JoinedRooms.ContainsKey(roomId)) return; // 隔离：只能往自己加入的房间发文件

            _pendingUploads[fileId] = new PendingUpload
            {
                RoomId = roomId,
                FileId = fileId,
                DisplayName = fileName,
                StoredName = SanitizeFileName(fileName),
                IsImage = isImage,
                TotalChunks = totalChunks,
                SenderConnectionId = s.ConnectionId,
                SenderUserId = s.UserId,
                SenderName = s.UserName
            };
        }

        private void HandleFileChunk(ServerSession s, WsMessage msg)
        {
            var fileId = msg.payload["fileId"]?.ToString();
            var dataBase64 = msg.payload["dataBase64"]?.ToString();
            if (string.IsNullOrWhiteSpace(fileId) || string.IsNullOrWhiteSpace(dataBase64)) return;
            if (!_pendingUploads.TryGetValue(fileId, out var up)) return;
            if (up.SenderConnectionId != s.ConnectionId) return;

            var bytes = Convert.FromBase64String(dataBase64);
            up.Chunks.Add(bytes);
            up.ReceivedChunks++;

            if (up.ReceivedChunks >= up.TotalChunks)
            {
                _pendingUploads.TryRemove(fileId, out _);
                FinalizeUpload(up);
            }
        }

        private void FinalizeUpload(PendingUpload up)
        {
            try
            {
                var fileDir = Path.Combine(AppConfig.FileStorageDirectory, up.RoomId, up.FileId);
                AppConfig.EnsureDirectory(fileDir);
                var originalPath = Path.Combine(fileDir, up.StoredName);
                var bytes = up.Combine();
                File.WriteAllBytes(originalPath, bytes);

                if (up.IsImage)
                {
                    var thumbPath = Path.Combine(fileDir, "thumb.jpg");
                    var thumbBytes = CreateThumbnail(originalPath, thumbPath);
                    var size = GetImageSize(originalPath);
                    RouteToRoom(Rooms.Get(up.RoomId), WsMessage.Create(WsMessageType.ImageMeta, new ImageMessage
                    {
                        RoomId = up.RoomId,
                        SenderUserId = up.SenderUserId,
                        SenderName = up.SenderName,
                        SenderSessionId = up.SenderConnectionId,
                        FileId = up.FileId,
                        FileName = up.DisplayName,
                        ThumbBase64 = thumbBytes == null ? "" : Convert.ToBase64String(thumbBytes),
                        Width = size.Item1,
                        Height = size.Item2,
                        SentTime = DateTime.Now
                    }));
                }
                else
                {
                    RouteToRoom(Rooms.Get(up.RoomId), WsMessage.Create(WsMessageType.FileMeta, new FileMessage
                    {
                        RoomId = up.RoomId,
                        SenderUserId = up.SenderUserId,
                        SenderName = up.SenderName,
                        SenderSessionId = up.SenderConnectionId,
                        FileId = up.FileId,
                        FileName = up.DisplayName,
                        FileSize = bytes.Length,
                        SentTime = DateTime.Now
                    }));
                }
                Log?.Invoke($"{up.SenderName} 上传了 {(up.IsImage ? "图片" : "文件")} {up.DisplayName} 到房间 {up.RoomId}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"文件保存失败: {ex.Message}");
            }
        }

        /// <summary>下载请求：{roomId, fileId}，服务端分块回传 FileData。</summary>
        private void HandleFileRequest(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            var fileId = msg.payload["fileId"]?.ToString();
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(fileId)) return;
            if (!s.JoinedRooms.ContainsKey(roomId)) return; // 隔离：只能下载已加入房间的文件

            var fileDir = Path.Combine(AppConfig.FileStorageDirectory, roomId, fileId);
            if (!Directory.Exists(fileDir)) return;

            // 返回原文件（thumb.jpg 是服务端生成的缩略图，不属于用户文件）
            var targetPath = Directory.GetFiles(fileDir)
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("thumb.jpg", StringComparison.OrdinalIgnoreCase));
            if (targetPath == null || !File.Exists(targetPath)) return;

            var name = Path.GetFileName(targetPath);
            var fileBytes = File.ReadAllBytes(targetPath);
            var totalChunks = (int)Math.Ceiling(fileBytes.Length / (double)ChunkSize);
            if (totalChunks == 0) totalChunks = 1;

            for (var i = 0; i < totalChunks; i++)
            {
                var offset = i * ChunkSize;
                var len = Math.Min(ChunkSize, fileBytes.Length - offset);
                var part = new byte[len];
                Array.Copy(fileBytes, offset, part, 0, len);
                s.Send(WsMessage.Create(WsMessageType.FileData, new
                {
                    roomId,
                    fileId,
                    fileName = name,
                    fileSize = fileBytes.Length,
                    chunkIndex = i,
                    totalChunks,
                    dataBase64 = Convert.ToBase64String(part)
                }));
            }
        }

        // ---------------- 0000 发言限流 ----------------

        private bool TryConsumePublicLimit(string userId)
        {
            var now = DateTime.Now;
            lock (_limitLock)
            {
                if (!_publicSpeechTimes.TryGetValue(userId, out var list))
                {
                    list = new List<DateTime>();
                    _publicSpeechTimes[userId] = list;
                }
                list.RemoveAll(t => (now - t).TotalHours >= 1);
                if (list.Count >= PublicRoomHourlyLimit) return false;
                list.Add(now);
                return true;
            }
        }

        // ---------------- 工具 ----------------

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var clean = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N") : clean;
        }

        private static byte[] CreateThumbnail(string imagePath, string thumbPath)
        {
            try
            {
                using (var img = Image.FromFile(imagePath))
                {
                    var maxSide = AppConfig.ImageThumbnailSize;
                    double ratio = (double)img.Width / Math.Max(1, img.Height);
                    int w, h;
                    if (img.Width >= img.Height)
                    {
                        w = maxSide;
                        h = Math.Max(1, (int)(maxSide / ratio));
                    }
                    else
                    {
                        h = maxSide;
                        w = Math.Max(1, (int)(maxSide * ratio));
                    }
                    using (var thumb = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(thumb))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(img, 0, 0, w, h);
                        }
                        thumb.Save(thumbPath, ImageFormat.Jpeg);
                    }
                    return File.ReadAllBytes(thumbPath);
                }
            }
            catch { return null; }
        }

        private static Tuple<int, int> GetImageSize(string imagePath)
        {
            try
            {
                using (var img = Image.FromFile(imagePath))
                    return Tuple.Create(img.Width, img.Height);
            }
            catch { return Tuple.Create(0, 0); }
        }

        private static string GetLocalIpv4()
        {
            try
            {
                var ip = Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip?.ToString() ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        private void RaiseStateChanged() => StateChanged?.Invoke();

        /// <summary>进行中的文件上传（分块暂存）。</summary>
        private class PendingUpload
        {
            public string RoomId;
            public string FileId;
            public string DisplayName;
            public string StoredName;
            public bool IsImage;
            public int TotalChunks;
            public int ReceivedChunks;
            public string SenderConnectionId;
            public string SenderUserId;
            public string SenderName;
            public List<byte[]> Chunks = new List<byte[]>();

            public byte[] Combine()
            {
                using (var ms = new MemoryStream())
                {
                    foreach (var c in Chunks) ms.Write(c, 0, c.Length);
                    return ms.ToArray();
                }
            }
        }
    }
}
