// CommunicationCoordinator.cs
using System;
using System.Threading.Tasks;
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Services;
using ShedulingNew.BusinessLogic.Task;
using ShedulingNew.DataAccess; // 添加对DataAccess命名空间的引用

namespace ShedulingNew.Coordinators
{
    /// <summary>
    /// 通信协调器，专门协调各通信模块(PLC、AGV、后端)
    /// 作为系统与外部设备和系统通信的中心枢纽，管理所有通信服务的生命周期
    /// 负责协调PLC通信、AGV通信和与后端系统的通信
    /// </summary>
    public class CommunicationCoordinator
    {
        private PLCService _plcService;        // PLC通信服务，处理与可编程逻辑控制器的通信
        private AGVService _agvService;        // AGV通信服务，处理与自动导引车的通信
        private BackendService _backendService; // 后端通信服务，处理与服务器端的通信
        private EventHub _eventHub;            // 事件总线，用于事件发布和订阅
        private MySqlHelper _mySqlHelper;      // MySQL数据库访问帮助类

        /// <summary>
        /// 构造函数
        /// 初始化事件总线实例，为后续的事件订阅做准备
        /// </summary>
        public CommunicationCoordinator()
        {
            _eventHub = EventHub.Instance; // 获取事件总线单例实例
        }

        /// <summary>
        /// 初始化通信模块
        /// 创建并初始化各通信服务，建立事件订阅关系
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 初始化各通信服务实例
                _plcService = new PLCService();      // 创建PLC通信服务
                _agvService = new AGVService();      // 创建AGV通信服务
                _backendService = new BackendService(); // 创建后端通信服务
                _mySqlHelper = new MySqlHelper();    // 创建MySQL数据库访问帮助类
                _mySqlHelper.Initialize();           // 初始化MySQL连接

                // 订阅各服务的事件，建立事件处理链
                SubscribeToServiceEvents();

