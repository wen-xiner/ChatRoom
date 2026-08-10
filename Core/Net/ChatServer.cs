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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ChatRoom.Config;
using ChatRoom.Core.Rooms;
using ChatRoom.Core.Storage;
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

        /// <summary>文件/图片存储目录（server-config.json 可配，空则默认 Data\Files）。</summary>
        public string FileStorageDirectory { get; private set; } = AppConfig.FileStorageDirectory;

        /// <summary>连接过/见过的用户（userId → 记录）。</summary>
        public IReadOnlyCollection<UserRecord> KnownUsers => _users.Values.ToList();

        /// <summary>日志。</summary>
        public event Action<string> Log;
        /// <summary>状态变化（房间/在线人数等），UI 刷新用。</summary>
        public event Action StateChanged;

        private WebSocketServer _server;
        private const int ChunkSize = 256 * 1024; // 每帧 256KB

        /// <summary>历史消息负载序列化：与协议一致用 camelCase，保证下发后客户端字段名匹配。</summary>
        private static readonly JsonSerializer CamelCaseSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });

        private readonly ConcurrentDictionary<string, PendingUpload> _pendingUploads =
            new ConcurrentDictionary<string, PendingUpload>();

        private readonly ConcurrentDictionary<string, UserRecord> _users =
            new ConcurrentDictionary<string, UserRecord>(StringComparer.OrdinalIgnoreCase);

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
            LoadPersistentState();
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
            SaveState();
            // 主动关闭所有客户端连接：Fleck.Dispose 只关闭监听 socket，已建立的连接不会被断开，
            // 否则客户端仍显示"已连接"，且消息收发继续被处理。
            foreach (var s in Sessions.Snapshot())
            {
                try { s.Connection.Close(); } catch { }
            }
            try { _server?.Dispose(); } catch { }
            _server = null;
            Sessions.Clear();
            _pendingUploads.Clear();
            IsRunning = false;
            Log?.Invoke("服务器已停止");
            RaiseStateChanged();
        }

        /// <summary>立即持久化配置/房间/用户/黑名单（限流值调整等场景调用）。</summary>
        public void SaveState()
        {
            JsonStore.Save(AppConfig.ServerConfigPath, new ServerConfig
            {
                Port = Port,
                PublicRoomHourlyLimit = PublicRoomHourlyLimit,
                FileStoragePath = FileStorageDirectory
            });
            var rooms = Rooms.AllRooms
                .Where(r => !r.IsPublic)
                .Select(r => new RoomRecord { RoomId = r.RoomId, Name = r.Name })
                .ToList();
            JsonStore.Save(AppConfig.RoomsConfigPath, rooms);
            JsonStore.Save(AppConfig.UsersConfigPath, _users.Values.OrderBy(u => u.UserId).ToList());
            var roomUsers = Rooms.AllRooms
                .Select(r => new RoomUsersRecord
                {
                    RoomId = r.RoomId,
                    RoomName = r.Name,
                    Users = r.Members
                        .Select(m => new UserRecord
                        {
                            UserId = m.UserId,
                            UserName = m.UserName,
                            LastIp = m.ClientIp,
                            LastSeen = DateTime.Now
                        })
                        .ToList()
                })
                .ToList();
            JsonStore.Save(AppConfig.RoomUsersConfigPath, roomUsers);
            JsonStore.Save(AppConfig.BlacklistConfigPath, Blacklist.Snapshot());
        }

        private void LoadPersistentState()
        {
            try
            {
                var cfg = JsonStore.Load<ServerConfig>(AppConfig.ServerConfigPath);
                PublicRoomHourlyLimit = cfg.PublicRoomHourlyLimit;
                FileStorageDirectory = string.IsNullOrWhiteSpace(cfg.FileStoragePath)
                    ? AppConfig.FileStorageDirectory
                    : cfg.FileStoragePath;
                AppConfig.EnsureDirectory(FileStorageDirectory);

                foreach (var r in JsonStore.Load<List<RoomRecord>>(AppConfig.RoomsConfigPath))
                    if (r.RoomId != AppConfig.PublicRoomId)
                        Rooms.TryCreate(r.RoomId, r.Name, out _, out _);

                foreach (var u in JsonStore.Load<List<UserRecord>>(AppConfig.UsersConfigPath))
                    if (!string.IsNullOrWhiteSpace(u.UserId))
                        _users[u.UserId] = u;

                // 加载房间+用户列表（校验文件可读，成员随连接实时变化）
                JsonStore.Load<List<RoomUsersRecord>>(AppConfig.RoomUsersConfigPath);

                Blacklist.LoadFrom(JsonStore.Load<List<BlacklistItem>>(AppConfig.BlacklistConfigPath));

                if (Directory.Exists(AppConfig.HistoryDirectory))
                {
                    foreach (var file in Directory.GetFiles(AppConfig.HistoryDirectory, "*.json"))
                    {
                        var roomId = Path.GetFileNameWithoutExtension(file);
                        var room = Rooms.Get(roomId);
                        if (room == null) continue;
                        foreach (var m in JsonStore.Load<List<HistoryMessage>>(file))
                        {
                            if (m == null || string.IsNullOrEmpty(m.Kind) || m.Payload == null) continue;
                            room.PushRecent(m);
                        }
                    }
                }
            }
            catch { /* 状态加载失败不阻断启动 */ }
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
            SaveState();
            Log?.Invoke($"房间 {roomId} 已关闭");
            RaiseStateChanged();
            return true;
        }

        /// <summary>创建房间。roomIdOrEmpty 为空则随机生成。</summary>
        public bool CreateRoom(string roomIdOrEmpty, string name, out string roomId, out string error)
        {
            roomId = string.IsNullOrWhiteSpace(roomIdOrEmpty) ? Rooms.GenerateRandomId() : roomIdOrEmpty.Trim();
            if (!Rooms.TryCreate(roomId, name, out _, out error)) return false;
            SaveState();
            Log?.Invoke($"已创建房间 {roomId}（{name}）");
            RaiseStateChanged();
            return true;
        }

        // ---------------- 模块8：踢出 / 黑名单 ----------------

        /// <summary>把指定房间中的用户（按 userId 或 IP）踢出。执行后立即离开该房间。</summary>
        public bool KickUser(string roomId, string userIdOrIp, out string error)
        {
            error = null;
            var room = Rooms.Get(roomId);
            if (room == null) { error = "房间不存在。"; return false; }
            var target = room.Members.FirstOrDefault(m =>
                string.Equals(m.UserId, userIdOrIp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.ClientIp, userIdOrIp, StringComparison.OrdinalIgnoreCase));
            if (target == null) { error = $"房间 {roomId} 中未找到该用户。"; return false; }

            room.RemoveMember(target);
            target.JoinedRooms.TryRemove(roomId, out _);
            if (target.ActiveRoomId == roomId) target.ActiveRoomId = null;

            target.Send(WsMessage.Create(WsMessageType.Kick, new { roomId, reason = "你已被服务器移出房间。" }));
            RouteToRoom(room, WsMessage.Create(WsMessageType.UserLeft, new { roomId, userId = target.UserId, sessionId = target.ConnectionId }));

            Log?.Invoke($"已踢出 {target.UserName}（{target.UserId}）离开房间 {roomId}");
            RaiseStateChanged();
            return true;
        }

        /// <summary>把用户（按 userId 或 IP）拉黑指定房间；若当前在房间内同时踢出。其他房间不受影响。</summary>
        public bool BanUser(string roomId, string key, string keyType, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(key)) { error = "标识不能为空。"; return false; }
            if (Rooms.Get(roomId) == null) { error = "房间不存在。"; return false; }

            Blacklist.Add(roomId, key, keyType);
            // 若该用户正在房间内，一并踢出
            var room = Rooms.Get(roomId);
            var target = room?.Members.FirstOrDefault(m =>
                string.Equals(m.UserId, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.ClientIp, key, StringComparison.OrdinalIgnoreCase));
            if (target != null)
                KickUser(roomId, key, out _);

            SaveState();
            Log?.Invoke($"已将 {key}（{keyType}）拉黑房间 {roomId}");
            RaiseStateChanged();
            return true;
        }

        /// <summary>解除指定房间的黑名单。</summary>
        public bool UnbanUser(string roomId, string key, out string error)
        {
            error = null;
            if (!Blacklist.Remove(roomId, key)) { error = "黑名单中不存在该条目。"; return false; }
            SaveState();
            Log?.Invoke($"已解除 {key} 在房间 {roomId} 的黑名单");
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
                case WsMessageType.HistoryRequest: HandleHistoryRequest(s, msg); break;
            }
        }

        /// <summary>记录/更新用户（users.json 持久化）。</summary>
        private void RecordUser(ServerSession s)
        {
            var now = DateTime.Now;
            _users.AddOrUpdate(s.UserId,
                _ => new UserRecord { UserId = s.UserId, UserName = s.UserName, LastIp = s.ClientIp, FirstSeen = now, LastSeen = now },
                (_, old) =>
                {
                    old.UserName = s.UserName;
                    old.LastIp = s.ClientIp;
                    old.LastSeen = now;
                    return old;
                });
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
            RecordUser(s);
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
            FillHistoryThumbs(history); // 图片历史补缩略图（历史文件不存 base64，此处从磁盘生成）

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
            room.PushRecent(ToHistory("chat", chat));
            PersistHistory(room);
            RouteToRoom(room, WsMessage.Create(WsMessageType.Chat, chat));
        }

        /// <summary>把房间消息缓冲写入独立历史文件（按房间隔离）。</summary>
        private void PersistHistory(Room room)
        {
            var path = Path.Combine(AppConfig.HistoryDirectory, room.RoomId + ".json");
            JsonStore.Save(path, room.GetRecent(AppConfig.HistoryKeepCount));
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
                var fileDir = Path.Combine(FileStorageDirectory, up.RoomId, up.FileId);
                AppConfig.EnsureDirectory(fileDir);
                var originalPath = Path.Combine(fileDir, up.StoredName);
                var bytes = up.Combine();
                File.WriteAllBytes(originalPath, bytes);

                var room = Rooms.Get(up.RoomId);
                if (up.IsImage)
                {
                    var thumbPath = Path.Combine(fileDir, "thumb.jpg");
                    var thumbBytes = CreateThumbnail(originalPath, thumbPath);
                    var size = GetImageSize(originalPath);
                    var imageMsg = new ImageMessage
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
                    };
                    RouteToRoom(room, WsMessage.Create(WsMessageType.ImageMeta, imageMsg));
                    // 历史不存缩略图（避免历史文件膨胀），下发时由服务端从磁盘重新生成
                    imageMsg.ThumbBase64 = null;
                    room.PushRecent(ToHistory("image", imageMsg));
                }
                else
                {
                    var fileMsg = new FileMessage
                    {
                        RoomId = up.RoomId,
                        SenderUserId = up.SenderUserId,
                        SenderName = up.SenderName,
                        SenderSessionId = up.SenderConnectionId,
                        FileId = up.FileId,
                        FileName = up.DisplayName,
                        FileSize = bytes.Length,
                        SentTime = DateTime.Now
                    };
                    RouteToRoom(room, WsMessage.Create(WsMessageType.FileMeta, fileMsg));
                    room.PushRecent(ToHistory("file", fileMsg));
                }
                if (room != null) PersistHistory(room);
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

            var fileDir = Path.Combine(FileStorageDirectory, roomId, fileId);
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

        // ---------------- 历史消息：统一载体 / 分页加载 ----------------

        /// <summary>把消息模型包装成历史统一载体（camelCase 负载，与协议字段一致）。</summary>
        private static HistoryMessage ToHistory(string kind, object msg)
            => new HistoryMessage { Kind = kind, Payload = JObject.FromObject(msg, CamelCaseSerializer) };

        /// <summary>取历史消息的发送时间（负载缺失时返回最小值）。</summary>
        private static DateTime GetSentTime(HistoryMessage m)
        {
            var raw = m?.Payload?["sentTime"]?.ToString();
            return DateTime.TryParse(raw, out var t) ? t : DateTime.MinValue;
        }

        /// <summary>从历史文件读取该房间全部持久化消息。</summary>
        private List<HistoryMessage> LoadHistoryFromFile(Room room)
        {
            var path = Path.Combine(AppConfig.HistoryDirectory, room.RoomId + ".json");
            if (!File.Exists(path)) return new List<HistoryMessage>();
            return JsonStore.Load<List<HistoryMessage>>(path);
        }

        /// <summary>
        /// 给历史中的图片消息补缩略图：历史文件不存 base64，下发时从磁盘 thumb.jpg 重新生成。
        /// 在副本上填充，不污染内存中的历史（避免缩略图被再次写入历史文件）。
        /// </summary>
        private void FillHistoryThumbs(List<HistoryMessage> history)
        {
            for (var i = 0; i < history.Count; i++)
            {
                var h = history[i];
                if (h.Kind != "image" || h.Payload == null) continue;
                var roomId = h.Payload["roomId"]?.ToString();
                var fileId = h.Payload["fileId"]?.ToString();
                if (string.IsNullOrEmpty(fileId)) continue;
                var thumbPath = Path.Combine(FileStorageDirectory, roomId ?? "", fileId, "thumb.jpg");
                if (!File.Exists(thumbPath)) continue;
                try
                {
                    var clone = (JObject)h.Payload.DeepClone();
                    clone["thumbBase64"] = Convert.ToBase64String(File.ReadAllBytes(thumbPath));
                    history[i] = new HistoryMessage { Kind = h.Kind, Payload = clone };
                }
                catch { /* 缩略图读取失败则保持占位图 */ }
            }
        }

        /// <summary>客户端向上滚动请求更早历史：按时间游标（beforeTime）分页返回。</summary>
        private void HandleHistoryRequest(ServerSession s, WsMessage msg)
        {
            var roomId = msg.payload["roomId"]?.ToString();
            if (string.IsNullOrWhiteSpace(roomId) || !s.JoinedRooms.ContainsKey(roomId)) return;
            var room = Rooms.Get(roomId);
            if (room == null) return;

            var beforeRaw = msg.payload["beforeTime"]?.ToString();
            var before = DateTime.TryParse(beforeRaw, out var bt) ? bt : DateTime.MaxValue;

            var older = LoadHistoryFromFile(room)
                .Where(m => GetSentTime(m) < before)
                .OrderByDescending(GetSentTime)
                .Take(AppConfig.HistoryPageSize)
                .OrderBy(GetSentTime)
                .ToList();
            FillHistoryThumbs(older);
            s.Send(WsMessage.Create(WsMessageType.HistoryAck, new { roomId, messages = older }));
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
