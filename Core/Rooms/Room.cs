using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ChatRoom.Core.Users;
using ChatRoom.Models;

namespace ChatRoom.Core.Rooms
{
    /// <summary>聊天房间。成员集合即消息投递范围（消息隔离的边界）。</summary>
    public class Room
    {
        public string RoomId { get; }
        public string Name { get; set; }
        /// <summary>0000 公共房间：不可关闭、不可删除、不限人数。</summary>
        public bool IsPublic { get; }
        public bool IsClosed { get; private set; }
        /// <summary>人数上限，0 = 不限。</summary>
        public int Capacity { get; set; }

        private readonly ConcurrentDictionary<string, ServerSession> _members =
            new ConcurrentDictionary<string, ServerSession>();
        /// <summary>当前在线成员（连接的会话）。</summary>
        public ICollection<ServerSession> Members => _members.Values;

        private readonly ConcurrentQueue<ChatMessage> _recentMessages = new ConcurrentQueue<ChatMessage>();
        private const int RecentCapacity = 50;

        public int OnlineCount => _members.Count;

        public Room(string roomId, string name, bool isPublic, int capacity = 0)
        {
            RoomId = roomId;
            Name = name;
            IsPublic = isPublic;
            Capacity = capacity;
        }

        public bool AddMember(ServerSession s) => _members.TryAdd(s.ConnectionId, s);

        public bool RemoveMember(ServerSession s) => _members.TryRemove(s.ConnectionId, out _);

        public void Close() => IsClosed = true;

        /// <summary>记录最近消息（加入时下发，仅本房间，历史隔离）。</summary>
        public void PushRecent(ChatMessage m)
        {
            _recentMessages.Enqueue(m);
            while (_recentMessages.Count > RecentCapacity)
                _recentMessages.TryDequeue(out _);
        }

        /// <summary>取最近 n 条消息（只取本房间的）。</summary>
        public List<ChatMessage> GetRecent(int n)
        {
            var all = _recentMessages.ToList();
            return all.Count <= n ? all : all.Skip(all.Count - n).ToList();
        }
    }
}
