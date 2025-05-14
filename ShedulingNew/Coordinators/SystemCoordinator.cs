using System;using System.Collections.Generic;using ShedulingNew.BusinessLogic;using ShedulingNew.BusinessLogic.Services;using ShedulingNew.BusinessLogic.Models;using ShedulingNew.DataAccess;

namespace ShedulingNew.Coordinators
{
    /// <summary>
    /// 系统协调器 - 顶层协调类，负责协调各个子系统
    /// 作为整个调度系统的核心控制中心，管理所有子系统的生命周期
    /// </summary>
    public class SystemCoordinator
    {
        // 单例实例，确保全局只有一个SystemCoordinator
        private static readonly SystemCoordinator _instance = new SystemCoordinator();
        public static SystemCoordinator Instance => _instance;
        
        // 各个子协调器和服务的引用
        private CommunicationCoordinator _communicationCoordinator;  // 通信协调器，负责所有外部通信
        private WorkflowCoordinator _workflowCoordinator;            // 工作流协调器，负责业务流程
        private RobotStatusService _robotStatusService;              // 机器人状态服务，管理机器人状态
        private ConfigService _configService;                        // 配置服务，管理系统配置
        private SQLiteStatusHelper _statusHelper;                    // SQLite状态助手，管理本地状态存储
        
        // 系统状态标志
        private bool _isInitialized = false;  // 是否已初始化
        private bool _isRunning = false;      // 是否正在运行
        
        /// <summary>
        /// 获取系统配置
        /// 允许外部访问配置，但不允许直接修改，需通过UpdateConfig方法修改
        /// </summary>
        public AppConfig Config => _configService.Config;
        
        /// <summary>
        /// 私有构造函数，实现单例模式
        /// 创建并初始化各个子协调器和服务实例
        /// </summary>
        private SystemCoordinator()
        {
            _configService = ConfigService.Instance;                  // 获取配置服务实例
            _communicationCoordinator = new CommunicationCoordinator(); // 创建通信协调器
            _workflowCoordinator = new WorkflowCoordinator();           // 创建工作流协调器
            _robotStatusService = new RobotStatusService();             // 创建机器人状态服务
            _statusHelper = new SQLiteStatusHelper();                   // 创建SQLite状态助手
        }
        
        /// <summary>
        /// 初始化系统协调器
        /// 按顺序初始化各个子系统，建立事件订阅关系
        /// </summary>
        public void Initialize()
        {
            // 如果已经初始化过，则直接返回
            if (_isInitialized)
                return;
                
            try
            {
                Console.WriteLine($"[SystemCoordinator] 系统初始化中，机器ID: {Config.Robot.MachineId}");
                
                // 初始化通信协调器，负责PLC、AGV和后端通信
                _communicationCoordinator.Initialize();
                
                // 初始化工作流协调器，负责任务和流程管理
                _workflowCoordinator.Initialize();
                
                // 初始化机器人状态服务，负责状态管理和监控
                _robotStatusService.Initialize();
                
                // 订阅全局未处理异常事件，确保系统稳定性
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    HandleGlobalException(e.ExceptionObject as Exception);
                };
                
                // 订阅状态相关事件，用于记录和同步状态
                SubscribeToStatusEvents();
                
                _isInitialized = true;
                
                // 通过事件总线发布初始化完成事件，通知其他组件
                EventHub.Instance.Publish("SystemInitialized", "System initialization completed");
                
                Console.WriteLine("[SystemCoordinator] 系统协调器初始化完成");
            }
            catch (Exception ex)
            {
                // 处理初始化过程中的异常
                HandleGlobalException(ex);
                throw;
            }
        }
        
        /// <summary>
        /// 启动系统
        /// 在初始化完成后启动各个子系统，开始正常运行流程
        /// </summary>
        public void Start()
        {
            // 如果未初始化，抛出异常
            if (!_isInitialized)
            {
                throw new InvalidOperationException("系统未初始化，请先调用Initialize方法");
            }
            
            // 如果已经在运行，则直接返回
            if (_isRunning)
                return;
                
            try
            {
                // 启动通信协调器，开始与外部设备和系统的通信
                _communicationCoordinator.Start();
                
                // 启动工作流协调器，开始执行业务流程
                _workflowCoordinator.Start();
                
                _isRunning = true;
                
                // 发布系统启动事件，通知其他组件系统已启动
                EventHub.Instance.Publish("SystemStarted", new { 
                    MachineId = Config.Robot.MachineId,
                    Timestamp = DateTime.UtcNow,
                    Message = "System started" 
                });
                
                Console.WriteLine("[SystemCoordinator] 系统已启动");
            }
            catch (Exception ex)
            {
                // 处理启动过程中的异常
                HandleGlobalException(ex);
                throw;
            }
        }
        
