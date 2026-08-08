using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ChatRoom.Core.Rooms
{
    /// <summary>
    /// 房间黑名单（按 userId 或 IP 标识）。加入房间时据此拒绝。
    /// 模块8 的黑名单管理界面在后续阶段实现，此处先提供数据与校验。
    /// </summary>
    public class BlacklistStore
    {
        private readonly ConcurrentDictionary<string, HashSet<string>> _byRoom =
            new ConcurrentDictionary<string, HashSet<string>>();

        public void Add(string roomId, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _byRoom.GetOrAdd(roomId, _ => new HashSet<string>()).Add(Normalize(key));
        }

        public bool Remove(string roomId, string key)
        {
            if (!_byRoom.TryGetValue(roomId, out var set)) return false;
            lock (set) { return set.Remove(Normalize(key)); }
        }

        public bool IsBanned(string roomId, string userId, string ip)
        {
            if (!_byRoom.TryGetValue(roomId, out var set)) return false;
            lock (set)
            {
                return set.Contains(Normalize(userId)) || set.Contains(Normalize(ip));
            }
        }

        private static string Normalize(string key)
            => (key ?? "").Trim().ToUpperInvariant();
    }
}
