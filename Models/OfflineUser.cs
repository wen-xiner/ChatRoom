using System;

namespace ChatRoom.Models
{
    /// <summary>
    /// 房间离线用户记录：断开服务器连接 ≠ 退出房间。
    /// 断线用户保留在房间用户列表中并标记离线（灰色状态点），主动退出/被踢才真正移除。
    /// </summary>
    public class OfflineUser
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTime OfflineTime { get; set; }
    }
}
