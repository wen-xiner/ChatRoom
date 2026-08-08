using System.Windows;
using ChatRoom.ViewModels;

namespace ChatRoom
{
    /// <summary>App 生命周期。</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            UiHelper.Init();
        }
    }
}
