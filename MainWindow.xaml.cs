using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatRoom.Core.Auth;
using ChatRoom.Core.Storage;
using ChatRoom.ViewModels;

namespace ChatRoom
{
    /// <summary>
    /// 主窗口：运行模式切换 + 服务器/客户端面板事件接线。
    /// 数据与逻辑在 ViewModels，此处只做 UI 事件转发。
    /// </summary>
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

        /// <summary>加载并应用窗口大小与表格列宽（JSON 持久化）。</summary>
        private void ApplyUiConfig()
        {
            var ui = JsonStore.Load<UiConfig>(ChatRoom.Config.AppConfig.UiConfigPath);
            if (ui.WindowWidth >= MinWidth && ui.WindowHeight >= MinHeight)
            {
                Width = ui.WindowWidth;
                Height = ui.WindowHeight;
            }
            // 固定列夹紧到合理范围；名称列始终弹性，避免旧配置/用户拖宽导致列总宽超过面板
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

        // ---------------- 运行模式 ----------------

        private void RbClient_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Client;
        private void RbServer_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Server;
        private void RbBoth_Checked(object sender, RoutedEventArgs e) => _vm.Mode = RunMode.Both;

        // ---------------- 服务器 ----------------

        private void BtnStartServer_Click(object sender, RoutedEventArgs e) => _vm.Server.Start();

        private void BtnStopServer_Click(object sender, RoutedEventArgs e) => _vm.Server.Stop();

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
            _vm.Server.CloseRoom(item.RoomId);
        }

        // ---------------- 客户端 ----------------

        private void BtnConnect_Click(object sender, RoutedEventArgs e) => _vm.Client.Connect();

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) => _vm.Client.Disconnect();

        private void BtnJoinRoom_Click(object sender, RoutedEventArgs e) => _vm.Client.JoinRoom();

        private void BtnLeaveRoom_Click(object sender, RoutedEventArgs e) => _vm.Client.LeaveCurrentRoom();

        private void LstJoinedRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstJoinedRooms.SelectedItem is JoinedRoomItem item)
                _vm.Client.SetActiveRoom(item.RoomId);
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

        // ---------------- 消息模板事件 ----------------

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

        // ---------------- 消息自动滚动 ----------------

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LstMessages.Items.Count > 0)
                LstMessages.ScrollIntoView(LstMessages.Items[LstMessages.Items.Count - 1]);
        }

        // ---------------- 服务器房间详情：踢人 / 黑名单 ----------------

        private void LstRoomMembers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.Server.SelectedUser = LstRoomMembers.SelectedItem as ServerUserItem;
        }

        private void LstRoomBlacklist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.Server.SelectedBlacklist = LstRoomBlacklist.SelectedItem as ChatRoom.Models.BlacklistItem;
        }

        private void BtnKick_Click(object sender, RoutedEventArgs e) => _vm.Server.KickSelectedUser();

        private void BtnBanId_Click(object sender, RoutedEventArgs e) => _vm.Server.BanSelectedById();

        private void BtnBanIp_Click(object sender, RoutedEventArgs e) => _vm.Server.BanSelectedByIp();

        private void BtnUnban_Click(object sender, RoutedEventArgs e) => _vm.Server.UnbanSelected();

        private void BtnSaveLimit_Click(object sender, RoutedEventArgs e) => _vm.Server.SaveLimit();

        // ---------------- 关闭时清理 ----------------

        protected override void OnClosed(System.EventArgs e)
        {
            SaveUiConfig();
            _vm.Server.Stop();
            _vm.Client.Disconnect();
            base.OnClosed(e);
        }
    }
}
