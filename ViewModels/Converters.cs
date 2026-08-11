using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatRoom.ViewModels
{
    /// <summary>bool 转对齐方向：true 靠右（自己发的），false 靠左（别人发的）。</summary>
    public class BoolToHorizontalAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
