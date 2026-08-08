using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatRoom.ViewModels
{
    /// <summary>bool → HorizontalAlignment：true=右（本人消息），false=左（他人消息）。</summary>
    public class BoolToHorizontalAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
