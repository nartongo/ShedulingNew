using System;
using System.ComponentModel;
using System.Windows.Input;
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Services;
using ShedulingNew.Coordinators;
using ShedulingNew.UI.Commands;

namespace ShedulingNew.UI.ViewModels
{
    /// <summary>
    /// 主窗口的ViewModel - 简化版
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        // 状态属性
        private string _statusMessage = "就绪";
        private string _systemStatus = "未启动";
        private string _currentTaskInfo = "无当前任务";
        private int _taskProgress = 0;
        private string _taskProgressText = "0%";
        private string _systemLogs = "系统准备就绪...\r\n";
        private DateTime _lastUpdateTime = DateTime.Now;
        
        // 服务引用
        private EventHub _eventHub;
        private SystemCoordinator _systemCoordinator;
        
        // 实现INotifyPropertyChanged接口
        public event PropertyChangedEventHandler PropertyChanged;
        
        #region 属性
        
        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged(nameof(StatusMessage));
                    AddLog(value);
                }
            }
        }
        
        /// <summary>
        /// 系统状态
        /// </summary>
        public string SystemStatus
        {
            get => _systemStatus;
            set
            {
                if (_systemStatus != value)
                {
                    _systemStatus = value;
                    OnPropertyChanged(nameof(SystemStatus));
                }
            }
        }
        
        /// <summary>
        /// 当前任务信息
        /// </summary>
        public string CurrentTaskInfo
        {
            get => _currentTaskInfo;
            set
            {
                if (_currentTaskInfo != value)
                {
                    _currentTaskInfo = value;
                    OnPropertyChanged(nameof(CurrentTaskInfo));
                }
            }
        }
        
        /// <summary>
        /// 任务进度
        /// </summary>
        public int TaskProgress
        {
            get => _taskProgress;
            set
            {
                if (_taskProgress != value)
                {
                    _taskProgress = value;
                    TaskProgressText = $"{value}%";
                    OnPropertyChanged(nameof(TaskProgress));
                }
            }
        }
        
        /// <summary>
        /// 任务进度文本
        /// </summary>
        public string TaskProgressText
        {
            get => _taskProgressText;
            set
            {
                if (_taskProgressText != value)
                {
                    _taskProgressText = value;
                    OnPropertyChanged(nameof(TaskProgressText));
                }
            }
        }
        
        /// <summary>
        /// 系统日志
        /// </summary>
        public string SystemLogs
        {
            get => _systemLogs;
            set
            {
                if (_systemLogs != value)
                {
                    _systemLogs = value;
                    OnPropertyChanged(nameof(SystemLogs));
                }
            }
        }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime
        {
            get => _lastUpdateTime;
            set
            {
                if (_lastUpdateTime != value)
                {
                    _lastUpdateTime = value;
                    OnPropertyChanged(nameof(LastUpdateTime));
                }
            }
        }
        
        #endregion
        
        // 命令
        public ICommand StartSystemCommand { get; private set; }
        public ICommand StopSystemCommand { get; private set; }
        
        public MainViewModel()
        {
            _eventHub = EventHub.Instance;
            _systemCoordinator = SystemCoordinator.Instance;
            
            // 初始化命令
            StartSystemCommand = new RelayCommand(ExecuteStartSystem);
            StopSystemCommand = new RelayCommand(ExecuteStopSystem);
            
            // 订阅事件
            SubscribeToEvents();
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventHub.Subscribe("SystemDataUpdated", data =>
            {
                // 更新UI状态
                StatusMessage = $"数据已更新: {DateTime.Now}";
                LastUpdateTime = DateTime.Now;
            });
            
            _eventHub.Subscribe("TaskStarted", data =>
            {
                dynamic taskData = data;
                CurrentTaskInfo = $"机器 {taskData.MachineId} 的任务已启动，共 {taskData.SpindleCount} 个断头锭子";
                TaskProgress = 0;
            });
            
            _eventHub.Subscribe("SpindleRepaired", data =>
            {
                // 更新任务进度
                UpdateTaskProgress();
            });
            
            _eventHub.Subscribe("TaskCompleted", data =>
            {
                TaskProgress = 100;
                CurrentTaskInfo = "任务已完成";
            });
        }
        
        /// <summary>
        /// 更新任务进度
        /// </summary>
        private async void UpdateTaskProgress()
        {
            try
            {
                var workflowCoordinator = _systemCoordinator.GetWorkflowCoordinator();
                if (workflowCoordinator != null)
                {
                    var taskController = workflowCoordinator.GetTaskController();
                    if (taskController != null)
                    {
                        var progress = await taskController.GetCurrentTaskProgress();
                        
                        if (progress != null && progress.TotalSpindles > 0)
                        {
                            int progressValue = (int)(progress.CompletedSpindles * 100 / progress.TotalSpindles);
                            TaskProgress = progressValue;
                            
                            CurrentTaskInfo = $"机器 {progress.MachineId} 的任务正在进行中，" +
                                            $"已完成 {progress.CompletedSpindles}/{progress.TotalSpindles} 个断头锭子";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"获取任务进度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 启动系统
        /// </summary>
        private void ExecuteStartSystem(object parameter)
        {
            try
            {
                // 初始化系统协调器
                _systemCoordinator.Initialize();
                
                // 启动系统
                _systemCoordinator.Start();
                
                SystemStatus = "已启动";
                StatusMessage = "系统已启动";
            }
            catch (Exception ex)
            {
                SystemStatus = "启动失败";
                StatusMessage = $"系统启动失败: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 停止系统
        /// </summary>
        private void ExecuteStopSystem(object parameter)
        {
            try
            {
                // 停止系统
                _systemCoordinator.Stop();
                
                SystemStatus = "已停止";
                StatusMessage = "系统已停止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"系统停止失败: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 添加日志
        /// </summary>
        private void AddLog(string log)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {log}\r\n";
            SystemLogs = logEntry + SystemLogs;
            
            // 保持日志不超过500行
            int maxLines = 500;
            string[] lines = SystemLogs.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length > maxLines)
            {
                SystemLogs = string.Join("\r\n", lines, 0, maxLines);
            }
        }
        
        /// <summary>
        /// 属性改变通知
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 