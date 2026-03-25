using System.Globalization;
using System.Windows.Data;

namespace Vktun.IoT.Connector.Client.Converters;

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        
        var enumValue = value.ToString();
        var targetValue = parameter.ToString();
        
        return enumValue == targetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Binding.DoNothing;
        
        var boolValue = (bool)value;
        if (boolValue)
        {
            return Enum.Parse(targetType, parameter.ToString()!);
        }
        
        return Binding.DoNothing;
    }
}

public class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;
        
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Binding.DoNothing;
        
        if (value is not bool boolValue || !boolValue)
            return Binding.DoNothing;
        
        var paramString = parameter.ToString();
        if (string.IsNullOrEmpty(paramString))
            return Binding.DoNothing;
        
        return Enum.Parse(targetType, paramString);
    }
}
