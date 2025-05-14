using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShedulingNew.BusinessLogic.Services;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.DataAccess;

namespace ShedulingNew.BusinessLogic.Task
{
    public enum TaskStage
    {
        Idle,
        WaitingForAGVToSwitchPoint,
        AtSwitchPoint,
        SendingBrokenSpindle,
        AwaitingPLCArrival,
        TriggeringRollers,
        AwaitingRepairDone,
        AllDone,
        ReturningToWaitPoint
    }

    public class TaskController
    {
        private static readonly TaskController _instance = new TaskController();
        public static TaskController Instance => _instance;

        private TaskStage _currentStage = TaskStage.Idle;
        private Queue<int> _spindleNumbers = new Queue<int>(); // 修改：锭号队列
        private string _currentSideNumber = ""; // 修改：当前边序号
        private string _currentTaskBatchId = "";
        private int _currentSpindleNumber = 0; // 修改：当前锭号
        private bool _isInitialized = false;
        private string _agvId = "AGV1"; // 默认AGV ID，将通过配置更新

        private AGVService _agvService = new AGVService();
        private PLCService _plcService = new PLCService();
        private MySqlHelper _dbHelper = new MySqlHelper();
        private SQLiteHelper _sqliteHelper = SQLiteHelper.Instance;
        private EventHub _eventHub = EventHub.Instance;
        private ConfigService _configService = ConfigService.Instance; // 添加配置服务引用

        // PLC线圈地址配置
        private const string PLC_SWITCH_POINT_ARRIVED = "M500"; // 告知PLC到达切换点信号
        private const string PLC_SPINDLE_POSITION = "D500"; // 锭子距离寄存器地址
        private const string PLC_TRIGGER_ROLLERS = "M501"; // 左右皮辊信号
        private const string PLC_RETURN_SWITCH_POINT = "M500"; // 返回切换点信号(复用M500)
        private const string PLC_SPINDLE_ARRIVAL = "M600"; // PLC到达锭位信号
        private const string PLC_REPAIR_DONE = "M601"; // 接头完成信号
        private const string PLC_BACK_TO_SWITCH_POINT = "M602"; // PLC告知到达权限切换点

        private TaskController() { }

        /// <summary>
        /// 初始化任务控制器
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // 初始化SQLite数据库
                _sqliteHelper.Initialize();
                
                // 初始化数据同步服务
                DataAccess.DataSyncService.Instance.Initialize();
                
                // 从配置文件加载AGV ID
                _agvId = _configService.Config.Agv.AgvId; // 使用专门的AgvId配置
                Console.WriteLine($"[TaskController] 使用AGV ID: {_agvId}");
                
                _isInitialized = true;
                
                // 订阅相关事件
                SubscribeToEvents();
                
