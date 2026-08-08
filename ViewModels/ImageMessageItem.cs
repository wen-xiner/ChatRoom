using System.Windows.Media.Imaging;
using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>图片消息展示项（缩略图或占位图，随认证状态切换）。</summary>
    public class ImageMessageItem : ObservableObject
    {
        public ImageMessage Data { get; set; }

        /// <summary>是否本人发送（气泡右对齐）。</summary>
        public bool IsMine { get; set; }

        public string SenderDisplay => IsMine ? "我" : Data?.SenderName;

        private BitmapSource _imageSource;
        public BitmapSource ImageSource
        {
            get => _imageSource;
            set { _imageSource = value; OnPropertyChanged(); }
        }
    }
}
