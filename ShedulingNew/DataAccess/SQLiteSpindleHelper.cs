using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using Newtonsoft.Json;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.BusinessLogic.Services;

namespace ShedulingNew.DataAccess
{
    /// <summary>
    /// SQLite锭子数据帮助类
    /// 用于缓存和管理锭子数据，支持断网环境下的数据访问
    /// </summary>
    public class SQLiteSpindleHelper
    {
        private readonly string _connectionString;
        private readonly ConfigService _configService;
        private readonly string _machineId;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SQLiteSpindleHelper()
        {
            _configService = ConfigService.Instance;
            var appConfig = _configService.Config;
            _machineId = appConfig.Robot.MachineId;

            // 构建数据库文件路径
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SpindleData.db");
            
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            
            _connectionString = $"Data Source={dbPath};Version=3;";
            
            // 初始化数据库
            InitializeDatabase();
        }

        /// <summary>
        /// 初始化数据库
        /// 创建必要的表和索引
        /// </summary>
        private void InitializeDatabase()
        {
            // 确保数据库文件存在
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SpindleData.db")))
            {
                SQLiteConnection.CreateFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SpindleData.db"));
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 创建锭子数据表
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS spindle_data (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            machine_id TEXT NOT NULL,
                            spindle_id INTEGER NOT NULL,
                            distance INTEGER NOT NULL,
                            status TEXT NOT NULL,
                            created_at TEXT NOT NULL,
                            updated_at TEXT NOT NULL,
                            UNIQUE(machine_id, spindle_id)
                        );
                        
                        CREATE INDEX IF NOT EXISTS idx_machine_spindle ON spindle_data (machine_id, spindle_id);
                        CREATE INDEX IF NOT EXISTS idx_spindle_status ON spindle_data (status);
                    ";
                    command.ExecuteNonQuery();
                }
                
                // 创建机器配置表（存储权限切换点和待命点信息）
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS machine_config_cache (
                            machine_id TEXT PRIMARY KEY,
                            switch_point_id TEXT NOT NULL,
                            wait_point_id TEXT NOT NULL,
                            config_json TEXT,
                            updated_at TEXT NOT NULL
                        );
                    ";
                    command.ExecuteNonQuery();
                }
                
                // 创建任务状态表
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS task_status (
                            task_id TEXT PRIMARY KEY,
                            machine_id TEXT NOT NULL,
                            status TEXT NOT NULL,
                            progress INTEGER NOT NULL,
                            current_spindle_id INTEGER,
                            total_spindles INTEGER NOT NULL,
                            completed_spindles INTEGER NOT NULL,
                            created_at TEXT NOT NULL,
                            updated_at TEXT NOT NULL
                        );
                    ";
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 缓存锭子数据
        /// 将从MySQL获取的锭子数据保存到本地SQLite
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <param name="spindles">锭子数据列表</param>
        public void CacheSpindlesData(string machineId, List<SpindleInfo> spindles)
        {
            if (spindles == null || spindles.Count == 0)
                return;
                
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 清除该机器的旧数据
                        using (var clearCommand = new SQLiteCommand(connection))
                        {
                            clearCommand.Transaction = transaction;
                            clearCommand.CommandText = "DELETE FROM spindle_data WHERE machine_id = @machineId";
                            clearCommand.Parameters.AddWithValue("@machineId", machineId);
                            clearCommand.ExecuteNonQuery();
                        }
                        
                        // 插入新数据
                        using (var command = new SQLiteCommand(connection))
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                                INSERT INTO spindle_data 
                                (machine_id, spindle_id, distance, status, created_at, updated_at)
                                VALUES 
                                (@machineId, @spindleId, @distance, @status, @createdAt, @updatedAt)
                            ";
                            
                            var machineIdParam = command.Parameters.Add("@machineId", DbType.String);
                            var spindleIdParam = command.Parameters.Add("@spindleId", DbType.Int32);
                            var distanceParam = command.Parameters.Add("@distance", DbType.Int32);
                            var statusParam = command.Parameters.Add("@status", DbType.String);
                            var createdAtParam = command.Parameters.Add("@createdAt", DbType.String);
                            var updatedAtParam = command.Parameters.Add("@updatedAt", DbType.String);
                            
                            foreach (var spindle in spindles)
                            {
                                machineIdParam.Value = machineId;
                                spindleIdParam.Value = spindle.SpindleId;
                                distanceParam.Value = spindle.Distance;
                                statusParam.Value = spindle.Status;
                                createdAtParam.Value = DateTime.Now.ToString("o");
                                updatedAtParam.Value = DateTime.Now.ToString("o");
                                
                                command.ExecuteNonQuery();
                            }
                        }
                        
