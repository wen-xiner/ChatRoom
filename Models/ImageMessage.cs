using System;

namespace ChatRoom.Models
{
    /// <summary>图片消息（缩略图 base64 随消息下发，未认证显示占位图；原图按需下载）。</summary>
    public class ImageMessage
    {
        public string RoomId { get; set; }
        public string SenderUserId { get; set; }
        public string SenderName { get; set; }
        /// <summary>发送者会话 ID（客户端据此判断是否本人）。</summary>
        public string SenderSessionId { get; set; }
        public string FileId { get; set; }
        public string FileName { get; set; }
        public string ThumbBase64 { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime SentTime { get; set; }
    }
}