                Console.WriteLine("通信协调器初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通信协调器初始化失败: {ex.Message}");
                throw; // 重新抛出异常，让上层处理
            }
        }

        /// <summary>
        /// 启动通信服务
        /// 按顺序启动各个通信服务，开始与外部设备和系统的通信
        /// </summary>
        public void Start()
        {
            try
            {
                // 启动PLC通信服务，开始与PLC设备通信
                _plcService.Start();
                // 启动AGV通信服务，开始与AGV设备通信
                _agvService.Start();
                // 启动后端通信服务，开始与后端系统通信
                _backendService.Start();

                Console.WriteLine("所有通信服务启动完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通信服务启动失败: {ex.Message}");
                throw; // 重新抛出异常，让上层处理
            }
        }

        /// <summary>
        /// 停止通信服务
        /// 按顺序停止各个通信服务，安全关闭通信连接
        /// </summary>
        public void Stop()
        {
            try
            {
                // 停止PLC通信服务
                _plcService.Stop();
                // 停止AGV通信服务
                _agvService.Stop();
                // 停止后端通信服务
                _backendService.Stop();

                Console.WriteLine("所有通信服务停止完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通信服务停止失败: {ex.Message}");
                throw; // 重新抛出异常，让上层处理
            }
        }

        /// <summary>
        /// 订阅各服务事件
        /// 为各种事件建立处理逻辑，处理来自不同通信服务的事件
        /// </summary>
        private void SubscribeToServiceEvents()
        {
            // 订阅PLC数据变化事件
            _eventHub.Subscribe("PLCDataChanged", async data =>
            {
                try
                {
                    var signal = data.ToString();

                    // 根据PLC信号类型分发处理
                    if (signal.Contains("ArrivedSpindle"))
                    {
                        // PLC已到达锭子位置
                        await TaskController.Instance.OnPLCConfirmedArrival();
                        _eventHub.Publish("PLCAtSpindle", data);
                    }
                    else if (signal.Contains("RepairDone"))
                    {
                        // PLC修复完成信号
                        await TaskController.Instance.OnRepairDone();
                        _eventHub.Publish("PLCRepairDone", data);
                    }
                    else if (signal.Contains("BackToSwitchPoint"))
                    {
                        // PLC返回切换点信号
                        await TaskController.Instance.OnPLCBackToSwitchPoint();
                        _eventHub.Publish("PLCBackToSwitchPoint", data);
                    }

                    // 广播系统数据更新
                    _eventHub.Publish("SystemDataUpdated", data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommunicationCoordinator] 处理PLC数据变化出错: {ex.Message}");
                }
            });

            // 订阅AGV状态变化事件
            _eventHub.Subscribe("AGVStatusChanged", async data =>
            {
                try
                {
                    // 根据AGV状态变化分发处理
                    if (data.ToString().Contains("AtSwitchPoint"))
                    {
                        // AGV已到达切换点
                        await TaskController.Instance.OnAGVAtSwitchPoint();
                        _eventHub.Publish("AGVAtSwitchPoint", data);
                    }
                    else if (data.ToString().Contains("BackToWaitPoint"))
                    {
                        // AGV已返回等待点
                        TaskController.Instance.OnAGVBackToWaitPoint();
                        _eventHub.Publish("AGVBackToWaitPoint", data);
                    }

                    // 广播系统数据更新
                    _eventHub.Publish("SystemDataUpdated", data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommunicationCoordinator] 处理AGV状态变化出错: {ex.Message}");
                }
            });

            // 订阅AGV到达目标点事件
            _eventHub.Subscribe("AGVTargetReached", async data =>
            {
                try
                {
                    // 解析目标点信息
                    var targetInfo = data as dynamic;
                    if (targetInfo != null)
                    {
                        uint targetPointId = targetInfo.TargetPointId;
                        Console.WriteLine($"[CommunicationCoordinator] AGV到达目标点: {targetPointId}");

                        // 查询目标点类型，判断是切换点还是等待点
                        string targetPointType = await GetTargetPointTypeAsync(targetPointId);
                        
                        if (targetPointType == "SwitchPoint")
                        {
                            // AGV到达切换点，触发切换点处理流程
                            Console.WriteLine($"[CommunicationCoordinator] 确认AGV到达切换点 ID: {targetPointId}");
                            await TaskController.Instance.OnAGVAtSwitchPoint();
                            _eventHub.Publish("AGVAtSwitchPoint", data);
                        }
                        else if (targetPointType == "WaitPoint")
                        {
                            // AGV返回等待点，触发等待点处理流程
                            Console.WriteLine($"[CommunicationCoordinator] 确认AGV返回等待点 ID: {targetPointId}");
                            TaskController.Instance.OnAGVBackToWaitPoint();
                            _eventHub.Publish("AGVBackToWaitPoint", data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommunicationCoordinator] 处理AGV到达目标点事件出错: {ex.Message}");
                }
            });

            // 订阅后端命令接收事件
            _eventHub.Subscribe("BackendCommandReceived", async data =>
            {
                try
                {
                    // 解析后端命令
                    var command = data as dynamic;
                    if (command != null && command.Type == "StartRepairTask")
                    {
                        // 收到启动修复任务命令，记录日志
                        string sideNumber = command.SideNumber;
                        Console.WriteLine($"[CommunicationCoordinator] 收到后端启动修复任务命令: 机器ID={sideNumber}");
                        
                        // 不再直接调用TaskController，避免重复处理
                        // 移除: await TaskController.Instance.StartNewTaskAsync(machineId);
                    }

                    // 广播系统命令接收
                    _eventHub.Publish("SystemCommandReceived", data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommunicationCoordinator] 处理后端命令出错: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// 配置PLC服务
        /// 设置PLC通信的IP地址和端口
        /// </summary>
        /// <param name="ipAddress">PLC的IP地址</param>
        /// <param name="port">PLC的端口，默认为502</param>
        public void ConfigurePLCService(string ipAddress, int port = 502)
        {
            _plcService.Configure(ipAddress, port);
            Console.WriteLine($"已配置PLC服务，地址: {ipAddress}, 端口: {port}");
        }
        
        /// <summary>
        /// 直接读取PLC线圈状态
        /// 异步读取指定地址的线圈（开关量）状态
        /// </summary>
        /// <param name="address">PLC线圈地址，格式如"M100"</param>
        /// <returns>线圈状态，true表示ON，false表示OFF</returns>
        public async Task<bool> ReadPLCCoilAsync(string address)
        {
            return await _plcService.ReadCoilAsync(address);
        }
        
        /// <summary>
        /// 直接写入PLC线圈状态
        /// 异步写入指定地址的线圈（开关量）状态
        /// </summary>
        /// <param name="address">PLC线圈地址，格式如"M100"</param>
        /// <param name="value">要写入的状态，true表示ON，false表示OFF</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> WritePLCCoilAsync(string address, bool value)
        {
            return await _plcService.WriteCoilAsync(address, value);
        }
        
        /// <summary>
        /// 直接读取PLC寄存器
        /// 异步读取指定地址的寄存器（数值量）
        /// </summary>
        /// <param name="address">PLC寄存器地址，格式如"D100"</param>
        /// <returns>寄存器中的数值</returns>
        public async Task<short> ReadPLCRegisterAsync(string address)
        {
            return await _plcService.ReadRegisterAsync(address);
        }
        
        /// <summary>
        /// 直接写入PLC寄存器
        /// 异步写入指定地址的寄存器（数值量）
        /// </summary>
        /// <param name="address">PLC寄存器地址，格式如"D100"</param>
        /// <param name="value">要写入的数值</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> WritePLCRegisterAsync(string address, short value)
        {
            return await _plcService.WriteRegisterAsync(address, value);
        }

        /// <summary>
        /// 根据目标点ID获取目标点类型
        /// </summary>
        /// <param name="targetPointId">目标点ID</param>
        /// <returns>目标点类型: "EntrySwitchPoint", "ExitSwitchPoint", "WaitPoint"或"Unknown"</returns>
        private async Task<string> GetTargetPointTypeAsync(uint targetPointId)
        {
            try
            {
                // 使用MySqlHelper从数据库中查询点位类型
                string pointType = await _mySqlHelper.GetPointTypeAsync(targetPointId);
                
                // 如果数据库返回了特定的点位类型，则直接使用
                if (pointType != "Unknown")
                {
                    // 根据不同的点位类型，可以进一步处理
                    if (pointType == "EntrySwitchPoint" || pointType == "ExitSwitchPoint")
                    {
                        // 切换点需要确定是进入型还是退出型，但在判断是否需要调用OnAGVAtSwitchPoint时，
                        // 所有切换点类型都应该调用此方法
                        Console.WriteLine($"[CommunicationCoordinator] 点位 {targetPointId} 是{pointType}类型");
                        return "SwitchPoint"; // 返回统一的SwitchPoint类型，便于后续处理
                    }
                    else if (pointType == "WaitPoint")
                    {
                        Console.WriteLine($"[CommunicationCoordinator] 点位 {targetPointId} 是等待点");
                        return "WaitPoint";
                    }
                    else 
                    {
                        Console.WriteLine($"[CommunicationCoordinator] 点位 {targetPointId} 是{pointType}类型，未处理的类型");
                        return pointType;
                    }
                }
                
                // 数据库没有该点位信息时，使用备用的硬编码判断方法
                Console.WriteLine($"[CommunicationCoordinator] 数据库中未找到点位 {targetPointId} 的类型，使用备用方法判断");
                
                // 假设1001-1999是切换点
                if (targetPointId >= 1001 && targetPointId < 2000)
                {
                    return "SwitchPoint";
                }
                // 假设2001-2999是等待点
                else if (targetPointId >= 2001 && targetPointId < 3000)
                {
                    return "WaitPoint";
                }
                
                // 无法确定类型
                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommunicationCoordinator] 获取目标点类型出错: {ex.Message}");
                return "Unknown";
            }
        }
    }
}
