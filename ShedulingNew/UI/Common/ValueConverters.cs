using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ShedulingNew.UI.Common
{
    /// <summary>
    /// 低于20%电量转换器
    /// </summary>
    public class LessThan20Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int powerLevel)
            {
                return powerLevel < 20;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// 电量在20%-40%之间转换器
    /// </summary>
    public class Between20And40Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int powerLevel)
            {
                return powerLevel >= 20 && powerLevel < 40;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    
    /// <summary>
    /// 数值到可见性转换器
    /// </summary>
    public class NumberToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int number && parameter is string compareText)
            {
                if (compareText.Contains(">"))
                {
                    int compareValue = int.Parse(compareText.Replace(">", "").Trim());
                    return number > compareValue ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (compareText.Contains("<"))
                {
                    int compareValue = int.Parse(compareText.Replace("<", "").Trim());
                    return number < compareValue ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (compareText.Contains("="))
                {
                    int compareValue = int.Parse(compareText.Replace("=", "").Trim());
                    return number == compareValue ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 