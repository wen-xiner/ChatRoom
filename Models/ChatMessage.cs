using System;

namespace ChatRoom.Models
{
    /// <summary>文本消息。</summary>
    public class ChatMessage
    {
        public string RoomId { get; set; }
        public string SenderUserId { get; set; }
        public string SenderName { get; set; }
        /// <summary>发送者会话 ID（客户端据此判断是否本人）。</summary>
        public string SenderSessionId { get; set; }
        public string Content { get; set; }
        public DateTime SentTime { get; set; }
    }
}
