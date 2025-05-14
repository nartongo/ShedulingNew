using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Data;
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Services;

namespace ShedulingNew.UI.ViewModels
{
    /// <summary>
    /// 机器人状态视图模型
    /// </summary>
    public class RobotStatusViewModel : INotifyPropertyChanged
    {
        private EventHub _eventHub;
        private RobotStatusService _robotStatusService;
        private ObservableCollection<RobotStatusItem> _robotStatusItems = new();
        private string _lastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        private string _statusMessage = "就绪";
        
        // 实现INotifyPropertyChanged接口
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// 机器人状态集合
        /// </summary>
        public ObservableCollection<RobotStatusItem> RobotStatusItems
        {
            get => _robotStatusItems;
            set
            {
                if (_robotStatusItems != value)
                {
                    _robotStatusItems = value;
                    OnPropertyChanged(nameof(RobotStatusItems));
                }
            }
        }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public string LastUpdateTime
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
                }
            }
        }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public RobotStatusViewModel()
        {
            _eventHub = EventHub.Instance;
            _robotStatusService = new RobotStatusService();
            
            // 为机器人状态集合创建线程安全的包装
            BindingOperations.EnableCollectionSynchronization(_robotStatusItems, new object());
            
            // 添加一些测试数据（真实场景中应通过服务获取）
            _robotStatusItems.Add(new RobotStatusItem 
            { 
                RobotId = "AGV001", 
                Status = "IDLE", 
                PowerLevel = 85, 
                Location = "等待区-1",
                Direction = "NONE",
                Speed = 0.0,
                LastUpdated = DateTime.Now
            });
            
            _robotStatusItems.Add(new RobotStatusItem 
            { 
                RobotId = "AGV002", 
                Status = "WORKING", 
                PowerLevel = 72, 
                Location = "巷道3-17",
                Direction = "FORWARD",
                Speed = 1.2,
                TaskId = "TASK_20230615123045",
                LastUpdated = DateTime.Now
            });
            
            // 订阅事件
            SubscribeToEvents();
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventHub.Subscribe("RobotStatusChanged", async (data) =>
            {
                // 直接将data赋值给dynamic变量，避免在模式匹配中使用dynamic
                dynamic statusData = data;
                try
                {
                    await UpdateRobotStatusAsync(statusData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RobotStatusViewModel] 处理状态变更事件失败: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// 处理机器人状态更新
        /// </summary>
        private async Task UpdateRobotStatusAsync(dynamic statusData)
        {
            string robotId = statusData.RobotId?.ToString();
            
            if (string.IsNullOrEmpty(robotId))
            {
                return;
            }
            
            // 查找现有项
            var existingItem = FindRobotStatusItem(robotId);
            
            if (existingItem != null)
            {
                // 更新现有项
                existingItem.Status = statusData.Status?.ToString() ?? existingItem.Status;
                existingItem.PowerLevel = statusData.Power != null ? Convert.ToInt32(statusData.Power) : existingItem.PowerLevel;
                existingItem.Location = statusData.Location?.ToString() ?? existingItem.Location;
                existingItem.Direction = statusData.Direction?.ToString() ?? existingItem.Direction;
                existingItem.Speed = statusData.Speed != null ? Convert.ToDouble(statusData.Speed) : existingItem.Speed;
                existingItem.TaskId = statusData.TaskId?.ToString();
                existingItem.LastUpdated = DateTime.Now;
            }
            else
            {
                // 添加新项
                var newItem = new RobotStatusItem
                {
                    RobotId = robotId,
                    Status = statusData.Status?.ToString() ?? "UNKNOWN",
                    PowerLevel = statusData.Power != null ? Convert.ToInt32(statusData.Power) : 0,
                    Location = statusData.Location?.ToString() ?? "UNKNOWN",
                    Direction = statusData.Direction?.ToString(),
                    Speed = statusData.Speed != null ? Convert.ToDouble(statusData.Speed) : 0,
                    TaskId = statusData.TaskId?.ToString(),
                    LastUpdated = DateTime.Now
                };
                
                _robotStatusItems.Add(newItem);
            }
            
            LastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            StatusMessage = $"已更新机器人[{robotId}]状态";
        }
        
        /// <summary>
        /// 查找机器人状态项
        /// </summary>
        private RobotStatusItem FindRobotStatusItem(string robotId)
        {
            foreach (var item in _robotStatusItems)
            {
                if (item.RobotId == robotId)
                {
                    return item;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// 刷新所有机器人状态
        /// </summary>
        public async Task RefreshAllRobotStatusAsync()
        {
            try
            {
                StatusMessage = "正在刷新机器人状态...";
                
                // 从服务获取所有机器人的状态
                var allStatuses = await _robotStatusService.GetAllRobotsStatusAsync();
                
                foreach (var status in allStatuses)
                {
                    // 更新到UI
                    await UpdateRobotStatusAsync(status);
                }
                
                LastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                StatusMessage = "机器人状态刷新完成";
            }
            catch (Exception ex)
            {
                StatusMessage = $"刷新机器人状态失败: {ex.Message}";
                Console.WriteLine($"[RobotStatusViewModel] {StatusMessage}");
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
    
    /// <summary>
    /// 机器人状态项
    /// </summary>
    public class RobotStatusItem : INotifyPropertyChanged
    {
        private string _robotId;
        private string _status;
        private int _powerLevel;
        private string _location;
        private string _direction;
        private double _speed;
        private string _taskId;
        private DateTime _lastUpdated;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// 机器人ID
        /// </summary>
        public string RobotId
        {
            get => _robotId;
            set
            {
                if (_robotId != value)
                {
                    _robotId = value;
                    OnPropertyChanged(nameof(RobotId));
                }
            }
        }
        
        /// <summary>
        /// 状态
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }
        
        /// <summary>
        /// 状态显示文本
        /// </summary>
        public string StatusDisplay
        {
            get
            {
                return Status switch
                {
                    "IDLE" => "空闲",
                    "WORKING" => "工作中",
                    "CHARGING" => "充电中",
                    "ERROR" => "错误",
                    _ => Status ?? "未知"
                };
            }
        }
        
        /// <summary>
        /// 电量百分比
        /// </summary>
        public int PowerLevel
        {
            get => _powerLevel;
            set
            {
                if (_powerLevel != value)
                {
                    _powerLevel = value;
                    OnPropertyChanged(nameof(PowerLevel));
                    OnPropertyChanged(nameof(PowerDisplay));
                }
            }
        }
        
        /// <summary>
        /// 电量显示
        /// </summary>
        public string PowerDisplay => $"{PowerLevel}%";
        
        /// <summary>
        /// 位置
        /// </summary>
        public string Location
        {
            get => _location;
            set
            {
                if (_location != value)
                {
                    _location = value;
                    OnPropertyChanged(nameof(Location));
                }
            }
        }
        
        /// <summary>
        /// 方向
        /// </summary>
        public string Direction
        {
            get => _direction;
            set
            {
                if (_direction != value)
                {
                    _direction = value;
                    OnPropertyChanged(nameof(Direction));
                    OnPropertyChanged(nameof(DirectionDisplay));
                }
            }
        }
        
        /// <summary>
        /// 方向显示
        /// </summary>
        public string DirectionDisplay
        {
            get
            {
                return Direction switch
                {
                    "FORWARD" => "前进",
                    "BACKWARD" => "后退",
                    "NONE" => "静止",
                    _ => Direction ?? "未知"
                };
            }
        }
        
        /// <summary>
        /// 速度 (m/s)
        /// </summary>
        public double Speed
        {
            get => _speed;
            set
            {
                if (_speed != value)
                {
                    _speed = value;
                    OnPropertyChanged(nameof(Speed));
                    OnPropertyChanged(nameof(SpeedDisplay));
                }
            }
        }
        
        /// <summary>
        /// 速度显示
        /// </summary>
        public string SpeedDisplay => $"{Speed:F1} m/s";
        
        /// <summary>
        /// 任务ID
        /// </summary>
        public string TaskId
        {
            get => _taskId;
            set
            {
                if (_taskId != value)
                {
                    _taskId = value;
                    OnPropertyChanged(nameof(TaskId));
                    OnPropertyChanged(nameof(HasTask));
                }
            }
        }
        
        /// <summary>
        /// 是否有任务
        /// </summary>
        public bool HasTask => !string.IsNullOrEmpty(TaskId);
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated
        {
            get => _lastUpdated;
            set
            {
                if (_lastUpdated != value)
                {
                    _lastUpdated = value;
                    OnPropertyChanged(nameof(LastUpdated));
                    OnPropertyChanged(nameof(LastUpdatedDisplay));
                }
            }
        }
        
        /// <summary>
        /// 最后更新时间显示
        /// </summary>
        public string LastUpdatedDisplay => LastUpdated.ToString("yyyy-MM-dd HH:mm:ss");
        
        /// <summary>
        /// 属性改变通知
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 