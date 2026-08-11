using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ChatRoom.Views
{
    /// <summary>看大图的窗口，能复制也能存下来。</summary>
    public partial class ImageViewerDialog : Window
    {
        private readonly string _filePath;
        private readonly BitmapSource _image;

        public ImageViewerDialog(string imagePath, string displayName)
        {
            InitializeComponent();
            _filePath = imagePath;
            _image = LoadImage(imagePath);
            ImgView.Source = _image;
            TxtTitle.Text = $"图片：{displayName}（{_image.PixelWidth}×{_image.PixelHeight}）";
        }

        private static BitmapSource LoadImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetImage(_image);
                MessageBox.Show(this, "已复制到剪贴板。", "复制", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：" + ex.Message, "复制", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "保存图片",
                FileName = Path.GetFileName(_filePath),
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_filePath, dlg.FileName, true);
                    MessageBox.Show(this, "图片已保存。", "保存", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
