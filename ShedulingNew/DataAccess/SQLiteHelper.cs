using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using ShedulingNew.Coordinators;

namespace ShedulingNew.DataAccess
{
    /// <summary>
    /// SQLite数据库帮助类 - 用于本地缓存断头任务数据
    /// </summary>
    public class SQLiteHelper
    {
        private static readonly SQLiteHelper _instance = new SQLiteHelper();
        private string _dbFilePath;
        private string _connectionString;
        public bool IsInitialized { get; private set; } = false;
        private bool _isInitialized = false;

        // 单例模式
        private SQLiteHelper() { }
        
        public static SQLiteHelper Instance => _instance;

        /// <summary>
        /// 初始化SQLite数据库
        /// </summary>
        /// <param name="dbFilePath">数据库文件路径，如果为null则使用配置中的路径</param>
        public void Initialize(string dbFilePath = null)
        {
            try
            {
                // 如果没有指定路径，则从配置文件中获取SQLite连接字符串
                if (dbFilePath == null)
                {
                    _connectionString = SystemCoordinator.Instance.Config.Database.SQLiteConnectionString;
                    // 从连接字符串中提取数据库文件路径
                    var connStringParts = _connectionString.Split(';');
                    foreach (var part in connStringParts)
                    {
                        if (part.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                        {
                            _dbFilePath = part.Substring("Data Source=".Length);
                            break;
                        }
                    }
                }
                else
                {
                    _dbFilePath = dbFilePath;
                    _connectionString = $"Data Source={_dbFilePath};Version=3;";
                }

                // 确保数据库文件存在
                EnsureDatabaseExists();
                
                // 测试连接
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    Console.WriteLine("SQLite数据库连接成功");
                }
                
                _isInitialized = true;
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SQLite数据库初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 确保数据库文件和表结构存在
        /// </summary>
        private void EnsureDatabaseExists()
        {
            bool isNewDb = !File.Exists(_dbFilePath);
            
            // 如果是新数据库或者数据库不存在，创建它
            if (isNewDb)
            {
                SQLiteConnection.CreateFile(_dbFilePath);
            }
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 创建必要的表
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS BrokenSpindles (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SideNumber TEXT NOT NULL,          -- 修改：使用边序号替代MachineId
                            SpindleNumber INTEGER NOT NULL,    -- 修改：使用锭号替代SpindleDistance
                            Status TEXT DEFAULT 'Pending',
                            CreatedAt TEXT DEFAULT (datetime('now', 'localtime')),
                            UpdatedAt TEXT,
                            TaskBatchId TEXT,
                            ProcessOrder INTEGER,
                            Notes TEXT
                        );
                        
                        CREATE TABLE IF NOT EXISTS TaskHistory (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            TaskBatchId TEXT NOT NULL,
                            SideNumber TEXT NOT NULL,          -- 修改：使用边序号替代MachineId
                            StartTime TEXT,
                            EndTime TEXT,
                            Status TEXT,
                            TotalSpindles INTEGER,
                            CompletedSpindles INTEGER,
                            Notes TEXT
                        );
                        
                        CREATE TABLE IF NOT EXISTS robot_status_log (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            robot_id TEXT NOT NULL,
                            timestamp DATETIME NOT NULL,
                            status TEXT NOT NULL,
                            power INTEGER NOT NULL,
                            location TEXT NOT NULL,
                            direction TEXT,
                            speed REAL,
                            sync_status TEXT DEFAULT 'PENDING',
                            error_msg TEXT
                        );
                        
                        CREATE TABLE IF NOT EXISTS robot_latest_status (
                            robot_id TEXT PRIMARY KEY,
                            timestamp DATETIME NOT NULL,
                            status TEXT NOT NULL,
                            power INTEGER NOT NULL,
                            location TEXT NOT NULL,
                            direction TEXT,
                            speed REAL,
                            task_id TEXT
                        );";
                    command.ExecuteNonQuery();
                }
                
                if (isNewDb)
                {
                    Console.WriteLine("SQLite数据库及表结构创建成功");
                }
            }
        }

        /// <summary>
        /// 保存单个断头锭子数据到本地缓存
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumber">锭号</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <param name="status">状态，默认为"Pending"</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SaveBrokenSpindleAsync(string sideNumber, int spindleNumber, string taskBatchId, string status = "Pending")
        {
            CheckInitialized();
            
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 检查是否已存在相同的记录
                    var existingRecord = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT Id FROM BrokenSpindles 
                        WHERE SideNumber = @SideNumber 
                        AND SpindleNumber = @SpindleNumber 
                        AND TaskBatchId = @TaskBatchId",
                        new { SideNumber = sideNumber, SpindleNumber = spindleNumber, TaskBatchId = taskBatchId });
                    
