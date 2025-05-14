using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ShedulingNew.UI.Common
{
    /// <summary>
    /// 状态到颜色转换器 - 用于在UI中根据状态显示不同颜色
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 处理布尔值（用于PLC线圈状态）
            if (value is bool boolValue)
            {
                return boolValue ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Gray);
            }
            
            // 处理字符串状态
            if (value is string status)
            {
                return status.ToLower() switch
                {
                    "正常" or "已连接" or "运行中" or "空闲" or "on" or "idle" or "running" or "connected" or "ready" => new SolidColorBrush(Colors.Green),
                    "警告" or "充电中" or "待命" or "正在刷新..." or "warning" or "charging" or "standby" => new SolidColorBrush(Colors.Orange),
                    "错误" or "断开" or "故障" or "未连接" or "off" or "error" or "disconnected" or "fault" => new SolidColorBrush(Colors.Red),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 