using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ChatRoom.Core.Net
{
    /// <summary>
    /// 统一协议外壳：{ type, payload }。
    /// 序列化统一为 camelCase，保证匿名对象与类型化模型（ChatMessage 等）字段名一致。
    /// </summary>
    public class WsMessage
    {
        private static readonly JsonSerializerSettings CamelCaseSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public string type { get; set; }
        public JObject payload { get; set; }

        public static WsMessage Create(WsMessageType type, object payloadObj = null)
        {
            return new WsMessage
            {
                type = type.ToString(),
                payload = payloadObj == null
                    ? new JObject()
                    : JObject.FromObject(payloadObj, JsonSerializer.Create(CamelCaseSettings))
            };
        }

        public string ToJson() => JsonConvert.SerializeObject(this, CamelCaseSettings);

        public static WsMessage FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<WsMessage>(json, CamelCaseSettings);
            }
            catch
            {
                return null;
            }
        }
    }
}
