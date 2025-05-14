using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.DataAccess;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// 后端通信服务类
    /// 负责与后端服务器通信，包括状态上报、指令接收和数据同步
    /// 使用RabbitMQ实现异步消息通信
    /// </summary>
    public class BackendService
    {
        private readonly EventHub _eventHub;
        private readonly ConfigService _configService;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private RabbitMqClient _rabbitMqClient;
        private SQLiteStatusHelper _statusHelper;
        private Timer _statusReportTimer;
        private RobotStatus _currentStatus;
        
        /// <summary>
        /// 构造函数
        /// 初始化事件总线和配置服务实例
        /// </summary>
        public BackendService()
        {
            _eventHub = EventHub.Instance;
            _configService = ConfigService.Instance;
            _currentStatus = new RobotStatus();
        }
        
        /// <summary>
        /// 启动后端通信服务
        /// 初始化RabbitMQ客户端，建立连接，注册事件处理器，并启动状态上报定时器
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            try
            {
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // 初始化SQLite状态帮助类
                _statusHelper = new SQLiteStatusHelper();
                
                // 初始化状态对象
                var appConfig = _configService.Config;
                _currentStatus.MachineId = appConfig.Robot.MachineId;
                _currentStatus.SoftwareVersion = appConfig.Robot.Version;
                
                // 初始化RabbitMQ客户端
                _rabbitMqClient = new RabbitMqClient(appConfig.Backend, appConfig.Robot.MachineId);
                
                // 连接RabbitMQ服务器
                if (_rabbitMqClient.Connect())
                {
                    // 发送启动请求并设置处理器
                    _rabbitMqClient.SendStartRequest();
                    _rabbitMqClient.SetupStartAckHandler();
                    _rabbitMqClient.SetupStatusAckHandler();
                    _rabbitMqClient.SetupTaskAssignmentHandler();
                    
                    // 启动状态上报定时器
                    int intervalMs = appConfig.Backend.StatusReportIntervalMs;
                    _statusReportTimer = new Timer(StatusReportCallback, null, 1000, intervalMs);
                    
                    // 启动未同步状态重发线程
                    System.Threading.Tasks.Task.Run(() => ResendPendingStatusReports(_cancellationTokenSource.Token));
                    
                    // 订阅相关事件
                    SubscribeToEvents();
                    
                    Console.WriteLine("后端通信服务启动成功");
                }
                else
                {
                    _isRunning = false;
                    Console.WriteLine("后端通信服务启动失败：无法连接到RabbitMQ服务器");
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Console.WriteLine($"后端通信服务启动失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 停止后端通信服务
        /// 取消所有异步操作，关闭连接，释放资源
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();
                _statusReportTimer?.Dispose();
                _rabbitMqClient?.Dispose();
                
                Console.WriteLine("后端通信服务停止成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"后端通信服务停止失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 状态上报定时回调
        /// 定期将当前状态上报到后端服务器
        /// </summary>
        private void StatusReportCallback(object state)
        {
            if (!_isRunning) return;
            
            try
            {
                // 更新系统运行时间和时间戳
                _currentStatus.UptimeSeconds = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
                _currentStatus.LastUpdateTime = DateTime.UtcNow;
                
                // 保存状态到SQLite
                long logId = _statusHelper.InsertStatusLog(_currentStatus);
                _statusHelper.UpsertLatestStatus(_currentStatus);
                
                // 发送状态到RabbitMQ
                if (_rabbitMqClient.SendStatusReport(_currentStatus))
                {
                    Console.WriteLine($"状态上报成功: {DateTime.Now}");
                    
                    // 订阅状态确认，一段时间后更新同步状态
                    System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ => {
                        // 如果5秒内没有收到确认，将不更新状态，等待重发机制处理
                    });
                }
                else
                {
                    Console.WriteLine("状态上报失败");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"状态上报过程中发生错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 重发未同步的状态报告
        /// 定期检查并重发所有未成功同步到后端的状态记录
        /// </summary>
        private async System.Threading.Tasks.Task ResendPendingStatusReports(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 等待一段时间再开始重发
                    await System.Threading.Tasks.Task.Delay(30000, cancellationToken); // 30秒检查一次
                    
                    // 获取所有未同步状态
                    var pendingLogs = _statusHelper.GetPendingStatusLogs(50);
                    
                    if (pendingLogs.Count > 0)
                    {
                        Console.WriteLine($"开始重发 {pendingLogs.Count} 条未同步状态");
                        
                        foreach (var log in pendingLogs)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            
                            // 解析状态对象
                            var status = Newtonsoft.Json.JsonConvert.DeserializeObject<RobotStatus>(log["status_json"].ToString());
                            
                            // 重新发送到RabbitMQ
                            if (_rabbitMqClient.SendStatusReport(status))
                            {
                                // 更新同步状态为SUCCESS
                                _statusHelper.UpdateStatusLogSync(Convert.ToInt64(log["id"]), "SUCCESS");
                            }
                            
                            // 避免发送太快
                            await System.Threading.Tasks.Task.Delay(500, cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 操作被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"重发状态过程中发生错误: {ex.Message}");
                    await System.Threading.Tasks.Task.Delay(10000, cancellationToken); // 错误后等待更长时间
                }
            }
        }
        
        /// <summary>
        /// 订阅系统事件
        /// 处理系统内部事件，更新状态或发送通知
        /// </summary>
        private void SubscribeToEvents()
        {
            // 订阅系统状态更新事件
            _eventHub.Subscribe("SystemDataUpdated", data =>
            {
                if (_isRunning)
                {
                    UpdateRobotStatus(data);
                }
            });
            
            // 订阅后端状态确认事件
            _eventHub.Subscribe("BackendStatusAck", data =>
            {
                if (_isRunning)
                {
                    ProcessStatusAck(data);
                }
            });
            
            // 订阅后端启动确认事件
            _eventHub.Subscribe("BackendStartAck", data =>
            {
                if (_isRunning)
                {
                    Console.WriteLine("收到后端启动确认");
                }
            });
        }
        
        /// <summary>
        /// 更新机器人状态
        /// 根据系统事件更新当前状态对象
        /// </summary>
        private void UpdateRobotStatus(object data)
        {
            try
            {
                // 根据数据类型更新状态
                dynamic eventData = data;
                string eventType = eventData?.Type?.ToString();
                
                if (eventType == "PositionChanged")
                {
                    _currentStatus.CurrentPosition = eventData.Position.ToString();
                }
                else if (eventType == "TaskStatusChanged")
                {
                    _currentStatus.CurrentTaskId = eventData.TaskId?.ToString();
                    _currentStatus.TaskProgress = Convert.ToInt32(eventData.Progress);
                    _currentStatus.WorkStatus = eventData.Status.ToString();
                }
                else if (eventType == "RepairCompleted")
                {
                    _currentStatus.TotalRepairs++;
                    if (Convert.ToBoolean(eventData.Success))
                    {
                        _currentStatus.SuccessfulRepairs++;
                    }
                    else
                    {
                        _currentStatus.FailedRepairs++;
                    }
                }
                else if (eventType == "ErrorOccurred")
                {
                    _currentStatus.IsError = true;
                    _currentStatus.ErrorMessage = eventData.Message.ToString();
                }
                else if (eventType == "ErrorResolved")
                {
                    _currentStatus.IsError = false;
                    _currentStatus.ErrorMessage = null;
                }
                else if (eventType == "WarningOccurred")
                {
                    _currentStatus.IsWarning = true;
                    _currentStatus.WarningMessage = eventData.Message.ToString();
                }
                else if (eventType == "WarningResolved")
                {
                    _currentStatus.IsWarning = false;
                    _currentStatus.WarningMessage = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新机器人状态出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理状态确认
        /// 更新本地日志的同步状态
        /// </summary>
        private void ProcessStatusAck(object data)
        {
            try
            {
                dynamic ack = data;
                if (ack != null && ack.logId != null)
                {
                    long logId = ack.logId;
                    _statusHelper.UpdateStatusLogSync(logId, "SUCCESS");
                    Console.WriteLine($"状态日志 {logId} 已确认同步");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理状态确认出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送任务状态更新
        /// 当任务状态变更时，主动向后端发送通知
        /// </summary>
        public void SendTaskStatusUpdate(string taskId, string status, int progress)
        {
            if (!_isRunning) return;
            
            try
            {
                var taskStatus = new
                {
                    type = "TaskStatusUpdate",
                    machineId = _currentStatus.MachineId,
                    taskId = taskId,
                    status = status,
                    progress = progress,
                    timestamp = DateTime.UtcNow.ToString("o")
                };
                
                _rabbitMqClient.Publish("backend.task", $"task.status.{_currentStatus.MachineId}", taskStatus);
                Console.WriteLine($"任务状态更新已发送: {taskId}, 状态: {status}, 进度: {progress}%");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送任务状态更新失败: {ex.Message}");
            }
        }
    }
} 