using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.DataAccess;

namespace ShedulingNew.BusinessLogic.Task
{
    /// <summary>
    /// 任务数据管理类 - 负责断头任务数据的导入、导出和批量处理
    /// </summary>
    public class TaskDataManager
    {
        private static readonly TaskDataManager _instance = new TaskDataManager();
        private SQLiteHelper _sqliteHelper;
        private MySqlHelper _mysqlHelper;
        private EventHub _eventHub;
        private bool _isInitialized = false;

        // 单例模式
        private TaskDataManager() { }
        
        public static TaskDataManager Instance => _instance;

        /// <summary>
        /// 初始化TaskDataManager
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _sqliteHelper = SQLiteHelper.Instance;
                _mysqlHelper = new MySqlHelper();
                _eventHub = EventHub.Instance;
                
                _isInitialized = true;
                Console.WriteLine("[TaskDataManager] 初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskDataManager] 初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从MySQL导入断头数据到SQLite
        /// </summary>
        public async Task<string> ImportBrokenSpindlesFromMySqlAsync(string machineId)
        {
            CheckInitialized();
            
            try
            {
                // 生成任务批次ID
                string taskBatchId = $"TASK_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                
                // 模拟从MySQL查询断头数据
                Console.WriteLine($"[TaskDataManager] 从MySQL导入机器{machineId}的断头数据");
                
                // 查询模拟数据（实际项目中应查询MySQL）
                await Task.Delay(300); // 模拟网络延迟
                List<int> spindleDistances = new List<int> { 1050, 2230, 3450, 4670, 5890 };
                
                // 保存到SQLite
                await _sqliteHelper.SaveBrokenSpindlesAsync(machineId, spindleDistances, taskBatchId);
                
                // 发布事件
                _eventHub.Publish("BrokenSpindlesImported", new {
                    MachineId = machineId,
                    TaskBatchId = taskBatchId,
                    Count = spindleDistances.Count
                });
                
                return taskBatchId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskDataManager] 导入断头数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 导出修复任务结果到CSV
        /// </summary>
        public async Task ExportTaskResultToCsvAsync(string taskBatchId, string outputPath = null)
        {
            CheckInitialized();
            
            try
            {
                // 获取任务信息
                var taskInfo = await _sqliteHelper.GetTaskProgressAsync(taskBatchId);
                if (taskInfo == null)
                {
                    throw new Exception($"任务{taskBatchId}不存在");
                }
                
                // 设置输出文件路径
                string fileName = $"Task_{taskBatchId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string filePath = outputPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exports", fileName);
                
                // 确保导出目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                
                // 创建CSV内容
                List<string> lines = new List<string>();
                
                // 添加标题行
                lines.Add("任务批次ID,机器ID,开始时间,结束时间,状态,总锭子数,已完成锭子数,备注");
                
                // 添加任务信息行
                lines.Add($"{taskInfo.TaskBatchId},{taskInfo.MachineId},{taskInfo.StartTime},{taskInfo.EndTime}," +
                         $"{taskInfo.Status},{taskInfo.TotalSpindles},{taskInfo.CompletedSpindles},{taskInfo.Notes}");
                
                // 写入文件
                await File.WriteAllLinesAsync(filePath, lines);
                
                Console.WriteLine($"[TaskDataManager] 任务{taskBatchId}结果已导出到{filePath}");
                
                // 发布事件
                _eventHub.Publish("TaskResultExported", new {
                    TaskBatchId = taskBatchId,
                    FilePath = filePath
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskDataManager] 导出任务结果失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 清理过期任务数据
        /// </summary>
        public async Task CleanupOldTasksAsync(int daysToKeep = 30)
        {
            CheckInitialized();
            
            // 实际项目中应实现清理逻辑
            Console.WriteLine($"[TaskDataManager] 清理{daysToKeep}天前的任务数据");
            
            // TODO: 实现清理过期数据的逻辑
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        private void CheckInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("TaskDataManager未初始化，请先调用Initialize方法");
            }
        }
    }
} 