                    if (existingRecord != null)
                    {
                        // 记录已存在，更新状态
                        await connection.ExecuteAsync(
                            @"UPDATE BrokenSpindles 
                            SET Status = @Status, UpdatedAt = @UpdatedAt 
                            WHERE SideNumber = @SideNumber 
                            AND SpindleNumber = @SpindleNumber 
                            AND TaskBatchId = @TaskBatchId",
                            new { 
                                SideNumber = sideNumber, 
                                SpindleNumber = spindleNumber, 
                                TaskBatchId = taskBatchId, 
                                Status = status,
                                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                        
                        Console.WriteLine($"已更新边序号[{sideNumber}]断头锭号[{spindleNumber}]的状态为[{status}]");
                    }
                    else
                    {
                        // 获取当前任务的最大处理顺序
                        var maxProcessOrder = await connection.QueryFirstOrDefaultAsync<int?>(
                            @"SELECT MAX(ProcessOrder) FROM BrokenSpindles 
                            WHERE SideNumber = @SideNumber AND TaskBatchId = @TaskBatchId",
                            new { SideNumber = sideNumber, TaskBatchId = taskBatchId }) ?? 0;
                        
                        // 添加新记录
                        await connection.ExecuteAsync(
                            @"INSERT INTO BrokenSpindles 
                            (SideNumber, SpindleNumber, TaskBatchId, Status, ProcessOrder) 
                            VALUES 
                            (@SideNumber, @SpindleNumber, @TaskBatchId, @Status, @ProcessOrder)",
                            new { 
                                SideNumber = sideNumber, 
                                SpindleNumber = spindleNumber, 
                                TaskBatchId = taskBatchId, 
                                Status = status,
                                ProcessOrder = maxProcessOrder + 1
                            });
                        
                        // 检查任务历史记录是否存在
                        var taskRecord = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            @"SELECT Id FROM TaskHistory WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                            new { TaskBatchId = taskBatchId, SideNumber = sideNumber });
                        
                        if (taskRecord == null)
                        {
                            // 创建新的任务历史记录
                            await connection.ExecuteAsync(
                                @"INSERT INTO TaskHistory 
                                (TaskBatchId, SideNumber, StartTime, Status, TotalSpindles, CompletedSpindles) 
                                VALUES 
                                (@TaskBatchId, @SideNumber, @StartTime, @Status, @TotalSpindles, @CompletedSpindles)",
                                new { 
                                    TaskBatchId = taskBatchId, 
                                    SideNumber = sideNumber, 
                                    StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), 
                                    Status = "Started", 
                                    TotalSpindles = 1, 
                                    CompletedSpindles = 0
                                });
                        }
                        else
                        {
                            // 更新任务历史记录中的总锭子数
                            await connection.ExecuteAsync(
                                @"UPDATE TaskHistory 
                                SET TotalSpindles = TotalSpindles + 1 
                                WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                                new { TaskBatchId = taskBatchId, SideNumber = sideNumber });
                        }
                        
                        Console.WriteLine($"已添加边序号[{sideNumber}]的断头锭号[{spindleNumber}]到本地缓存");
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存断头锭子数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据边序号获取该任务的所有断头锭号，按处理顺序排序
        /// </summary>
        public async Task<List<int>> GetBrokenSpindleDistancesAsync(string sideNumber, string taskBatchId)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                var result = await connection.QueryAsync<int>(@"
                    SELECT SpindleNumber 
                    FROM BrokenSpindles 
                    WHERE SideNumber = @SideNumber 
                    AND TaskBatchId = @TaskBatchId
                    AND Status = 'Pending'
                    ORDER BY ProcessOrder ASC",
                    new { SideNumber = sideNumber, TaskBatchId = taskBatchId });
                
                return result.AsList();
            }
        }

