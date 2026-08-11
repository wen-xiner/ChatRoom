using System;

namespace ChatRoom.Models
{
    /// <summary>房间里的离线用户：断了服务器连接不等于退房，断线还在列表里标灰，主动退出或被踢才真正移除。</summary>
    public class OfflineUser
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTime OfflineTime { get; set; }
    }
}
