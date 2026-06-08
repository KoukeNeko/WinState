using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinState.Helpers
{
    /// <summary>
    /// Shows a "saved" indicator only for the field that was most recently persisted. The bound
    /// value is the ViewModel's RecentlySavedField id; the ConverterParameter is this row's field
    /// id. Visible when they match, Collapsed otherwise.
    /// </summary>
    internal class SavedFieldToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var current = value as string;
            var fieldId = parameter as string;
            return !string.IsNullOrEmpty(current) && current == fieldId
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