        /// <summary>
        /// 更新锭子状态
        /// </summary>
        public async Task UpdateSpindleStatusAsync(string sideNumber, int spindleNumber, string taskBatchId, string status)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                await connection.ExecuteAsync(@"
                    UPDATE BrokenSpindles 
                    SET Status = @Status, UpdatedAt = @UpdatedAt
                    WHERE SideNumber = @SideNumber 
                    AND SpindleNumber = @SpindleNumber
                    AND TaskBatchId = @TaskBatchId",
                    new { 
                        SideNumber = sideNumber, 
                        SpindleNumber = spindleNumber, 
                        TaskBatchId = taskBatchId,
                        Status = status,
                        UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                
                // 如果状态是已完成，更新任务历史中的完成计数
                if (status == "Completed")
                {
                    await connection.ExecuteAsync(@"
                        UPDATE TaskHistory 
                        SET CompletedSpindles = CompletedSpindles + 1
                        WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                        new { TaskBatchId = taskBatchId, SideNumber = sideNumber });
                    
                    // 检查是否所有锭子都已完成
                    var taskInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT TotalSpindles, CompletedSpindles 
                        FROM TaskHistory 
                        WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                        new { TaskBatchId = taskBatchId, SideNumber = sideNumber });
                    
                    if (taskInfo != null && taskInfo.TotalSpindles == taskInfo.CompletedSpindles)
                    {
                        // 所有锭子都已完成，更新任务状态和结束时间
                        await connection.ExecuteAsync(@"
                            UPDATE TaskHistory 
                            SET Status = 'Completed', EndTime = @EndTime
                            WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                            new { 
                                TaskBatchId = taskBatchId, 
                                SideNumber = sideNumber,
                                EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                    }
                }
            }
        }

        /// <summary>
        /// 获取任务进度信息
        /// </summary>
        public async Task<dynamic> GetTaskProgressAsync(string taskBatchId)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                return await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT * FROM TaskHistory WHERE TaskBatchId = @TaskBatchId",
                    new { TaskBatchId = taskBatchId });
            }
        }

        /// <summary>
        /// 记录机器人状态到历史日志表
        /// </summary>
        public async Task LogRobotStatusAsync(string robotId, string status, int power, 
            string location, string direction, double? speed)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                var parameters = new
                {
                    RobotId = robotId,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Status = status,
                    Power = power,
                    Location = location,
                    Direction = direction,
                    Speed = speed
                };
                
                await connection.ExecuteAsync(@"
                    INSERT INTO robot_status_log 
                    (robot_id, timestamp, status, power, location, direction, speed) 
                    VALUES 
                    (@RobotId, @Timestamp, @Status, @Power, @Location, @Direction, @Speed)",
                    parameters);
                
                Console.WriteLine($"已记录机器人[{robotId}]状态到历史日志");
            }
        }
        
        /// <summary>
        /// 更新机器人当前状态缓存
        /// </summary>
        public async Task UpdateRobotLatestStatusAsync(string robotId, string status, int power, 
            string location, string direction, double? speed, string taskId = null)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                var parameters = new
                {
                    RobotId = robotId,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Status = status,
                    Power = power,
                    Location = location,
                    Direction = direction,
                    Speed = speed,
                    TaskId = taskId
                };
                
                await connection.ExecuteAsync(@"
                    INSERT INTO robot_latest_status
                    (robot_id, timestamp, status, power, location, direction, speed, task_id)
                    VALUES
                    (@RobotId, @Timestamp, @Status, @Power, @Location, @Direction, @Speed, @TaskId)
                    ON CONFLICT(robot_id) DO UPDATE SET
                        timestamp = excluded.timestamp,
                        status = excluded.status,
                        power = excluded.power,
                        location = excluded.location,
                        direction = excluded.direction,
                        speed = excluded.speed,
                        task_id = excluded.task_id",
                    parameters);
                
                Console.WriteLine($"已更新机器人[{robotId}]的当前状态缓存");
            }
        }
        
        /// <summary>
        /// 同时记录和更新机器人状态
        /// </summary>
        /// <remarks>
        /// 此方法同时将状态记录到历史日志，并更新当前状态缓存，推荐在状态变更时使用
        /// </remarks>
        public async Task RecordRobotStatusAsync(string robotId, string status, int power, 
            string location, string direction, double? speed, string taskId = null)
        {
            await LogRobotStatusAsync(robotId, status, power, location, direction, speed);
            await UpdateRobotLatestStatusAsync(robotId, status, power, location, direction, speed, taskId);
        }
        
        /// <summary>
        /// 获取机器人当前状态
        /// </summary>
        public async Task<dynamic> GetRobotLatestStatusAsync(string robotId)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                return await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT * FROM robot_latest_status 
                    WHERE robot_id = @RobotId",
                    new { RobotId = robotId });
            }
        }
        
        /// <summary>
        /// 获取所有机器人的当前状态
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetAllRobotsLatestStatusAsync()
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                return await connection.QueryAsync<dynamic>(@"
                    SELECT * FROM robot_latest_status 
                    ORDER BY timestamp DESC");
            }
        }
        
        /// <summary>
        /// 获取机器人历史状态日志
        /// </summary>
        /// <param name="robotId">机器人ID</param>
        /// <param name="limit">返回的记录数限制，默认100条</param>
        /// <param name="syncStatus">同步状态筛选，null表示所有状态</param>
        public async Task<IEnumerable<dynamic>> GetRobotStatusLogsAsync(
            string robotId, int limit = 100, string syncStatus = null)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT * FROM robot_status_log 
                    WHERE robot_id = @RobotId";
                
                if (!string.IsNullOrEmpty(syncStatus))
                {
                    sql += " AND sync_status = @SyncStatus";
                }
                
                sql += " ORDER BY timestamp DESC LIMIT @Limit";
                
                return await connection.QueryAsync<dynamic>(sql,
                    new { RobotId = robotId, SyncStatus = syncStatus, Limit = limit });
            }
        }
        
        /// <summary>
        /// 获取待同步的机器人状态日志
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetPendingSyncStatusLogsAsync(int limit = 100)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                return await connection.QueryAsync<dynamic>(@"
                    SELECT * FROM robot_status_log 
                    WHERE sync_status = 'PENDING'
                    ORDER BY timestamp ASC
                    LIMIT @Limit",
                    new { Limit = limit });
            }
        }
        
        /// <summary>
        /// 更新机器人状态日志的同步状态
        /// </summary>
        public async Task UpdateStatusLogSyncResultAsync(long logId, bool success, string errorMsg = null)
        {
            CheckInitialized();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                await connection.ExecuteAsync(@"
                    UPDATE robot_status_log 
                    SET sync_status = @SyncStatus, error_msg = @ErrorMsg
                    WHERE id = @LogId",
                    new { 
                        LogId = logId, 
                        SyncStatus = success ? "SUCCESS" : "FAILED",
                        ErrorMsg = errorMsg
                    });
            }
        }

        /// <summary>
        /// 检查是否初始化
        /// </summary>
        private void CheckInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("SQLite数据库未初始化，请先调用Initialize方法");
            }
        }

        /// <summary>
        /// 批量保存断头锭子数据到本地缓存
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumbers">锭号列表</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SaveBrokenSpindlesAsync(string sideNumber, List<int> spindleNumbers, string taskBatchId)
        {
            CheckInitialized();
            
            if (spindleNumbers == null || spindleNumbers.Count == 0)
            {
                Console.WriteLine($"[SQLiteHelper] 保存断头锭子数据失败: 无数据");
                return false;
            }
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // 开始事务
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 查询当前任务的最大处理顺序
                        var maxProcessOrder = await connection.QueryFirstOrDefaultAsync<int?>(
                            @"SELECT MAX(ProcessOrder) FROM BrokenSpindles 
                            WHERE SideNumber = @SideNumber AND TaskBatchId = @TaskBatchId",
                            new { SideNumber = sideNumber, TaskBatchId = taskBatchId }, transaction) ?? 0;
                        
                        // 添加所有断头锭子数据
                        for (int i = 0; i < spindleNumbers.Count; i++)
                        {
                            var parameters = new
                            {
                                SideNumber = sideNumber,
                                SpindleNumber = spindleNumbers[i],
                                TaskBatchId = taskBatchId,
                                ProcessOrder = maxProcessOrder + i + 1,
                                Status = "Pending"
                            };
                            
                            await connection.ExecuteAsync(@"
                                INSERT INTO BrokenSpindles 
                                (SideNumber, SpindleNumber, TaskBatchId, ProcessOrder, Status) 
                                VALUES 
                                (@SideNumber, @SpindleNumber, @TaskBatchId, @ProcessOrder, @Status)",
                                parameters, transaction);
                        }
                        
                        // 检查任务历史记录是否存在
                        var taskRecord = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            @"SELECT Id, TotalSpindles FROM TaskHistory WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                            new { TaskBatchId = taskBatchId, SideNumber = sideNumber }, transaction);
                        
                        if (taskRecord == null)
                        {
                            // 创建新的任务历史记录
                            var taskParams = new
                            {
                                TaskBatchId = taskBatchId,
                                SideNumber = sideNumber,
                                StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                Status = "Started",
                                TotalSpindles = spindleNumbers.Count,
                                CompletedSpindles = 0
                            };
                            
                            await connection.ExecuteAsync(@"
                                INSERT INTO TaskHistory 
                                (TaskBatchId, SideNumber, StartTime, Status, TotalSpindles, CompletedSpindles) 
                                VALUES 
                                (@TaskBatchId, @SideNumber, @StartTime, @Status, @TotalSpindles, @CompletedSpindles)",
                                taskParams, transaction);
                        }
                        else
                        {
                            // 更新任务历史记录中的总锭子数
                            await connection.ExecuteAsync(@"
                                UPDATE TaskHistory 
                                SET TotalSpindles = TotalSpindles + @NewSpindles 
                                WHERE TaskBatchId = @TaskBatchId AND SideNumber = @SideNumber",
                                new { 
                                    TaskBatchId = taskBatchId, 
                                    SideNumber = sideNumber, 
                                    NewSpindles = spindleNumbers.Count 
                                }, transaction);
                        }
                        
                        transaction.Commit();
                        Console.WriteLine($"[SQLiteHelper] 已批量保存{spindleNumbers.Count}个断头锭子数据到本地缓存");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"[SQLiteHelper] 批量保存断头锭子数据失败: {ex.Message}");
                        return false;
                    }
                }
            }
        }
    }
} 