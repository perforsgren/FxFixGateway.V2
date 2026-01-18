using System;
using System.Globalization;
using System.Windows.Data;

namespace FxFixGateway.UI.Converters
{
    /// <summary>
    /// Returnerar true om värdet INTE är null.
    /// </summary>
    public class NullToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
