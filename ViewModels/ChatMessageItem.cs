using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>文本消息展示项，带 IsMine 决定气泡靠左还是靠右。</summary>
    public class ChatMessageItem
    {
        public ChatMessage Data { get; set; }
        public bool IsMine { get; set; }

        public string SenderDisplay => IsMine ? "我" : Data?.SenderName;
    }
}
