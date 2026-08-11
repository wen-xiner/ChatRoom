using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ChatRoom.Core.Users;
using ChatRoom.Models;

namespace ChatRoom.Core.Rooms
{
    /// <summary>聊天房间：成员集合即消息投递范围（消息隔离边界）。</summary>
    public class Room
    {
        public string RoomId { get; }
        public string Name { get; set; }
        /// <summary>0000 公共房间：不可关闭删除、不限人数。</summary>
        public bool IsPublic { get; }
        public bool IsClosed { get; private set; }
        /// <summary>能装多少人，0 就是不限。</summary>
        public int Capacity { get; set; }

        private readonly ConcurrentDictionary<string, ServerSession> _members =
            new ConcurrentDictionary<string, ServerSession>();
        /// <summary>现在在线的人（连着会话的）。</summary>
        public ICollection<ServerSession> Members => _members.Values;

        /// <summary>最近离线用户（断开但未退出，保留列表并标记离线）。</summary>
        private readonly List<OfflineUser> _offlineUsers = new List<OfflineUser>();
        private const int MaxOffline = 50;

        private readonly ConcurrentQueue<HistoryMessage> _recentMessages = new ConcurrentQueue<HistoryMessage>();
        /// <summary>历史消息缓存条数，跟存盘的条数一致，保证写盘完整。</summary>
        private const int RecentCapacity = 200;

        public int OnlineCount => _members.Count;

        /// <summary>离线人数（断开但还没退的）。</summary>
        public int OfflineCount
        {
            get { lock (_offlineUsers) return _offlineUsers.Count; }
        }

        /// <summary>房间总人数 = 在线 + 离线。</summary>
        public int TotalCount => OnlineCount + OfflineCount;

        public Room(string roomId, string name, bool isPublic, int capacity = 0)
        {
            RoomId = roomId;
            Name = name;
            IsPublic = isPublic;
            Capacity = capacity;
        }

        public bool AddMember(ServerSession s) => _members.TryAdd(s.ConnectionId, s);

        public bool RemoveMember(ServerSession s) => _members.TryRemove(s.ConnectionId, out _);

        /// <summary>该用户是否还有在线会话在房间（多实例下同 userId 多个 session）。</summary>
        public bool HasOnline(string userId)
            => _members.Values.Any(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));

        /// <summary>记一笔离线用户，按 userId 去重并刷新时间。</summary>
        public void AddOffline(string userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            lock (_offlineUsers)
            {
                var now = DateTime.Now;
                var existing = _offlineUsers.FirstOrDefault(o => string.Equals(o.UserId, userId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.UserName = userName;
                    existing.OfflineTime = now;
                }
                else
                {
                    _offlineUsers.Add(new OfflineUser { UserId = userId, UserName = userName, OfflineTime = now });
                }
                // 离线列表别放太多，超出上限就丢掉最旧的
                while (_offlineUsers.Count > MaxOffline)
                    _offlineUsers.RemoveAt(0);
            }
        }

        /// <summary>移除离线记录（重进/主动退出时调用）。</summary>
        public void RemoveOffline(string userId)
        {
            lock (_offlineUsers)
            {
                _offlineUsers.RemoveAll(o => string.Equals(o.UserId, userId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>把离线用户列表拿出来（给服务器端成员管理展示用）。</summary>
        public List<OfflineUser> GetOfflineUsers()
        {
            lock (_offlineUsers) return _offlineUsers.ToList();
        }

        /// <summary>房间用户列表：在线每个会话一条，离线按 userId 一条；已在线就不重复显示离线记录。</summary>
        public List<UserInfo> GetUsers()
        {
            var users = new List<UserInfo>();
            foreach (var m in _members.Values)
                users.Add(new UserInfo { SessionId = m.ConnectionId, UserId = m.UserId, UserName = m.UserName, Online = true });
            var onlineIds = new HashSet<string>(_members.Values.Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
            lock (_offlineUsers)
            {
                foreach (var o in _offlineUsers)
                {
                    if (onlineIds.Contains(o.UserId)) continue;
                    users.Add(new UserInfo { SessionId = "", UserId = o.UserId, UserName = o.UserName, Online = false });
                }
            }
            return users;
        }

        public void Close() => IsClosed = true;

        /// <summary>记录最近消息（文本/文件/图片统一载体，仅本房间，历史隔离）。</summary>
        public void PushRecent(HistoryMessage m)
        {
            _recentMessages.Enqueue(m);
            while (_recentMessages.Count > RecentCapacity)
                _recentMessages.TryDequeue(out _);
        }

        /// <summary>取最近 n 条消息，只拿本房间的。</summary>
        public List<HistoryMessage> GetRecent(int n)
        {
            var all = _recentMessages.ToList();
            return all.Count <= n ? all : all.Skip(all.Count - n).ToList();
        }
    }
}
