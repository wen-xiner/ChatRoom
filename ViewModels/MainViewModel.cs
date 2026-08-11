namespace ChatRoom.ViewModels
{
    /// <summary>运行模式。</summary>
    public enum RunMode
    {
        Client,
        Server,
        Both
    }

    /// <summary>主窗口的 ViewModel：切换运行模式，把服务器和客户端揉在一起。</summary>
    public class MainViewModel : ObservableObject
    {
        public ServerViewModel Server { get; } = new ServerViewModel();
        public ClientViewModel Client { get; } = new ClientViewModel();

        private RunMode _mode = RunMode.Client;
        public RunMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerVisible));
                OnPropertyChanged(nameof(IsClientVisible));
            }
        }

        public bool IsServerVisible => Mode == RunMode.Server || Mode == RunMode.Both;
        public bool IsClientVisible => Mode == RunMode.Client || Mode == RunMode.Both;
    }
}
