using System.Windows;
using ChatRoom.Core.Auth;

namespace ChatRoom.Views
{
    /// <summary>注册对话框：显示注册码，输入认证码完成认证（解锁文件/图片收发）。</summary>
    public partial class RegistrationDialog : Window
    {
        public RegistrationDialog()
        {
            InitializeComponent();
            TxtRegCode.Text = AuthService.GetRegCode();
        }

        /// <summary>是否注册成功（调用方据此刷新认证状态）。</summary>
        public bool RegisteredSuccessfully { get; private set; }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.TryRegister(TxtAuthCode.Text.Trim(), out var error))
            {
                RegisteredSuccessfully = true;
                MessageBox.Show(this, "注册成功！现在可以使用 发送/接收 图片、文件 功能。",
                    "注册成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            else
            {
                TxtResult.Text = error;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
