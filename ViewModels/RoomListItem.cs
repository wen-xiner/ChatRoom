using ChatRoom.Core.Rooms;

namespace ChatRoom.ViewModels
{
    /// <summary>服务器房间列表项。</summary>
    public class RoomListItem
    {
        public string RoomId { get; set; }
        public string Name { get; set; }
        public int OnlineCount { get; set; }
        public string TotalText { get; set; }
        public string StatusText { get; set; }
        public Room Room { get; set; }
    }
}
