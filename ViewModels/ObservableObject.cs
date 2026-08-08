using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChatRoom.ViewModels
{
    /// <summary>MVVM 基类：属性变更通知。</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
