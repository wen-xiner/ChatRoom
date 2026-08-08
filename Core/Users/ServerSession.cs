using System.Collections.Concurrent;
using Fleck;
using ChatRoom.Core.Net;

namespace ChatRoom.Core.Users
{
    /// <summary>服务器侧的一个 WebSocket 连接会话。</summary>
    public class ServerSession
    {
        public IWebSocketConnection Connection { get; }
        public string ConnectionId { get; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string ClientIp { get; set; }
        /// <summary>是否已通过 Hello 握手（注册用户 ID）。</summary>
        public bool Registered { get; set; }
        /// <summary>当前正在查看的房间号（决定收到完整消息还是只收通知）。</summary>
        public string ActiveRoomId { get; set; }
        /// <summary>已加入的房间号集合。</summary>
        public ConcurrentDictionary<string, byte> JoinedRooms { get; } = new ConcurrentDictionary<string, byte>();

        public ServerSession(IWebSocketConnection conn)
        {
            Connection = conn;
            ConnectionId = conn.ConnectionInfo.Id.ToString();
            ClientIp = conn.ConnectionInfo.ClientIpAddress;
        }

        public bool IsAvailable => Connection != null && Connection.IsAvailable;

        public void Send(WsMessage msg)
        {
            if (!IsAvailable) return;
            try { Connection.Send(msg.ToJson()); } catch { /* 发送失败忽略 */ }
        }
    }
}
