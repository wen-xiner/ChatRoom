using System;

namespace ChatRoom.Models
{
    /// <summary>黑名单条目：按用户 ID 或 IP 标识房间。</summary>
    public class BlacklistItem
    {
        public string RoomId { get; set; }
        /// <summary>拉黑的凭据，用户 ID 或 IP。</summary>
        public string Key { get; set; }
        /// <summary>凭据类型："userId" 或 "ip"。</summary>
        public string KeyType { get; set; }
        public DateTime AddedTime { get; set; }
        public bool Active { get; set; } = true;
    }
}
