using System;
using System.Windows;
using System.Windows.Threading;

namespace ChatRoom.ViewModels
{
    /// <summary>把后台线程回调切到 UI 线程（防止 WebSocket 线程碰 WPF 集合）。</summary>
    public static class UiHelper
    {
        private static Dispatcher _dispatcher;

        public static void Init() => _dispatcher = Application.Current?.Dispatcher;

        public static void Dispatch(Action action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess())
                action();
            else
                _dispatcher.BeginInvoke(action);
        }
    }
}
