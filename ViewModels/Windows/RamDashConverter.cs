using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WinState.ViewModels.Windows
{
    public class RamDashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double val)
            {
                // Radius 30 -> Circumference = 2 * PI * 30 = 188.495
                double circumference = 188.5;
                double dash = (val / 100.0) * circumference;
                double gap = circumference - dash;
                // Ensure no negative values
                if (dash < 0) dash = 0;
                if (gap < 0) gap = 0;
                
                return new DoubleCollection { dash, gap };
            }
            return new DoubleCollection { 0, 188.5 };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
