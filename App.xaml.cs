using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ChatRoom.Config;
using ChatRoom.ViewModels;

namespace ChatRoom
{
    /// <summary>管 App 启动，顺手抓全局异常，方便排查崩溃。</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            UiHelper.Init();
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += (s, args) => LogCrash(args.Exception);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            MessageBox.Show(
                "发生未处理异常：\n" + e.Exception.Message + "\n\n详细信息已写入 Data\\crash.log",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception);
        }

        /// <summary>异常堆栈写入 Data\crash.log。</summary>
        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var dir = AppConfig.DataDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{ex}\r\n\r\n");
            }
            catch { /* 写日志失败忽略 */ }
        }
    }
}
