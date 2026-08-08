using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ChatRoom.Models;

namespace ChatRoom.Core.Rooms
{
    /// <summary>
    /// 房间黑名单（按 userId 或 IP 标识）。
    /// 加入房间时据此拒绝；支持查看、解除、JSON 持久化。
    /// </summary>
    public class BlacklistStore
    {
        private readonly ConcurrentDictionary<string, List<BlacklistItem>> _byRoom =
            new ConcurrentDictionary<string, List<BlacklistItem>>();

        /// <summary>指定房间的黑名单条目（用于管理界面）。</summary>
        public List<BlacklistItem> GetEntries(string roomId)
            => _byRoom.TryGetValue(roomId, out var list) ? list.ToList() : new List<BlacklistItem>();

        public IEnumerable<BlacklistItem> AllEntries
            => _byRoom.Values.SelectMany(x => x);

        public void Add(string roomId, string key, string keyType)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var list = _byRoom.GetOrAdd(roomId, _ => new List<BlacklistItem>());
            lock (list)
            {
                if (!list.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new BlacklistItem
                    {
                        RoomId = roomId,
                        Key = key.Trim(),
                        KeyType = string.IsNullOrWhiteSpace(keyType) ? "userId" : keyType.Trim(),
                        AddedTime = DateTime.Now,
                        Active = true
                    });
                }
            }
        }

        /// <summary>解除黑名单（标记失效并移除）。</summary>
        public bool Remove(string roomId, string key)
        {
            if (!_byRoom.TryGetValue(roomId, out var list)) return false;
            lock (list)
            {
                var item = list.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
                if (item == null) return false;
                list.Remove(item);
                item.Active = false;
                return true;
            }
        }

        /// <summary>是否被拉黑：按 userId 或 ip 匹配。</summary>
        public bool IsBanned(string roomId, string userId, string ip)
        {
            if (!_byRoom.TryGetValue(roomId, out var list)) return false;
            lock (list)
            {
                return list.Any(e => e.Active &&
                    (string.Equals(e.Key, userId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(e.Key, ip, StringComparison.OrdinalIgnoreCase)));
            }
        }

        // ---- 持久化 ----

        public List<BlacklistItem> Snapshot() => _byRoom.Values.SelectMany(x => x).ToList();

        public void LoadFrom(IEnumerable<BlacklistItem> items)
        {
            _byRoom.Clear();
            if (items == null) return;
            foreach (var g in items.Where(i => i.Active).GroupBy(i => i.RoomId))
                _byRoom[g.Key] = g.ToList();
        }
    }
}
