namespace ChatRoom.ViewModels
{
    /// <summary>客户端「已加入房间」列表项。</summary>
    public class JoinedRoomItem : ObservableObject
    {
        public string RoomId { get; set; }
        public string Name { get; set; }

        private int _onlineCount;
        public int OnlineCount
        {
            get => _onlineCount;
            set { _onlineCount = value; OnPropertyChanged(); }
        }

        private bool _hasNewMessage;
        /// <summary>有没有新消息（红点；别的房间只提示，不给内容）。</summary>
        public bool HasNewMessage
        {
            get => _hasNewMessage;
            set { _hasNewMessage = value; OnPropertyChanged(); }
        }

        private bool _available = true;
        /// <summary>房间是否可用（关闭后为不可用）。</summary>
        public bool Available
        {
            get => _available;
            set { _available = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailabilityText)); }
        }

        private bool _joined = true;
        /// <summary>这次是不是真加进去了（恢复的历史房间是 false，点了才变真）。</summary>
        public bool Joined
        {
            get => _joined;
            set { _joined = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailabilityText)); }
        }

        public string AvailabilityText
        {
            get
            {
                if (!Available) return "已关闭";
                return Joined ? "可用" : "未加入";
            }
        }

        public string Header => $"#{RoomId} {Name}（{OnlineCount}人）";

        public override string ToString() => Header;
    }
}
