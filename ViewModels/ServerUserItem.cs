namespace ChatRoom.ViewModels
{
    /// <summary>服务器侧房间成员展示项。</summary>
    public class ServerUserItem
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Ip { get; set; }
        /// <summary>在不在线（离线的没 IP）。</summary>
        public bool Online { get; set; }
    }
}
