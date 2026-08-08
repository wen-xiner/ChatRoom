using System;

namespace ChatRoom.Models
{
    /// <summary>文件消息（下载状态由客户端本地维护，未认证不可下载）。</summary>
    public class FileMessage
    {
        public string RoomId { get; set; }
        public string SenderUserId { get; set; }
        public string SenderName { get; set; }
        /// <summary>发送者会话 ID（客户端据此判断是否本人）。</summary>
        public string SenderSessionId { get; set; }
        public string FileId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public DateTime SentTime { get; set; }
    }
}
