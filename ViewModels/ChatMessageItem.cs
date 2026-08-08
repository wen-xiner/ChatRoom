using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>文本消息展示项（附加 IsMine，用于气泡左右方向与配色）。</summary>
    public class ChatMessageItem
    {
        public ChatMessage Data { get; set; }
        public bool IsMine { get; set; }

        public string SenderDisplay => IsMine ? "我" : Data?.SenderName;
    }
}
