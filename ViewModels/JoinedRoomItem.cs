namespace ChatRoom.ViewModels
{
    /// <summary>客户端已加入房间的列表项。</summary>
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
        /// <summary>是否有新消息（红点；其它房间新消息只提示不含内容）。</summary>
        public bool HasNewMessage
        {
            get => _hasNewMessage;
            set { _hasNewMessage = value; OnPropertyChanged(); }
        }

        private bool _available = true;
        /// <summary>房间是否可用（服务器关闭后不可用）。</summary>
        public bool Available
        {
            get => _available;
            set { _available = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailabilityText)); }
        }

        private bool _joined = true;
        /// <summary>本次是否已真正加入（连接后恢复显示的历史房间为 false，点击后重新加入）。</summary>
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