                        transaction.Commit();
                        Console.WriteLine($"已缓存 {spindles.Count} 条锭子数据到本地数据库");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"缓存锭子数据失败: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 获取机器的所有待修复锭子
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <returns>锭子列表</returns>
        public List<SpindleInfo> GetPendingSpindles(string machineId)
        {
            var result = new List<SpindleInfo>();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT spindle_id, distance, status, created_at, updated_at
                        FROM spindle_data
                        WHERE machine_id = @machineId AND status = 'PENDING'
                        ORDER BY spindle_id ASC
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SpindleInfo
                            {
                                SpindleId = reader.GetInt32(0),
                                Distance = reader.GetInt32(1),
                                Status = reader.GetString(2),
                                MachineId = machineId,
                                CreatedAt = DateTime.Parse(reader.GetString(3)),
                                UpdatedAt = DateTime.Parse(reader.GetString(4))
                            });
                        }
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// 获取指定锭子信息
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <param name="spindleId">锭子ID</param>
        /// <returns>锭子信息</returns>
        public SpindleInfo GetSpindle(string machineId, int spindleId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT spindle_id, distance, status, created_at, updated_at
                        FROM spindle_data
                        WHERE machine_id = @machineId AND spindle_id = @spindleId
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    command.Parameters.AddWithValue("@spindleId", spindleId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new SpindleInfo
                            {
                                SpindleId = reader.GetInt32(0),
                                Distance = reader.GetInt32(1),
                                Status = reader.GetString(2),
                                MachineId = machineId,
                                CreatedAt = DateTime.Parse(reader.GetString(3)),
                                UpdatedAt = DateTime.Parse(reader.GetString(4))
                            };
                        }
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// 更新锭子状态
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <param name="spindleId">锭子ID</param>
        /// <param name="status">新状态</param>
        public void UpdateSpindleStatus(string machineId, int spindleId, string status)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        UPDATE spindle_data
                        SET status = @status, updated_at = @updatedAt
                        WHERE machine_id = @machineId AND spindle_id = @spindleId
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    command.Parameters.AddWithValue("@spindleId", spindleId);
                    command.Parameters.AddWithValue("@status", status);
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));
                    
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 缓存机器配置信息（包括切换点和待命点位置ID）
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <param name="switchPointId">切换点位置ID</param>
        /// <param name="waitPointId">待命点位置ID</param>
        /// <param name="configData">额外配置数据（可选）</param>
        public void CacheMachineConfig(string machineId, string switchPointId, string waitPointId, object configData = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO machine_config_cache
                        (machine_id, switch_point_id, wait_point_id, config_json, updated_at)
                        VALUES
                        (@machineId, @switchPointId, @waitPointId, @configJson, @updatedAt)
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    command.Parameters.AddWithValue("@switchPointId", switchPointId);
                    command.Parameters.AddWithValue("@waitPointId", waitPointId);
                    command.Parameters.AddWithValue("@configJson", configData != null ? JsonConvert.SerializeObject(configData) : null);
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));
                    
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 获取机器切换点位置ID
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <returns>切换点位置ID</returns>
        public string GetSwitchPointId(string machineId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT switch_point_id
                        FROM machine_config_cache
                        WHERE machine_id = @machineId
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    
                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }

        /// <summary>
        /// 获取机器待命点位置ID
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <returns>待命点位置ID</returns>
        public string GetWaitPointId(string machineId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT wait_point_id
                        FROM machine_config_cache
                        WHERE machine_id = @machineId
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", machineId);
                    
                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }

        /// <summary>
        /// 创建或更新任务状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="machineId">机器ID</param>
        /// <param name="status">任务状态</param>
        /// <param name="progress">进度百分比</param>
        /// <param name="currentSpindleId">当前处理的锭子ID</param>
        /// <param name="totalSpindles">总锭子数</param>
        /// <param name="completedSpindles">已完成锭子数</param>
        public void UpdateTaskStatus(string taskId, string machineId, string status, int progress, 
                                    int? currentSpindleId, int totalSpindles, int completedSpindles)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO task_status
                        (task_id, machine_id, status, progress, current_spindle_id, 
                         total_spindles, completed_spindles, created_at, updated_at)
                        VALUES
                        (@taskId, @machineId, @status, @progress, @currentSpindleId, 
                         @totalSpindles, @completedSpindles, @createdAt, @updatedAt)
                    ";
                    
                    command.Parameters.AddWithValue("@taskId", taskId);
                    command.Parameters.AddWithValue("@machineId", machineId);
                    command.Parameters.AddWithValue("@status", status);
                    command.Parameters.AddWithValue("@progress", progress);
                    command.Parameters.AddWithValue("@currentSpindleId", currentSpindleId.HasValue ? (object)currentSpindleId.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@totalSpindles", totalSpindles);
                    command.Parameters.AddWithValue("@completedSpindles", completedSpindles);
                    
                    // 查询是否已存在相同taskId的记录
                    var existingCommand = new SQLiteCommand(
                        "SELECT created_at FROM task_status WHERE task_id = @taskId", connection);
                    existingCommand.Parameters.AddWithValue("@taskId", taskId);
                    var existingCreatedAt = existingCommand.ExecuteScalar();
                    
                    // 如果已存在，使用原创建时间；否则使用当前时间
                    if (existingCreatedAt != null && existingCreatedAt != DBNull.Value)
                    {
                        command.Parameters.AddWithValue("@createdAt", existingCreatedAt.ToString());
                    }
                    else
                    {
                        command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("o"));
                    }
                    
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));
                    
                    command.ExecuteNonQuery();
                }
            }
        }
    }
} 