using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatRoom.Core.Auth;
using ChatRoom.Core.Storage;
using ChatRoom.ViewModels;

namespace ChatRoom
{
    /// <summary>主窗口：切换运行模式、接线服务器/客户端面板事件。逻辑都在 ViewModels，这里只转发 UI 事件。</summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            Loaded += MainWindow_Loaded;
            _vm.Client.Messages.CollectionChanged += Messages_CollectionChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyUiConfig();
            RefreshAuthStatus();
        }

        /// <summary>读取 UI 配置并应用窗口大小与列宽。</summary>
        private void ApplyUiConfig()
        {
            var ui = JsonStore.Load<UiConfig>(ChatRoom.Config.AppConfig.UiConfigPath);
            if (ui.WindowWidth >= MinWidth && ui.WindowHeight >= MinHeight)
            {
                Width = ui.WindowWidth;
                Height = ui.WindowHeight;
            }
            // 固定列限制在合理范围，名称列自适应，防止拖太宽把面板撑爆。
            ColRoomId.Width = Clamp(ui.ColRoomId, 48, 80);
            ColName.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            ColOnline.Width = Clamp(ui.ColOnline, 36, 50);
            ColTotal.Width = Clamp(ui.ColTotal, 40, 60);
            ColStatus.Width = Clamp(ui.ColStatus, 40, 60);
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        private void SaveUiConfig()
        {
            var ui = new UiConfig
            {
                WindowWidth = ActualWidth,
                WindowHeight = ActualHeight,
                ColRoomId = ColRoomId.ActualWidth,
                ColName = ColName.ActualWidth,
                ColOnline = ColOnline.ActualWidth,
                ColTotal = ColTotal.ActualWidth,
                ColStatus = ColStatus.ActualWidth
            };
            JsonStore.Save(ChatRoom.Config.AppConfig.UiConfigPath, ui);
        }

        private void RefreshAuthStatus()
        {
            TxtAuthStatus.Text = AuthService.IsAuthenticated()
                ? "认证状态：已认证"
                : "认证状态：未认证（图片/文件收发受限）";
        }

        // ---- 运行模式 ----

        private void RbClient_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Client;
        private void RbServer_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Server;
        private void RbBoth_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Both;

        // ---- 服务器 ----

        private void BtnStartServer_Click(object sender, RoutedEventArgs e) => _vm.Server.Start();

        private void BtnStopServer_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmDanger(this, "确定要关闭服务器吗？\n所有已连接的客户端将被断开。", "确认关闭服务器"))
                _vm.Server.Stop();
        }

        private void BtnCreateRoom_Click(object sender, RoutedEventArgs e) => _vm.Server.CreateRoom();

        private void BtnCloseRoom_Click(object sender, RoutedEventArgs e)
        {
            var item = DgvRooms.SelectedItem as RoomListItem;
            if (item == null)
            {
                MessageBox.Show(this, "请先在房间列表中选择要关闭的房间。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要关闭房间 {item.RoomId}（{item.Name}）吗？\n房间内所有成员将被移出。", "确认关闭房间"))
                _vm.Server.CloseRoom(item.RoomId);
        }

        // ---- 客户端 ----

        private void BtnConnect_Click(object sender, RoutedEventArgs e) => _vm.Client.Connect();

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmDanger(this, "确定要断开连接吗？\n断开后你在房间内的状态将变为离线（不会退出房间）。", "确认断开连接"))
                _vm.Client.Disconnect();
        }

        private void BtnJoinRoom_Click(object sender, RoutedEventArgs e) => _vm.Client.JoinRoom();

        private void BtnLeaveRoom_Click(object sender, RoutedEventArgs e)
        {
            var roomId = _vm.Client.ActiveRoomId;
            if (string.IsNullOrEmpty(roomId))
            {
                MessageBox.Show(this, "当前没有正在查看的房间。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要退出房间 {roomId} 吗？", "确认退出房间"))
                _vm.Client.LeaveCurrentRoom();
        }

        private void LstJoinedRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstJoinedRooms.SelectedItem is JoinedRoomItem item)
            {
                // 历史恢复的条目点了就是加入，已加入的点了就是切换查看
                if (item.Joined)
                    _vm.Client.SetActiveRoom(item.RoomId);
                else
                    _vm.Client.JoinRoom(item.RoomId);
            }
        }

        private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _vm.Client.SendText();
            }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => _vm.Client.SendText();

        private void BtnSendFile_Click(object sender, RoutedEventArgs e) => _vm.Client.SendFile();

        private void BtnSendImage_Click(object sender, RoutedEventArgs e) => _vm.Client.SendImage();

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            _vm.Client.ShowRegistration();
            RefreshAuthStatus();
        }

        // ---- 消息模板事件 ----

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is FileMessageItem item)
                _vm.Client.DownloadFile(item);
        }

        private void ImgView_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ImageMessageItem item)
                _vm.Client.ViewImage(item);
        }

        // ---- 消息自动滚动 ----

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // 头插加载更早历史时不自动滚底，保持浏览位置
                if (e.NewStartingIndex >= 0 && e.NewStartingIndex < LstMessages.Items.Count - 1)
                    return;
                // 等容器布局好再滚到底，CollectionChanged 时容器可能还没生成，立即滚会失效
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var scroll = FindVisualChild<ScrollViewer>(LstMessages);
                    if (scroll != null)
                        scroll.ScrollToEnd();
                    else if (LstMessages.Items.Count > 0)
                        LstMessages.ScrollIntoView(LstMessages.Items[LstMessages.Items.Count - 1]);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void LstMessages_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 滚到顶部时加载更早历史
            if (e.VerticalOffset <= 0)
                _vm.Client.LoadMoreHistory();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var inner = FindVisualChild<T>(child);
                if (inner != null) return inner;
            }
            return null;
        }

        // ---- 服务器面板：整区滚轮滚动 ----

        private void ServerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 鼠标在面板任意位置都能滚整个面板，不必非把鼠标放到右侧滚动条上。
            if (!(sender is ScrollViewer sv)) return;
            var lines = SystemParameters.WheelScrollLines;
            if (lines < 0)
            {
                if (e.Delta > 0) sv.PageUp();
                else sv.PageDown();
            }
            else
            {
                for (var i = 0; i < lines; i++)
                {
                    if (e.Delta > 0) sv.LineUp();
                    else sv.LineDown();
                }
            }
            e.Handled = true;
        }

        // ---- 服务器房间详情：踢人 / 黑名单 ----

        private void LstRoomMembers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.Server.SelectedUser = LstRoomMembers.SelectedItem as ServerUserItem;
        }

        private void LstRoomBlacklist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.Server.SelectedBlacklist = LstRoomBlacklist.SelectedItem as ChatRoom.Models.BlacklistItem;
        }

        private void BtnKick_Click(object sender, RoutedEventArgs e)
        {
            var user = _vm.Server.SelectedUser;
            if (user == null)
            {
                MessageBox.Show(this, "请先在成员列表中选择要踢出的用户。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要踢出用户 {user.UserName}（{user.UserId}）吗？", "确认踢出"))
                _vm.Server.KickSelectedUser();
        }

        private void BtnBanId_Click(object sender, RoutedEventArgs e)
        {
            var user = _vm.Server.SelectedUser;
            if (user == null)
            {
                MessageBox.Show(this, "请先在成员列表中选择要拉黑的用户。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要将用户 {user.UserName}（ID {user.UserId}）拉黑当前房间吗？", "确认拉黑"))
                _vm.Server.BanSelectedById();
        }

        private void BtnBanIp_Click(object sender, RoutedEventArgs e)
        {
            var user = _vm.Server.SelectedUser;
            if (user == null)
            {
                MessageBox.Show(this, "请先在成员列表中选择要拉黑的用户。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要将 IP {user.Ip} 拉黑当前房间吗？", "确认拉黑"))
                _vm.Server.BanSelectedByIp();
        }

        private void BtnUnban_Click(object sender, RoutedEventArgs e)
        {
            var item = _vm.Server.SelectedBlacklist;
            if (item == null)
            {
                MessageBox.Show(this, "请先在黑名单列表中选择要解除的条目。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ConfirmDanger(this, $"确定要解除 {item.Key} 在当前房间的黑名单吗？", "确认解除黑名单"))
                _vm.Server.UnbanSelected();
        }

        /// <summary>危险操作二次确认，点「是」返回 true。</summary>
        private static bool ConfirmDanger(Window owner, string message, string title)
            => MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private void BtnSaveLimit_Click(object sender, RoutedEventArgs e) => _vm.Server.SaveLimit();

        // ---- 关闭时清理 ----

        protected override void OnClosed(System.EventArgs e)
        {
            SaveUiConfig();
            _vm.Server.Stop();
            _vm.Client.Disconnect();
            base.OnClosed(e);
        }
    }
}
