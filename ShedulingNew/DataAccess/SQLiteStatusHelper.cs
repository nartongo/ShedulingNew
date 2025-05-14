using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.BusinessLogic.Services;

namespace ShedulingNew.DataAccess
{
    /// <summary>
    /// SQLite状态帮助类
    /// 用于管理机器人状态日志的本地存储和读取
    /// 提供状态历史记录和最新状态缓存的数据访问
    /// </summary>
    public class SQLiteStatusHelper
    {
        private readonly string _connectionString;
        private readonly ConfigService _configService;
        private readonly string _machineId;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SQLiteStatusHelper()
        {
            _configService = ConfigService.Instance;
            var appConfig = _configService.GetConfig();
            _machineId = appConfig.MachineId;

            // 构建数据库文件路径
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "RobotStatus.db");
            
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
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "RobotStatus.db")))
            {
                SQLiteConnection.CreateFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "RobotStatus.db"));
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 创建状态历史日志表
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS robot_status_log (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            machine_id TEXT NOT NULL,
                            timestamp TEXT NOT NULL,
                            status_json TEXT NOT NULL,
                            sync_status TEXT NOT NULL DEFAULT 'PENDING',
                            created_at TEXT NOT NULL
                        );
                        
                        CREATE INDEX IF NOT EXISTS idx_machine_timestamp ON robot_status_log (machine_id, timestamp);
                        CREATE INDEX IF NOT EXISTS idx_sync_status ON robot_status_log (sync_status);
                    ";
                    command.ExecuteNonQuery();
                }
                
                // 创建最新状态缓存表
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS robot_latest_status (
                            machine_id TEXT PRIMARY KEY,
                            status_json TEXT NOT NULL,
                            timestamp TEXT NOT NULL,
                            updated_at TEXT NOT NULL
                        );
                    ";
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 插入状态日志
        /// 记录一条新的状态记录到历史日志表中
        /// </summary>
        /// <param name="status">机器人状态对象</param>
        /// <returns>插入的记录ID</returns>
        public long InsertStatusLog(object status)
        {
            string statusJson = Newtonsoft.Json.JsonConvert.SerializeObject(status);
            string timestamp = DateTime.UtcNow.ToString("o");
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        INSERT INTO robot_status_log (machine_id, timestamp, status_json, sync_status, created_at)
                        VALUES (@machineId, @timestamp, @statusJson, 'PENDING', @createdAt);
                        SELECT last_insert_rowid();
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", _machineId);
                    command.Parameters.AddWithValue("@timestamp", timestamp);
                    command.Parameters.AddWithValue("@statusJson", statusJson);
                    command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
                    
                    return Convert.ToInt64(command.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// 更新或插入最新状态
        /// 更新机器人的最新状态缓存
        /// </summary>
        /// <param name="status">机器人状态对象</param>
        public void UpsertLatestStatus(object status)
        {
            string statusJson = Newtonsoft.Json.JsonConvert.SerializeObject(status);
            string timestamp = DateTime.UtcNow.ToString("o");
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO robot_latest_status (machine_id, status_json, timestamp, updated_at)
                        VALUES (@machineId, @statusJson, @timestamp, @updatedAt);
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", _machineId);
                    command.Parameters.AddWithValue("@statusJson", statusJson);
                    command.Parameters.AddWithValue("@timestamp", timestamp);
                    command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
                    
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 更新状态日志同步状态
        /// 当状态成功上报到后端后，更新本地日志的同步状态
        /// </summary>
        /// <param name="logId">日志记录ID</param>
        /// <param name="syncStatus">同步状态，如SUCCESS、FAILED</param>
        public void UpdateStatusLogSync(long logId, string syncStatus)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        UPDATE robot_status_log
                        SET sync_status = @syncStatus
                        WHERE id = @id;
                    ";
                    
                    command.Parameters.AddWithValue("@id", logId);
                    command.Parameters.AddWithValue("@syncStatus", syncStatus);
                    
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 获取最新状态
        /// 获取机器人的最新状态记录
        /// </summary>
        /// <returns>最新状态的JSON字符串</returns>
        public string GetLatestStatus()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT status_json
                        FROM robot_latest_status
                        WHERE machine_id = @machineId;
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", _machineId);
                    
                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }

        /// <summary>
        /// 获取未同步的状态记录
        /// 获取所有尚未同步到后端的状态记录
        /// </summary>
        /// <param name="limit">最大返回记录数</param>
        /// <returns>未同步的状态记录列表</returns>
        public List<Dictionary<string, object>> GetPendingStatusLogs(int limit = 100)
        {
            var result = new List<Dictionary<string, object>>();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        SELECT id, machine_id, timestamp, status_json
                        FROM robot_status_log
                        WHERE sync_status = 'PENDING' AND machine_id = @machineId
                        ORDER BY timestamp ASC
                        LIMIT @limit;
                    ";
                    
                    command.Parameters.AddWithValue("@machineId", _machineId);
                    command.Parameters.AddWithValue("@limit", limit);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new Dictionary<string, object>
                            {
                                ["id"] = reader.GetInt64(0),
                                ["machine_id"] = reader.GetString(1),
                                ["timestamp"] = reader.GetString(2),
                                ["status_json"] = reader.GetString(3)
                            };
                            
                            result.Add(item);
                        }
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// 批量更新状态日志同步状态
        /// </summary>
        /// <param name="logIds">日志记录ID列表</param>
        /// <param name="syncStatus">同步状态</param>
        public void BatchUpdateStatusLogSync(List<long> logIds, string syncStatus)
        {
            if (logIds == null || logIds.Count == 0)
                return;
                
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var command = new SQLiteCommand(connection))
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                                UPDATE robot_status_log
                                SET sync_status = @syncStatus
                                WHERE id = @id;
                            ";
                            
                            var idParam = command.Parameters.Add("@id", DbType.Int64);
                            command.Parameters.AddWithValue("@syncStatus", syncStatus);
                            
                            foreach (var id in logIds)
                            {
                                idParam.Value = id;
                                command.ExecuteNonQuery();
                            }
                        }
                        
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 清理旧的状态日志
        /// 删除超过保留期限的历史记录
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        public void CleanupOldStatusLogs(int daysToKeep = 30)
        {
            // 计算截止日期
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep).ToString("o");
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = @"
                        DELETE FROM robot_status_log
                        WHERE timestamp < @cutoffDate AND sync_status != 'PENDING';
                    ";
                    
                    command.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                    
                    var rowsDeleted = command.ExecuteNonQuery();
                    Console.WriteLine($"已清理 {rowsDeleted} 条旧状态日志记录");
                }
            }
        }
    }
} 