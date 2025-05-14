using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShedulingNew.BusinessLogic;
using ShedulingNew.DataAccess;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// 机器人状态服务 - 管理机器人状态数据的更新、查询和同步
    /// </summary>
    public class RobotStatusService
    {
        private EventHub _eventHub;
        private SQLiteHelper _sqliteHelper;
        private bool _isInitialized = false;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public RobotStatusService()
        {
            _eventHub = EventHub.Instance;
            _sqliteHelper = SQLiteHelper.Instance;
        }
        
        /// <summary>
        /// 初始化服务
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                // 确保SQLite初始化完成
                if (!_sqliteHelper.IsInitialized)
                {
                    _sqliteHelper.Initialize();
                }
                
                // 订阅事件
                SubscribeToEvents();
                
                _isInitialized = true;
                Console.WriteLine("[RobotStatusService] 初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 初始化失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 订阅机器人状态更新事件
            _eventHub.Subscribe("RobotStatusUpdated", async (data) =>
            {
                if (data is dynamic statusUpdate)
                {
                    try
                    {
                        await ProcessRobotStatusUpdateAsync(statusUpdate);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RobotStatusService] 处理状态更新失败: {ex.Message}");
                    }
                }
            });
            
            // 订阅任务状态变更事件，以更新机器人任务状态
            _eventHub.Subscribe("TaskStarted", async (data) =>
            {
                if (data is dynamic taskData)
                {
                    string robotId = taskData.RobotId?.ToString();
                    string taskId = taskData.TaskBatchId?.ToString();
                    
                    if (!string.IsNullOrEmpty(robotId) && !string.IsNullOrEmpty(taskId))
                    {
                        try
                        {
                            // 获取当前状态
                            var currentStatus = await _sqliteHelper.GetRobotLatestStatusAsync(robotId);
                            if (currentStatus != null)
                            {
                                // 更新状态为WORKING并关联任务ID
                                await _sqliteHelper.RecordRobotStatusAsync(
                                    robotId,
                                    "WORKING",
                                    Convert.ToInt32(currentStatus.power),
                                    currentStatus.location,
                                    currentStatus.direction,
                                    currentStatus.speed,
                                    taskId
                                );
                                
                                // 发布状态变更事件
                                _eventHub.Publish("RobotStatusChanged", new
                                {
                                    RobotId = robotId,
                                    Status = "WORKING",
                                    TaskId = taskId
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RobotStatusService] 更新机器人任务状态失败: {ex.Message}");
                        }
                    }
                }
            });
            
            // 订阅任务完成事件
            _eventHub.Subscribe("TaskCompleted", async (data) =>
            {
                if (data is dynamic taskData)
                {
                    string robotId = taskData.RobotId?.ToString();
                    
                    if (!string.IsNullOrEmpty(robotId))
                    {
                        try
                        {
                            // 获取当前状态
                            var currentStatus = await _sqliteHelper.GetRobotLatestStatusAsync(robotId);
                            if (currentStatus != null)
                            {
                                // 更新状态为IDLE并清除任务ID
                                await _sqliteHelper.RecordRobotStatusAsync(
                                    robotId,
                                    "IDLE",
                                    Convert.ToInt32(currentStatus.power),
                                    currentStatus.location,
                                    currentStatus.direction,
                                    currentStatus.speed,
                                    null // 清除任务ID
                                );
                                
                                // 发布状态变更事件
                                _eventHub.Publish("RobotStatusChanged", new
                                {
                                    RobotId = robotId,
                                    Status = "IDLE",
                                    TaskId = (string)null
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RobotStatusService] 更新机器人任务状态失败: {ex.Message}");
                        }
                    }
                }
            });
        }
        
        /// <summary>
        /// 处理机器人状态更新
        /// </summary>
        private async Task ProcessRobotStatusUpdateAsync(dynamic statusUpdate)
        {
            string robotId = statusUpdate.RobotId?.ToString();
            
            if (string.IsNullOrEmpty(robotId))
            {
                Console.WriteLine("[RobotStatusService] 无效的状态更新: 缺少RobotId");
                return;
            }
            
            try
            {
                // 提取状态数据
                string status = statusUpdate.Status?.ToString() ?? "UNKNOWN";
                int power = Convert.ToInt32(statusUpdate.Power ?? 0);
                string location = statusUpdate.Location?.ToString() ?? "UNKNOWN";
                string direction = statusUpdate.Direction?.ToString();
                double? speed = statusUpdate.Speed != null ? Convert.ToDouble(statusUpdate.Speed) : (double?)null;
                string taskId = statusUpdate.TaskId?.ToString();
                
                // 记录并更新状态
                await _sqliteHelper.RecordRobotStatusAsync(
                    robotId, status, power, location, direction, speed, taskId);
                
                // 发布状态变更事件，供其他组件使用
                _eventHub.Publish("RobotStatusChanged", new
                {
                    RobotId = robotId,
                    Status = status,
                    Power = power,
                    Location = location,
                    Direction = direction,
                    Speed = speed,
                    TaskId = taskId,
                    Timestamp = DateTime.Now
                });
                
                Console.WriteLine($"[RobotStatusService] 机器人[{robotId}]状态已更新: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 处理状态更新时出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取机器人当前状态
        /// </summary>
        public async Task<dynamic> GetRobotStatusAsync(string robotId)
        {
            CheckInitialized();
            
            try
            {
                return await _sqliteHelper.GetRobotLatestStatusAsync(robotId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 获取机器人状态失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取所有机器人的当前状态
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetAllRobotsStatusAsync()
        {
            CheckInitialized();
            
            try
            {
                return await _sqliteHelper.GetAllRobotsLatestStatusAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 获取所有机器人状态失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取机器人状态历史记录
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetRobotStatusHistoryAsync(
            string robotId, int limit = 100)
        {
            CheckInitialized();
            
            try
            {
                return await _sqliteHelper.GetRobotStatusLogsAsync(robotId, limit);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 获取机器人状态历史失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 同步待上报的状态数据
        /// </summary>
        public async Task SyncPendingStatusLogsAsync()
        {
            CheckInitialized();
            
            try
            {
                // 获取所有待同步的状态记录
                var pendingLogs = await _sqliteHelper.GetPendingSyncStatusLogsAsync(50);
                
                foreach (var log in pendingLogs)
                {
                    try
                    {
                        // 在实际应用中，这里应该调用后端API上报数据
                        // 此处简化为打印日志
                        Console.WriteLine($"[RobotStatusService] 同步状态记录: " +
                            $"机器人[{log.robot_id}], 状态[{log.status}], 时间[{log.timestamp}]");
                        
                        // 模拟上报成功
                        bool success = true;
                        string errorMsg = null;
                        
                        // 更新同步状态
                        await _sqliteHelper.UpdateStatusLogSyncResultAsync(
                            Convert.ToInt64(log.id), success, errorMsg);
                    }
                    catch (Exception ex)
                    {
                        // 更新同步失败状态
                        await _sqliteHelper.UpdateStatusLogSyncResultAsync(
                            Convert.ToInt64(log.id), false, ex.Message);
                        
                        Console.WriteLine($"[RobotStatusService] 同步状态记录失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 同步状态记录时出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 手动更新机器人状态
        /// </summary>
        public async Task UpdateRobotStatusAsync(string robotId, string status, int power, 
            string location, string direction = null, double? speed = null, string taskId = null)
        {
            CheckInitialized();
            
            try
            {
                // 记录并更新状态
                await _sqliteHelper.RecordRobotStatusAsync(
                    robotId, status, power, location, direction, speed, taskId);
                
                // 发布状态变更事件
                _eventHub.Publish("RobotStatusChanged", new
                {
                    RobotId = robotId,
                    Status = status,
                    Power = power,
                    Location = location,
                    Direction = direction,
                    Speed = speed,
                    TaskId = taskId,
                    Timestamp = DateTime.Now
                });
                
                Console.WriteLine($"[RobotStatusService] 手动更新机器人[{robotId}]状态: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RobotStatusService] 手动更新状态失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 检查是否初始化
        /// </summary>
        private void CheckInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("[RobotStatusService] 服务未初始化，请先调用Initialize方法");
            }
        }
    }
} 