using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ChatRoom.Config;
using ChatRoom.Core.Net;
using ChatRoom.Core.Storage;
using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>服务器面板逻辑：启动/停止、创建/关闭房间、房间列表、状态统计、踢人/黑名单。</summary>
    public class ServerViewModel : ObservableObject
    {
        public ChatServer Server { get; private set; }

        public ObservableCollection<RoomListItem> Rooms { get; } = new ObservableCollection<RoomListItem>();
        public ObservableCollection<ServerUserItem> RoomMembers { get; } = new ObservableCollection<ServerUserItem>();
        public ObservableCollection<BlacklistItem> RoomBlacklist { get; } = new ObservableCollection<BlacklistItem>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        private const int MaxLogs = 300;

        // 服务器侧选中项（房间/成员/黑名单条目）
        private RoomListItem _selectedRoom;
        private ServerUserItem _selectedUser;
        private BlacklistItem _selectedBlacklist;

        public ServerViewModel()
        {
            var cfg = JsonStore.Load<ServerConfig>(AppConfig.ServerConfigPath);
            _portText = cfg.Port.ToString();
            _limitText = cfg.PublicRoomHourlyLimit.ToString();
        }

        private string _portText = AppConfig.DefaultPort.ToString();
        public string PortText { get => _portText; set { _portText = value; OnPropertyChanged(); } }

        private string _limitText = AppConfig.DefaultPublicRoomHourlyLimit.ToString();
        /// <summary>0000 房间每小时发言上限（0=不限）。</summary>
        public string LimitText { get => _limitText; set { _limitText = value; OnPropertyChanged(); } }

        private string _createRoomId = "";
        public string CreateRoomId { get => _createRoomId; set { _createRoomId = value; OnPropertyChanged(); } }

        private string _createRoomName = "";
        public string CreateRoomName { get => _createRoomName; set { _createRoomName = value; OnPropertyChanged(); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }

        public string StatusText => IsRunning ? $"● 运行中  {Server?.LocalIp}:{PortText}" : "○ 已停止";

        public string ConnectedCountText => $"连接 {Server?.Sessions.ConnectionCount ?? 0}";
        public string OnlineUserText => $"在线用户 {Server?.Sessions.OnlineUserCount ?? 0}";
        public string RoomCountText => $"房间 {(IsRunning ? Server?.Rooms.Count ?? 0 : 0)}";

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
            Server.SaveState();
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

        /// <summary>创建房间。无论是否输入房间号都先弹窗确认，避免误创建。</summary>
        public void CreateRoom()
        {
            if (!IsRunning) { AddLog("请先启动服务器。"); return; }
            var id = CreateRoomId.Trim();
            var name = CreateRoomName.Trim();

            // 创建前统一确认：空房间号说明将随机生成；有输入则显示房间号/名称
            var message = string.IsNullOrWhiteSpace(id)
                ? "未输入房间号，将随机生成一个 4~6 位数字房间号。\n是否继续创建？"
                : (string.IsNullOrWhiteSpace(name)
                    ? $"确认创建房间 {id} 吗？"
                    : $"确认创建房间 {id}（{name}）吗？");
            var confirm = MessageBox.Show(Application.Current?.MainWindow, message, "创建房间",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                AddLog("已取消创建房间。");
                return;
            }

            if (Server.CreateRoom(id, name, out var roomId, out var error))
            {
                AddLog($"已创建房间 {roomId}（{name}）");
                CreateRoomId = "";
                CreateRoomName = "";
                // 创建成功弹窗提示（自定义房间号与随机生成都提示）
                MessageBox.Show(Application.Current?.MainWindow,
                    $"房间 {roomId}（{name}）创建成功。", "创建成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AddLog(error);
                // 创建失败（房间号已存在/格式不符等）弹窗提示，居中于主窗口（服务器界面）
                MessageBox.Show(Application.Current?.MainWindow, error, "创建房间失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // ---------------- 房间详情（成员 / 黑名单 / 踢出 / 拉黑） ----------------

        public RoomListItem SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                _selectedRoom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RoomDetailsTitle));
                RefreshRoomDetails();
            }
        }

        public ServerUserItem SelectedUser { get => _selectedUser; set { _selectedUser = value; OnPropertyChanged(); } }
        public BlacklistItem SelectedBlacklist { get => _selectedBlacklist; set { _selectedBlacklist = value; OnPropertyChanged(); } }

        public string RoomDetailsTitle =>
            SelectedRoom == null ? "未选择房间" : $"房间 #{SelectedRoom.RoomId}（{SelectedRoom.Name}）";

        /// <summary>按选中房间刷新成员列表与黑名单。</summary>
        public void RefreshRoomDetails()
        {
            RoomMembers.Clear();
            RoomBlacklist.Clear();
            if (Server == null || SelectedRoom?.Room == null) return;
            var room = SelectedRoom.Room;
            foreach (var m in room.Members.OrderBy(x => x.UserName))
                RoomMembers.Add(new ServerUserItem { UserId = m.UserId, UserName = m.UserName, Ip = m.ClientIp, Online = true });
            // 离线用户（断开未退出）：每个离线连接都显示（灰点 + "离线"字样）。
            // 不按 UserId 过滤在线会话，避免同机多实例共享 UserId 时断开的连接不显示离线。
            foreach (var o in room.GetOfflineUsers().OrderBy(x => x.UserName))
                RoomMembers.Add(new ServerUserItem { UserId = o.UserId, UserName = o.UserName, Ip = "", Online = false });
            foreach (var b in Server.Blacklist.GetEntries(room.RoomId).OrderByDescending(x => x.AddedTime))
                RoomBlacklist.Add(b);
        }

        public void KickSelectedUser()
        {
            if (Server == null || SelectedRoom == null) { AddLog("请先在房间列表选择房间、再选择要踢出的用户。"); return; }
            var user = SelectedUser;
            if (user == null) { AddLog("请先选中要踢出的成员。"); return; }
            if (Server.KickUser(SelectedRoom.RoomId, user.UserId, out var error))
                AddLog($"已踢出 {user.UserName} 离开房间 {SelectedRoom.RoomId}");
            else
                AddLog(error);
            RefreshRoomDetails();
            RefreshAll();
        }

        public void BanSelectedById() => BanSelected("userId");

        public void BanSelectedByIp() => BanSelected("ip");

        private void BanSelected(string keyType)
        {
            if (Server == null || SelectedRoom == null) { AddLog("请先在房间列表选择房间、再选择要拉黑的用户。"); return; }
            var user = SelectedUser;
            if (user == null) { AddLog("请先选中要拉黑的成员。"); return; }
            var key = keyType == "ip" ? user.Ip : user.UserId;
            if (Server.BanUser(SelectedRoom.RoomId, key, keyType, out var error))
                AddLog($"已将 {key}（{keyType}）拉黑房间 {SelectedRoom.RoomId}");
            else
                AddLog(error);
            RefreshRoomDetails();
            RefreshAll();
        }

        public void UnbanSelected()
        {
            if (Server == null || SelectedRoom == null || SelectedBlacklist == null) return;
            // 先取局部变量：UnbanUser 内部会同步触发 RefreshAll，进而清空黑名单列表并把
            // SelectedBlacklist 置为 null，不能再读取该属性。
            var roomId = SelectedRoom.RoomId;
            var key = SelectedBlacklist.Key;
            if (Server.UnbanUser(roomId, key, out var error))
                AddLog($"已解除 {key} 在房间 {roomId} 的黑名单");
            else
                AddLog(error);
            RefreshRoomDetails();
        }

        /// <summary>设置 0000 房间每小时发言上限并持久化。</summary>
        public void SaveLimit()
        {
            if (Server == null) { AddLog("请先启动服务器。"); return; }
            if (!int.TryParse(LimitText.Trim(), out var limit) || limit < 0)
            {
                AddLog("请输入有效的发言上限（0 = 不限）。");
                return;
            }
            Server.PublicRoomHourlyLimit = limit;
            Server.SaveState();
            AddLog($"已设置 0000 房间发言上限 {limit}/小时");
        }

        // ---------------- 刷新 ----------------

        private void RefreshAll()
        {
            var keepId = SelectedRoom?.RoomId;
            Rooms.Clear();
            // 服务器停止后不再显示任何房间（房间已不对外服务），避免列表残留
            if (Server != null && IsRunning)
            {
                foreach (var r in Server.Rooms.AllRooms.OrderBy(x => x.RoomId))
                {
                    Rooms.Add(new RoomListItem
                    {
                        RoomId = r.RoomId,
                        Name = r.Name,
                        OnlineCount = r.OnlineCount,
                        OfflineCount = r.OfflineCount,
                        TotalText = r.TotalCount.ToString(), // 总人数 = 在线 + 离线
                        TotalTooltip = $"在线 {r.OnlineCount} / 离线 {r.OfflineCount} / 总 {r.TotalCount}",
                        StatusText = r.IsPublic ? "公共" : (r.IsClosed ? "已关闭" : "运行中"),
                        Room = r
                    });
                }
            }
            // 保留房间选中，并刷新其详情（成员/黑名单）
            if (keepId != null)
                SelectedRoom = Rooms.FirstOrDefault(r => r.RoomId == keepId);
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
