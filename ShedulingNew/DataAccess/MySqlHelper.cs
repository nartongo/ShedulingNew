using System;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using ShedulingNew.Coordinators;

namespace ShedulingNew.DataAccess
{
    /// <summary>
    /// MySQL数据库帮助类
    /// 提供对MySQL数据库的访问和操作封装
    /// 负责处理与后端主数据库的连接和数据交互
    /// </summary>
    public class MySqlHelper
    {
        private string _connectionString;  // 数据库连接字符串
        private bool _isInitialized = false;  // 初始化标志
        
        /// <summary>
        /// 初始化数据库连接
        /// 从配置中获取连接字符串并测试连接
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 从配置服务中获取连接字符串
                // 避免硬编码数据库连接信息
                _connectionString = SystemCoordinator.Instance.Config.Database.MySqlConnectionString;
                
                // 测试连接，确保数据库可访问
                using (MySqlConnection connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                    Console.WriteLine("MySQL数据库连接成功");
                }
                
                _isInitialized = true;  // 标记初始化成功
            }
            catch (Exception ex)
            {
                // 记录初始化错误并重新抛出异常
                Console.WriteLine($"MySQL数据库初始化失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 执行查询并返回结果集
        /// 异步方式执行SQL查询，支持参数化查询
        /// </summary>
        /// <param name="sql">SQL查询语句</param>
        /// <param name="parameters">查询参数字典，可选</param>
        /// <returns>查询结果集</returns>
        public async Task<DataTable> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            // 检查初始化状态
            CheckInitialized();
            
            DataTable dataTable = new DataTable();
            
            try
            {
                // 创建并打开数据库连接
                using (MySqlConnection connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 创建命令对象
                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        // 添加参数，防止SQL注入
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value);
                            }
                        }
                        
                        // 使用数据适配器填充结果集
                        using (MySqlDataAdapter adapter = new MySqlDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }
                
                return dataTable;
            }
            catch (Exception ex)
            {
                // 记录查询错误并重新抛出异常
                Console.WriteLine($"执行查询失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 执行非查询SQL语句(INSERT, UPDATE, DELETE等)
        /// 异步方式执行修改数据的SQL语句，返回受影响的行数
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="parameters">参数字典，可选</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            // 检查初始化状态
            CheckInitialized();
            
            try
            {
                // 创建并打开数据库连接
                using (MySqlConnection connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 创建命令对象
                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        // 添加参数，防止SQL注入
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value);
                            }
                        }
                        
                        // 执行命令并返回受影响的行数
                        return await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误并重新抛出异常
                Console.WriteLine($"执行非查询失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 执行查询并返回第一行第一列的值
        /// 适用于查询单个值的情况，如COUNT、MAX等聚合函数
        /// </summary>
        /// <param name="sql">SQL查询语句</param>
        /// <param name="parameters">查询参数字典，可选</param>
        /// <returns>查询结果的第一行第一列值</returns>
        public async Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null)
        {
            // 检查初始化状态
            CheckInitialized();
            
            try
            {
                // 创建并打开数据库连接
                using (MySqlConnection connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 创建命令对象
                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        // 添加参数，防止SQL注入
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value);
                            }
                        }
                        
                        // 执行命令并返回第一行第一列的值
                        return await command.ExecuteScalarAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误并重新抛出异常
                Console.WriteLine($"执行查询标量失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 开始一个事务
        /// 用于需要事务支持的多步数据库操作
        /// </summary>
        /// <returns>MySQL事务对象</returns>
        public async Task<MySqlTransaction> BeginTransactionAsync()
        {
            // 检查初始化状态
            CheckInitialized();
            
            try
            {
                // 创建数据库连接，不使用using因为事务需要在方法外部使用
                MySqlConnection connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                // 开始并返回事务，由调用者负责提交或回滚
                return await connection.BeginTransactionAsync();
            }
            catch (Exception ex)
            {
                // 记录错误并重新抛出异常
                Console.WriteLine($"开始事务失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取指定机器的权限切换点信息
        /// </summary>
        /// <param name="machineId">机器ID</param>
        /// <returns>包含入口和出口切换点ID的元组</returns>
        public async Task<(string EntrySwitchPointId, string ExitSwitchPointId)> GetSwitchPointsAsync(string machineId)
        {
            CheckInitialized();
            
            try
            {
                string sql = @"
                    SELECT entry_switch_point_id, exit_switch_point_id 
                    FROM switch_points 
                    WHERE machine_id = @machineId";
                
                var parameters = new Dictionary<string, object>
                {
                    { "@machineId", machineId }
                };
                
                DataTable result = await ExecuteQueryAsync(sql, parameters);
                
                if (result.Rows.Count > 0)
                {
                    string entrySwitchPointId = result.Rows[0]["entry_switch_point_id"].ToString();
                    string exitSwitchPointId = result.Rows[0]["exit_switch_point_id"].ToString();
                    
                    Console.WriteLine($"[MySqlHelper] 获取机器 {machineId} 的切换点：入口={entrySwitchPointId}, 出口={exitSwitchPointId}");
                    return (entrySwitchPointId, exitSwitchPointId);
                }
                
                Console.WriteLine($"[MySqlHelper] 未找到机器 {machineId} 的切换点信息");
                return (null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 获取切换点信息出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取指定AGV的待命点信息
        /// </summary>
        /// <param name="agvId">AGV ID</param>
        /// <returns>待命点ID</returns>
        public async Task<string> GetAgvWaitPointAsync(string agvId)
        {
            CheckInitialized();
            
            try
            {
                string sql = @"
                    SELECT wait_point_id 
                    FROM agv_wait_points 
                    WHERE agv_id = @agvId";
                
                var parameters = new Dictionary<string, object>
                {
                    { "@agvId", agvId }
                };
                
                DataTable result = await ExecuteQueryAsync(sql, parameters);
                
                if (result.Rows.Count > 0)
                {
                    string waitPointId = result.Rows[0]["wait_point_id"].ToString();
                    Console.WriteLine($"[MySqlHelper] 获取AGV {agvId} 的待命点：{waitPointId}");
                    return waitPointId;
                }
                
                Console.WriteLine($"[MySqlHelper] 未找到AGV {agvId} 的待命点信息");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 获取AGV待命点信息出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 获取指定机器的断头锭子数据
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="limit">最大返回数量，默认不限制</param>
        /// <returns>断头锭子距离列表</returns>
        public async Task<List<int>> GetBrokenSpindlesAsync(string sideNumber, int? limit = null)
        {
            CheckInitialized();
            
            try
            {
                // 将边序号转换为实际机器号和边信息
                (int machineNumber, string side) = ConvertSideNumberToMachineInfo(sideNumber);
                
                // 构建表名
                string tableName = $"data{machineNumber}_update";
                
                // 查询断头数据
                string sql = $@"
                    SELECT `value` 
                    FROM `{tableName}` 
                    WHERE deviceId = @deviceId
                    AND `value` > 0
                    ORDER BY pointId ASC";
                
                if (limit.HasValue && limit.Value > 0)
                {
                    sql += " LIMIT @limit";
                }
                
                var parameters = new Dictionary<string, object>
                {
                    { "@deviceId", side }
                };
                
                if (limit.HasValue && limit.Value > 0)
                {
                    parameters.Add("@limit", limit.Value);
                }
                
                DataTable result = await ExecuteQueryAsync(sql, parameters);
                
                List<int> spindles = new List<int>();
                foreach (DataRow row in result.Rows)
                {
                    int spindleNumber = Convert.ToInt32(row["value"]);
                    spindles.Add(spindleNumber);
                }
                
                Console.WriteLine($"[MySqlHelper] 获取机器{machineNumber} {side}边的断头数据：共 {spindles.Count} 条记录");
                return spindles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 获取断头数据出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 将边序号转换为机器号和边信息
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <returns>机器号和边信息(right/left)</returns>
        public (int machineNumber, string side) ConvertSideNumberToMachineInfo(string sideNumber)
        {
            // 将传入的字符串转换为整数
            if (!int.TryParse(sideNumber, out int sideNumberInt))
            {
                throw new ArgumentException($"无效的边序号: {sideNumber}");
            }
            
            // 计算实际机器号：(sideNumber + 1) / 2，向上取整
            int machineNumber = (sideNumberInt + 1) / 2;
            
            // 确定是左边还是右边：sideNumber 为奇数时是右边，偶数时是左边
            string side = sideNumberInt % 2 == 1 ? "right" : "left";
            
            Console.WriteLine($"[MySqlHelper] 边序号 {sideNumber} 转换为：机器号 {machineNumber}，{side}边");
            
            return (machineNumber, side);
        }
        
        /// <summary>
        /// 更新断头锭子状态
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumber">锭号</param>
        /// <param name="status">新状态</param>
        /// <returns>是否更新成功</returns>
        public async Task<bool> UpdateBrokenSpindleStatusAsync(string sideNumber, int spindleNumber, string status)
        {
            CheckInitialized();
            
            try
            {
                // 将边序号转换为实际机器号和边信息
                (int machineNumber, string side) = ConvertSideNumberToMachineInfo(sideNumber);
                
                // 构建表名
                string tableName = $"data{machineNumber}_update";
                
                // 更新断头状态
                string sql = $@"
                    UPDATE `{tableName}` 
                    SET updated_at = CURRENT_TIMESTAMP
                    WHERE deviceId = @deviceId AND `value` = @spindleNumber";
                
                var parameters = new Dictionary<string, object>
                {
                    { "@deviceId", side },
                    { "@spindleNumber", spindleNumber }
                };
                
                int affectedRows = await ExecuteNonQueryAsync(sql, parameters);
                
                Console.WriteLine($"[MySqlHelper] 更新机器{machineNumber} {side}边 锭号 {spindleNumber} 状态为 {status}，影响行数：{affectedRows}");
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 更新断头状态出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 根据点位ID获取点位类型
        /// </summary>
        /// <param name="pointId">点位ID</param>
        /// <returns>点位类型，如"EntrySwitchPoint", "ExitSwitchPoint", "WaitPoint"等，无法确定时返回"Unknown"</returns>
        public async Task<string> GetPointTypeAsync(uint pointId)
        {
            CheckInitialized();
            
            try
            {
                // 查询该点位是否为任何机器的进入切换点
                string entrySql = @"
                    SELECT machine_id 
                    FROM switch_points 
                    WHERE entry_switch_point_id = @pointId";
                
                var parameters = new Dictionary<string, object>
                {
                    { "@pointId", pointId }
                };
                
                DataTable entryResult = await ExecuteQueryAsync(entrySql, parameters);
                
                if (entryResult.Rows.Count > 0)
                {
                    string machineId = entryResult.Rows[0]["machine_id"].ToString();
                    Console.WriteLine($"[MySqlHelper] 点位 {pointId} 是机器 {machineId} 的入口切换点");
                    return "EntrySwitchPoint";
                }
                
                // 查询该点位是否为任何机器的退出切换点
                string exitSql = @"
                    SELECT machine_id 
                    FROM switch_points 
                    WHERE exit_switch_point_id = @pointId";
                
                DataTable exitResult = await ExecuteQueryAsync(exitSql, parameters);
                
                if (exitResult.Rows.Count > 0)
                {
                    string machineId = exitResult.Rows[0]["machine_id"].ToString();
                    Console.WriteLine($"[MySqlHelper] 点位 {pointId} 是机器 {machineId} 的出口切换点");
                    return "ExitSwitchPoint";
                }
                
                // 查询该点位是否为任何AGV的等待点
                string waitSql = @"
                    SELECT agv_id 
                    FROM agv_wait_points 
                    WHERE wait_point_id = @pointId";
                
                DataTable waitResult = await ExecuteQueryAsync(waitSql, parameters);
                
                if (waitResult.Rows.Count > 0)
                {
                    string agvId = waitResult.Rows[0]["agv_id"].ToString();
                    Console.WriteLine($"[MySqlHelper] 点位 {pointId} 是AGV {agvId} 的等待点");
                    return "WaitPoint";
                }
                
                // 没有找到匹配的点位类型
                Console.WriteLine($"[MySqlHelper] 未找到点位 {pointId} 的类型信息");
                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 获取点位类型出错: {ex.Message}");
                return "Unknown"; // 出错时也返回Unknown，而不是抛出异常，提高系统稳定性
            }
        }
        
        /// <summary>
        /// 添加单个断头锭子记录
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumber">锭号</param>
        /// <returns>是否添加成功</returns>
        public async Task<bool> AddBrokenSpindleAsync(string sideNumber, int spindleNumber)
        {
            CheckInitialized();
            
            try
            {
                // 将边序号转换为实际机器号和边信息
                (int machineNumber, string side) = ConvertSideNumberToMachineInfo(sideNumber);
                
                // 构建表名
                string tableName = $"data{machineNumber}_update";
                
                // 检查是否已存在相同的记录
                string checkSql = $@"
                    SELECT COUNT(*) FROM `{tableName}` 
                    WHERE deviceId = @deviceId AND `value` = @spindleNumber";
                
                var checkParams = new Dictionary<string, object>
                {
                    { "@deviceId", side },
                    { "@spindleNumber", spindleNumber }
                };
                
                int existingCount = Convert.ToInt32(await ExecuteScalarAsync(checkSql, checkParams));
                
                if (existingCount > 0)
                {
                    // 记录已存在，更新
                    string updateSql = $@"
                        UPDATE `{tableName}` 
                        SET updated_at = CURRENT_TIMESTAMP
                        WHERE deviceId = @deviceId AND `value` = @spindleNumber";
                    
                    int updated = await ExecuteNonQueryAsync(updateSql, checkParams);
                    
                    Console.WriteLine($"[MySqlHelper] 更新机器{machineNumber} {side}边 锭号 {spindleNumber} 记录，影响行数：{updated}");
                    return updated > 0;
                }
                else
                {
                    // 插入新记录，注意：实际表结构可能需要更多字段
                    string insertSql = $@"
                        INSERT INTO `{tableName}` 
                        (deviceId, `value`, created_at) 
                        VALUES 
                        (@deviceId, @spindleNumber, CURRENT_TIMESTAMP)";
                    
                    int inserted = await ExecuteNonQueryAsync(insertSql, checkParams);
                    
                    Console.WriteLine($"[MySqlHelper] 添加机器{machineNumber} {side}边 锭号 {spindleNumber} 断头记录，影响行数：{inserted}");
                    return inserted > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlHelper] 添加断头锭子记录出错: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 检查是否已初始化
        /// 内部方法，在执行数据库操作前检查初始化状态
        /// </summary>
        private void CheckInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("数据库未初始化，请先调用Initialize方法");
            }
        }
    }
} 