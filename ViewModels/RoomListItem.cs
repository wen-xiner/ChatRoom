using ChatRoom.Core.Rooms;

namespace ChatRoom.ViewModels
{
    /// <summary>服务器房间列表项。</summary>
    public class RoomListItem
    {
        public string RoomId { get; set; }
        public string Name { get; set; }
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
        public string TotalText { get; set; }
        /// <summary>总人数悬停说明（在线 / 离线 / 总）。</summary>
        public string TotalTooltip { get; set; }
        public string StatusText { get; set; }
        public Room Room { get; set; }
    }
}
