using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ChatRoom.Core.Net
{
    /// <summary>
    /// 统一协议外壳：{ type, payload }。
    /// 一律用 camelCase 序列化，匿名对象和 ChatMessage 等模型的字段名才对得上。
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
