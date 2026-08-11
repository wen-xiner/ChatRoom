using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>文件消息展示项（含下载状态与 IsMine）。</summary>
    public class FileMessageItem : ObservableObject
    {
        public FileMessage Data { get; set; }

        /// <summary>是不是自己发的（是就靠右）。</summary>
        public bool IsMine { get; set; }

        public string SenderDisplay => IsMine ? "我" : Data?.SenderName;

        private string _stateText = "未下载";
        public string StateText
        {
            get => _stateText;
            set { _stateText = value; OnPropertyChanged(); }
        }

        public string SizeText => Data == null ? "" : FormatSize(Data.FileSize);

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }
    }
}
