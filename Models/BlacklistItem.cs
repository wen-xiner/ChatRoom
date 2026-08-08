using System;

namespace ChatRoom.Models
{
    /// <summary>黑名单条目：按用户 ID 或 IP 标识指定房间。</summary>
    public class BlacklistItem
    {
        public string RoomId { get; set; }
        /// <summary>标识值（用户 ID 或 IP）。</summary>
        public string Key { get; set; }
        /// <summary>标识类型："userId" / "ip"。</summary>
        public string KeyType { get; set; }
        public DateTime AddedTime { get; set; }
        public bool Active { get; set; } = true;
    }
}
