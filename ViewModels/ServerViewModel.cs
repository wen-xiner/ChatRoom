using System;
using System.Collections.ObjectModel;
using System.Linq;
using ChatRoom.Config;
using ChatRoom.Core.Net;

namespace ChatRoom.ViewModels
{
    /// <summary>服务器面板逻辑：启动/停止、创建/关闭房间、房间列表、状态统计。</summary>
    public class ServerViewModel : ObservableObject
    {
        public ChatServer Server { get; private set; }

        public ObservableCollection<RoomListItem> Rooms { get; } = new ObservableCollection<RoomListItem>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        private const int MaxLogs = 300;

        private string _portText = AppConfig.DefaultPort.ToString();
        public string PortText { get => _portText; set { _portText = value; OnPropertyChanged(); } }

        private string _createRoomId = "";
        public string CreateRoomId { get => _createRoomId; set { _createRoomId = value; OnPropertyChanged(); } }

        private string _createRoomName = "";
        public string CreateRoomName { get => _createRoomName; set { _createRoomName = value; OnPropertyChanged(); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }

        public string StatusText => IsRunning ? $"● 运行中  {Server?.LocalIp}:{PortText}" : "○ 已停止";

        public string ConnectedCountText => $"连接 {Server?.Sessions.ConnectionCount ?? 0}";
        public string OnlineUserText => $"在线用户 {Server?.Sessions.OnlineUserCount ?? 0}";
        public string RoomCountText => $"房间 {Server?.Rooms.Count ?? 0}";

        // ---------------- 操作 ----------------

        public void Start()
        {
            if (IsRunning) return;
            if (!int.TryParse(PortText.Trim(), out var port) || port <= 0 || port > 65535)
            {
                AddLog("端口无效（1~65535）。");
                return;
            }
            Server = new ChatServer(port);
            Server.Log += msg => UiHelper.Dispatch(() => AddLog(msg));
            Server.StateChanged += () => UiHelper.Dispatch(RefreshAll);
            Server.Start();
            IsRunning = true;
            RefreshAll();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            Server?.Stop();
            IsRunning = false;
            RefreshAll();
        }

        /// <summary>创建房间。CreateRoomId 为空则服务器随机生成。</summary>
        public void CreateRoom()
        {
            if (!IsRunning) { AddLog("请先启动服务器。"); return; }
            var id = CreateRoomId.Trim();
            if (Server.CreateRoom(id, CreateRoomName.Trim(), out var roomId, out var error))
            {
                AddLog($"已创建房间 {roomId}（{CreateRoomName.Trim()}）");
                CreateRoomId = "";
                CreateRoomName = "";
            }
            else
            {
                AddLog(error);
            }
            RefreshAll();
        }

        public void CloseRoom(string roomId)
        {
            if (Server == null || string.IsNullOrWhiteSpace(roomId)) return;
            if (Server.CloseRoom(roomId, out var error))
                AddLog($"房间 {roomId} 已关闭");
            else
                AddLog(error);
            RefreshAll();
        }

        // ---------------- 刷新 ----------------

        private void RefreshAll()
        {
            Rooms.Clear();
            if (Server != null)
            {
                foreach (var r in Server.Rooms.AllRooms.OrderBy(x => x.RoomId))
                {
                    Rooms.Add(new RoomListItem
                    {
                        RoomId = r.RoomId,
                        Name = r.Name,
                        OnlineCount = r.OnlineCount,
                        TotalText = r.Capacity == 0 ? "不限" : r.Capacity.ToString(),
                        StatusText = r.IsPublic ? "公共" : (r.IsClosed ? "已关闭" : "运行中"),
                        Room = r
                    });
                }
            }
            OnPropertyChanged(nameof(ConnectedCountText));
            OnPropertyChanged(nameof(OnlineUserText));
            OnPropertyChanged(nameof(RoomCountText));
        }

        private void AddLog(string msg)
        {
            Logs.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
            while (Logs.Count > MaxLogs) Logs.RemoveAt(0);
        }
    }
}