        /// <summary>
        /// 停止系统
        /// 按顺序停止各个子系统，确保资源正确释放
        /// </summary>
        public void Stop()
        {
            // 如果系统未运行，则直接返回
            if (!_isRunning)
                return;
                
            try
            {
                // 先停止工作流协调器，确保业务流程正确结束
                _workflowCoordinator.Stop();
                
                // 再停止通信协调器，确保通信通道正确关闭
                _communicationCoordinator.Stop();
                
                _isRunning = false;
                
                // 发布系统停止事件，通知其他组件系统已停止
                EventHub.Instance.Publish("SystemStopped", new { 
                    MachineId = Config.Robot.MachineId,
                    Timestamp = DateTime.UtcNow,
                    Message = "System stopped" 
                });
                
                Console.WriteLine("[SystemCoordinator] 系统已停止");
            }
            catch (Exception ex)
            {
                // 处理停止过程中的异常
                HandleGlobalException(ex);
                throw;
            }
        }
        
        /// <summary>
        /// 获取通信协调器实例
        /// 用于外部组件访问通信功能
        /// </summary>
        /// <returns>通信协调器实例</returns>
        public CommunicationCoordinator GetCommunicationCoordinator()
        {
            return _communicationCoordinator;
        }
        
        /// <summary>
        /// 获取工作流协调器实例
        /// 用于外部组件访问业务流程功能
        /// </summary>
        /// <returns>工作流协调器实例</returns>
        public WorkflowCoordinator GetWorkflowCoordinator()
        {
            return _workflowCoordinator;
        }
        
        /// <summary>
        /// 获取机器人状态服务实例
        /// 用于外部组件访问机器人状态
        /// </summary>
        /// <returns>机器人状态服务实例</returns>
        public RobotStatusService GetRobotStatusService()
        {
            return _robotStatusService;
        }
        
        /// <summary>
        /// 获取SQLite状态帮助类实例
        /// 用于外部组件访问本地状态存储功能
        /// </summary>
        /// <returns>SQLite状态帮助类实例</returns>
        public SQLiteStatusHelper GetStatusHelper()
        {
            return _statusHelper;
        }
        
        /// <summary>
        /// 更新系统配置
        /// 安全地更新配置并保存到持久化存储
        /// </summary>
        /// <param name="updateAction">配置更新委托，用于修改配置</param>
        public void UpdateConfig(Action<AppConfig> updateAction)
        {
            // 调用配置服务更新配置
            _configService.UpdateAndSave(updateAction);
            // 发布配置更新事件，通知其他组件配置已更新
            EventHub.Instance.Publish("ConfigUpdated", new {
                MachineId = Config.Robot.MachineId,
                Timestamp = DateTime.UtcNow,
                Message = "Configuration has been updated"
            });
        }
        
        /// <summary>
        /// 订阅状态相关事件
        /// 用于记录和同步状态变更
        /// </summary>
        private void SubscribeToStatusEvents()
        {
            EventHub eventHub = EventHub.Instance;
            
            // 订阅后端状态确认事件
            eventHub.Subscribe("BackendStatusAck", data =>
            {
                try
                {
                    // 处理状态确认，更新本地日志状态
                    Console.WriteLine("[SystemCoordinator] 收到后端状态确认");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SystemCoordinator] 处理状态确认出错: {ex.Message}");
                }
            });
            
            // 订阅后端启动确认事件
            eventHub.Subscribe("BackendStartAck", data =>
            {
                try
                {
                    Console.WriteLine("[SystemCoordinator] 收到后端启动确认，系统就绪");
                    
                    // 发布系统就绪事件
                    eventHub.Publish("SystemReady", new {
                        MachineId = Config.Robot.MachineId,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SystemCoordinator] 处理启动确认出错: {ex.Message}");
                }
            });
            
            // 订阅系统异常事件
            eventHub.Subscribe("SystemException", data =>
            {
                try
                {
                    // 将异常记录到本地状态日志
                    var errorStatus = new RobotStatus
                    {
                        MachineId = Config.Robot.MachineId,
                        IsError = true,
                        ErrorMessage = (data as dynamic)?.Message?.ToString(),
                        LastUpdateTime = DateTime.UtcNow,
                        WorkStatus = "ERROR"
                    };
                    
                    _statusHelper.InsertStatusLog(errorStatus);
                    _statusHelper.UpsertLatestStatus(errorStatus);
                }
                catch
                {
                    // 防止在处理异常时再次异常
                }
            });
        }
        
        /// <summary>
        /// 处理全局异常
        /// 统一处理系统中的未捕获异常，确保系统稳定性
        /// </summary>
        /// <param name="exception">捕获到的异常</param>
        private void HandleGlobalException(Exception exception)
        {
            Console.WriteLine($"[SystemCoordinator] 系统发生异常: {exception?.Message}");
            // 发布系统异常事件，通知其他组件处理异常
            EventHub.Instance.Publish("SystemException", new 
            {
                Message = exception?.Message,
                Source = exception?.Source,
                StackTrace = exception?.StackTrace
            });
        }
    }
} 