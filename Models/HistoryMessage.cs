using Newtonsoft.Json.Linq;

namespace ChatRoom.Models
{
    /// <summary>历史消息统一载体：Kind 区分文本/文件/图片，Payload 是对应消息的 camelCase JSON；图片缩略图不入历史，下发时重生成。</summary>
    public class HistoryMessage
    {
        /// <summary>这条是啥类型："chat" / "file" / "image"。</summary>
        public string Kind { get; set; }

        /// <summary>真正消息的 JSON 内容。</summary>
        public JObject Payload { get; set; }
    }
}
