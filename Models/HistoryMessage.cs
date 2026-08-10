using Newtonsoft.Json.Linq;

namespace ChatRoom.Models
{
    /// <summary>
    /// 历史消息统一载体：Kind 区分文本/文件/图片，Payload 为对应消息（ChatMessage/FileMessage/ImageMessage）的 camelCase JSON。
    /// 图片消息的 ThumbBase64 不写入历史文件（避免文件膨胀），下发时由服务端从磁盘重新生成。
    /// </summary>
    public class HistoryMessage
    {
        /// <summary>消息类型："chat" / "file" / "image"。</summary>
        public string Kind { get; set; }

        /// <summary>对应消息的 JSON 负载。</summary>
        public JObject Payload { get; set; }
    }
}
