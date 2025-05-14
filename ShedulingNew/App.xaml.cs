using System;
using System.Configuration;
using System.Data;
using System.Windows;
using ShedulingNew.Coordinators;
using ShedulingNew.BusinessLogic.Services;

namespace ShedulingNew;

/// <summary>
/// App.xaml 的交互逻辑
/// 应用程序的入口点，负责初始化和启动整个系统
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用程序启动事件处理方法
    /// 按顺序初始化并启动系统的各个组件，注册应用程序退出事件
    /// </summary>
    /// <param name="e">启动事件参数</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        try
        {
            // 初始化配置服务（必须先于其他服务初始化）
            // 因为其他服务可能依赖于配置信息
            var configService = ConfigService.Instance;
            
            // 初始化系统协调器
            // 系统协调器会依次初始化通信协调器、工作流协调器等子系统
            SystemCoordinator.Instance.Initialize();
            
            // 启动系统
            // 启动后系统开始正常运行，如开始通信、处理工作流等
            SystemCoordinator.Instance.Start();
            
            // 注册应用程序退出事件
            // 确保应用程序退出前能够停止系统，释放资源
            Current.Exit += (s, args) =>
            {
                // 停止系统
                // 停止所有子系统，如关闭通信连接、终止工作流等
                SystemCoordinator.Instance.Stop();
            };
        }
        catch (Exception ex)
        {
            // 系统初始化失败，弹出错误消息并关闭应用程序
            MessageBox.Show($"系统初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
        }
    }
}