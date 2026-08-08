using Newtonsoft.Json;

namespace ChatRoom.Models
{
    /// <summary>
    /// 用户展示信息（用户列表用）。
    /// SessionId 用于区分同一台机器上同一 UserId 的多个连接（多实例），去重必须用它。
    /// </summary>
    public class UserInfo
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public bool Online { get; set; }

        /// <summary>是否本人（客户端本地标记，不上网络）。</summary>
        [JsonIgnore]
        public bool IsMe { get; set; }
    }
}
