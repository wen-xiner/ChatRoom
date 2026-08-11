using System.Windows.Media.Imaging;
using ChatRoom.Models;

namespace ChatRoom.ViewModels
{
    /// <summary>图片消息展示项（缩略图/占位图随认证切换）。</summary>
    public class ImageMessageItem : ObservableObject
    {
        public ImageMessage Data { get; set; }

        /// <summary>是不是自己发的（是就靠右）。</summary>
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
