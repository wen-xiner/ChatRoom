namespace ChatRoom.ViewModels
{
    /// <summary>服务器侧房间成员展示项。</summary>
    public class ServerUserItem
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Ip { get; set; }
        /// <summary>是否在线（离线用户为 false，无 IP）。</summary>
        public bool Online { get; set; }
    }
}
