using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChatRoom.Core.Users
{
    /// <summary>连接会话集合；提供按 userId 去重的在线用户数。</summary>
    public class SessionManager
    {
        private readonly ConcurrentDictionary<string, ServerSession> _byConnection =
            new ConcurrentDictionary<string, ServerSession>();

        public void Add(ServerSession s) => _byConnection[s.ConnectionId] = s;

        public void Remove(ServerSession s) => _byConnection.TryRemove(s.ConnectionId, out _);

        public void Clear() => _byConnection.Clear();

        public ServerSession GetByConnectionId(string connectionId)
            => _byConnection.TryGetValue(connectionId, out var s) ? s : null;

        /// <summary>把当前所有会话打个快照（停服时逐个关连接，遍历不怕并发增删）。</summary>
        public List<ServerSession> Snapshot() => _byConnection.Values.ToList();

        public int ConnectionCount => _byConnection.Count;

        /// <summary>在线用户总数，按 userId 去重。</summary>
        public int OnlineUserCount =>
            _byConnection.Values
                .Where(x => x.Registered && !string.IsNullOrEmpty(x.UserId))
                .Select(x => x.UserId)
                .Distinct()
                .Count();
    }
}