                Console.WriteLine("[TaskController] 初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskController] 初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 订阅相关事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 订阅SystemCommandReceived事件，该事件由CommunicationCoordinator处理后转发
            _eventHub.Subscribe("SystemCommandReceived", async (data) =>
            {
                try
                {
                    dynamic command = data as dynamic;
                    if (command != null && command.Type == "StartRepairTask")
                    {
                        string sideNumber = command.SideNumber;
                        Console.WriteLine($"[TaskController] 收到启动接头任务命令: 边序号={sideNumber}");
                        await StartNewTaskAsync(sideNumber);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TaskController] 处理系统命令事件出错: {ex.Message}");
                }
            });

            // 订阅PLC状态变化事件
            _eventHub.Subscribe("PLCDataChanged", async (data) =>
            {
                try
                {
                    // 检查PLC信号
                    if (_currentStage == TaskStage.AwaitingPLCArrival)
                    {
                        // 检查PLC是否已到达锭位
                        bool arrivedAtSpindle = await _plcService.ReadCoilAsync(PLC_SPINDLE_ARRIVAL);
                        if (arrivedAtSpindle)
                        {
                            await OnPLCConfirmedArrival();
                        }
                    }
                    else if (_currentStage == TaskStage.TriggeringRollers)
                    {
                        // 检查修复是否完成
                        bool repairDone = await _plcService.ReadCoilAsync(PLC_REPAIR_DONE);
                        if (repairDone)
                        {
                            await OnRepairDone();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TaskController] 处理PLC状态变化事件时出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 启动接头任务
        /// </summary>
        public async System.Threading.Tasks.Task StartNewTaskAsync(string sideNumber)
        {
            //这里的sideNumber是边序号
            Console.WriteLine("[TaskController] 启动接头任务: 边序号=" + sideNumber); 

            // 利用时间戳生成唯一任务id
            _currentTaskBatchId = $"TASK_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            _currentSideNumber = sideNumber;
            // 初始化任务状态为等待AGV到达权限切换点
            _currentStage = TaskStage.WaitingForAGVToSwitchPoint;

            try
            {
                // 从边序号转换为机器ID和边位置
                var machineInfo = _dbHelper.ConvertSideNumberToMachineInfo(sideNumber);
                int machineNumber = machineInfo.machineNumber;
                string side = machineInfo.side;
                string machineId = machineNumber.ToString();
                
                Console.WriteLine($"[TaskController] 边序号 {sideNumber} 对应机器 {machineId} 的 {side} 边");
                
                // 从MySQL获取细纱机锭位权限切换点信息
                var switchPoints = await _dbHelper.GetSwitchPointsAsync(machineId);
                string entrySwitchPointId = switchPoints.EntrySwitchPointId;
                
                if (string.IsNullOrEmpty(entrySwitchPointId))
                {
                    Console.WriteLine($"[TaskController] 警告: 未找到机器 {machineId} 的入口切换点");
                }
                else
                {
                    Console.WriteLine($"[TaskController] 获取到机器 {machineId} 的入口切换点: {entrySwitchPointId}");
                }
                // 通知AGV前往权限切换点，传入切换点ID参数
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(entrySwitchPointId))
                {
                    parameters.Add("SwitchPointId", entrySwitchPointId);
                }
                // 使用从配置加载的AGV ID，而不是硬编码"AGV1"
                _agvService.SendCommandToAGV(_agvId, "GoToSwitchPoint", parameters);
                
                // 发布任务启动事件
                _eventHub.Publish("TaskStarted", new { 
                    SideNumber = sideNumber,
                    TaskBatchId = _currentTaskBatchId
                });
            }
            catch (Exception ex)
            {
                _currentStage = TaskStage.Idle;
                Console.WriteLine($"[TaskController] 启动任务失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// AGV到达权限切换点
        /// </summary>
        public async System.Threading.Tasks.Task OnAGVAtSwitchPoint()
        {
            _currentStage = TaskStage.AtSwitchPoint;
            Console.WriteLine("[TaskController] AGV已到达权限切换点");
            
            // 通知PLC - AGV已到达切换点
            await _plcService.WriteCoilAsync(PLC_SWITCH_POINT_ARRIVED, true);

            try 
            {
                // 使用数据同步服务获取断头数据并同步到本地缓存
                var dataSyncService = DataAccess.DataSyncService.Instance;
                List<int> brokenSpindles = await dataSyncService.SyncBrokenSpindlesAsync(_currentSideNumber, _currentTaskBatchId);
                
                if (brokenSpindles.Count == 0)
                {
                    Console.WriteLine($"[TaskController] 警告：边序号 {_currentSideNumber} 没有待处理的断头数据");
                }

                // 从缓存读取数据（确保使用缓存的数据）
                var distances = await dataSyncService.GetBrokenSpindleDistancesFromCacheAsync(_currentSideNumber, _currentTaskBatchId);
                _spindleNumbers = new Queue<int>(distances);
                
                // 发送第一个断头锭子位置
                if (_spindleNumbers.Count > 0)
                {
                    await SendNextSpindleDistance();
                }
                else
                {
                    Console.WriteLine($"[TaskController] 错误：无法获取断头数据，无法继续任务");
                    // 可能需要错误处理或重置任务状态
                    _currentStage = TaskStage.AllDone;
                    _eventHub.Publish("TaskError", new {
                        SideNumber = _currentSideNumber,
                        ErrorMessage = "无法获取断头数据"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskController] 获取断头数据失败: {ex.Message}");
                // 处理错误情况
                _currentStage = TaskStage.Idle;
                _eventHub.Publish("TaskError", new {
                    SideNumber = _currentSideNumber,
                    ErrorMessage = ex.Message
                });
            }
        }

        /// <summary>
        /// PLC确认已到达锭位
        /// </summary>
        public async System.Threading.Tasks.Task OnPLCConfirmedArrival()
        {
            _currentStage = TaskStage.TriggeringRollers;
            Console.WriteLine("[TaskController] PLC已到达锭位，启动皮辊");
            
            // 通知PLC启动皮辊
            await _plcService.WriteCoilAsync(PLC_TRIGGER_ROLLERS, true);
            
            // 发布事件
            _eventHub.Publish("SpindleRepairing", new {
                SideNumber = _currentSideNumber,
                SpindleNumber = _currentSpindleNumber
            });
        }

        /// <summary>
        /// 修复完成，处理下一个锭位
        /// </summary>
        public async System.Threading.Tasks.Task OnRepairDone()
        {
            // 关闭皮辊信号
            await _plcService.WriteCoilAsync(PLC_TRIGGER_ROLLERS, false);
            
            // 更新当前锭位状态为已完成
            if (_currentSpindleNumber > 0)
            {
                // 使用数据同步服务更新状态（同时更新MySQL和SQLite）
                var dataSyncService = DataAccess.DataSyncService.Instance;
                await dataSyncService.UpdateSpindleStatusAsync(
                    _currentSideNumber,
                    _currentSpindleNumber,
                    _currentTaskBatchId,
                    "Completed"
                );
                
                // 发布事件
                _eventHub.Publish("SpindleRepaired", new {
                    SideNumber = _currentSideNumber,
                    SpindleNumber = _currentSpindleNumber
                });
            }

            // 检查是否还有更多锭位需要处理
            if (_spindleNumbers.Count > 0)
            {
                Console.WriteLine("[TaskController] 修复完成，继续下一个锭位");
                await SendNextSpindleDistance();
            }
            else
            {
                Console.WriteLine("[TaskController] 当前任务锭位全部完成");
                _currentStage = TaskStage.AllDone;
                
                // 通知PLC返回切换点
                await _plcService.WriteCoilAsync(PLC_RETURN_SWITCH_POINT, true);
                
                // 发布事件
                _eventHub.Publish("TaskCompleted", new {
                    SideNumber = _currentSideNumber,
                    TaskBatchId = _currentTaskBatchId
                });
            }
        }

        /// <summary>
        /// PLC已返回切换点
        /// </summary>
        public async System.Threading.Tasks.Task OnPLCBackToSwitchPoint()
        {
            _currentStage = TaskStage.ReturningToWaitPoint;
            
            // 重置PLC信号
            await _plcService.WriteCoilAsync(PLC_RETURN_SWITCH_POINT, false);
            await _plcService.WriteCoilAsync(PLC_SWITCH_POINT_ARRIVED, false);
            
            // 通知AGV返回等待点
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            // 从配置获取默认等待点ID
            string waitPointId = _configService.Config.Agv.DefaultWaitPosition;
            parameters.Add("WaitPointId", waitPointId);
            
            // 使用从配置加载的AGV ID
            _agvService.SendCommandToAGV(_agvId, "GoToWaitPoint", parameters);
            
            // 发布事件
            _eventHub.Publish("ReturningToWaitPoint", new {
                SideNumber = _currentSideNumber
            });
        }

        /// <summary>
        /// AGV已返回等待点，任务完全结束
        /// </summary>
        public void OnAGVBackToWaitPoint()
        {
            _currentStage = TaskStage.Idle;
            _currentSideNumber = "";
            _currentTaskBatchId = "";
            _currentSpindleNumber = 0;
            
            Console.WriteLine("[TaskController] AGV已返回等待点，任务完全结束");
            
            // 发布事件
            _eventHub.Publish("AGVReady", new { Status = "Ready" });
        }

        /// <summary>
        /// 发送下一个断头锭子距离到PLC
        /// </summary>
        private async System.Threading.Tasks.Task SendNextSpindleDistance()
        {
            if (_spindleNumbers.TryDequeue(out var distance))
            {
                _currentStage = TaskStage.SendingBrokenSpindle;
                _currentSpindleNumber = distance;
                
                Console.WriteLine("[TaskController] 发送锭位距离: " + distance);
                
                // 更新当前锭位状态为处理中
                var dataSyncService = DataAccess.DataSyncService.Instance;
                await dataSyncService.UpdateSpindleStatusAsync(
                    _currentSideNumber,
                    distance,
                    _currentTaskBatchId,
                    "Processing"
                );
                
                // 发送到PLC寄存器
                await _plcService.WriteRegisterAsync(PLC_SPINDLE_POSITION, (short)distance);
                
                // 切换状态
                _currentStage = TaskStage.AwaitingPLCArrival;
                
                // 发布事件
                _eventHub.Publish("SpindleSent", new {
                    SideNumber = _currentSideNumber,
                    SpindleNumber = distance
                });
            }
        }

        /// <summary>
        /// 从MySQL数据库加载断头数据
        /// </summary>
        private async System.Threading.Tasks.Task<List<int>> LoadBrokenSpindlesFromMySqlAsync(string sideNumber)
        {
            // 使用数据同步服务加载断头数据
            Console.WriteLine("[TaskController] 从MySQL加载断头数据...");
            
            try
            {
                var dataSyncService = DataAccess.DataSyncService.Instance;
                // 使用临时任务批次ID，仅用于获取数据，不会实际创建任务
                string tempTaskBatchId = $"TEMP_{DateTime.Now.Ticks}";
                return await dataSyncService.SyncBrokenSpindlesAsync(sideNumber, tempTaskBatchId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskController] 从MySQL加载断头数据失败: {ex.Message}");
                return new List<int>(); // 返回空列表，表示没有数据
            }
        }

        /// <summary>
        /// 获取当前任务状态
        /// </summary>
        public TaskStage GetCurrentStage()
        {
            return _currentStage;
        }

        /// <summary>
        /// 获取当前任务进度
        /// </summary>
        public async Task<dynamic> GetCurrentTaskProgress()
        {
            if (string.IsNullOrEmpty(_currentTaskBatchId))
            {
                return new { Status = "NoActiveTask" };
            }
            
            try
            {
                var dataSyncService = DataAccess.DataSyncService.Instance;
                return await dataSyncService.GetTaskProgressAsync(_currentTaskBatchId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskController] 获取任务进度失败: {ex.Message}");
                return new { Status = "Error", Message = ex.Message };
            }
        }
    }
}
