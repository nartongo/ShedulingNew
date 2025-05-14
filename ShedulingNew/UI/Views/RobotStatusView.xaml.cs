using System;
using System.Windows;
using System.Windows.Controls;
using ShedulingNew.BusinessLogic.Services;
using ShedulingNew.UI.ViewModels;

namespace ShedulingNew.UI.Views
{
    /// <summary>
    /// RobotStatusView.xaml 的交互逻辑
    /// </summary>
    public partial class RobotStatusView : UserControl
    {
        private RobotStatusViewModel _viewModel;
        private RobotStatusService _robotStatusService;
        
        public RobotStatusView()
        {
            InitializeComponent();
            
            // 获取ViewModel实例
            _viewModel = Resources["RobotStatusViewModel"] as RobotStatusViewModel;
            
            // 初始化服务
            _robotStatusService = new RobotStatusService();
            try
            {
                _robotStatusService.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化机器人状态服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 刷新按钮点击事件处理
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 调用ViewModel的刷新方法
                await _viewModel.RefreshAllRobotStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新机器人状态失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 同步按钮点击事件处理
        /// </summary>
        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 同步待上报的状态记录
                await _robotStatusService.SyncPendingStatusLogsAsync();
                MessageBox.Show("状态同步完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步状态记录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 查看详情按钮点击事件处理
        /// </summary>
        private void ViewDetailButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前机器人ID
            var button = sender as Button;
            var item = button.DataContext as RobotStatusItem;
            
            if (item != null)
            {
                MessageBox.Show($"机器人[{item.RobotId}]详细信息:\n" +
                                $"状态: {item.StatusDisplay}\n" +
                                $"电量: {item.PowerDisplay}\n" +
                                $"位置: {item.Location}\n" +
                                $"方向: {item.DirectionDisplay}\n" +
                                $"速度: {item.SpeedDisplay}\n" +
                                $"任务ID: {item.TaskId ?? "无"}\n" +
                                $"最后更新: {item.LastUpdatedDisplay}",
                                "机器人详情", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        /// <summary>
        /// 查看历史按钮点击事件处理
        /// </summary>
        private async void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前机器人ID
            var button = sender as Button;
            var item = button.DataContext as RobotStatusItem;
            
            if (item != null)
            {
                try
                {
                    // 获取历史记录
                    var history = await _robotStatusService.GetRobotStatusHistoryAsync(item.RobotId, 10);
                    
                    // 构建历史记录显示文本
                    string historyText = $"机器人[{item.RobotId}]最近10条状态记录:\n\n";
                    
                    foreach (var record in history)
                    {
                        historyText += $"时间: {record.timestamp}\n" +
                                      $"状态: {record.status}\n" +
                                      $"电量: {record.power}%\n" +
                                      $"位置: {record.location}\n" +
                                      $"同步状态: {record.sync_status}\n" +
                                      $"---------------------\n";
                    }
                    
                    MessageBox.Show(historyText, "状态历史记录", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"获取历史记录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}