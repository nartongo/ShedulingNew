using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace ShedulingNew
{
    public class DbSetup
    {
        private static readonly string ConnectionString = "Server=localhost;Database=conn_site;User=remote;Password=root;";

        public static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("开始设置数据库...");
                
                // 创建权限切换点表
                await CreateSwitchPointsTable();
                
                // 创建AGV待命点表
                await CreateAgvWaitPointsTable();
                
                // 插入测试数据
                await InsertTestData();
                
                Console.WriteLine("数据库设置完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置数据库时出错: {ex.Message}");
            }
        }

        private static async Task CreateSwitchPointsTable()
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                
                string createTableSql = @"
                    CREATE TABLE IF NOT EXISTS switch_points (
                        machine_id VARCHAR(50) PRIMARY KEY,
                        entry_switch_point_id VARCHAR(50) NOT NULL,
                        exit_switch_point_id VARCHAR(50) NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB;";
                
                using (var command = new MySqlCommand(createTableSql, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine("权限切换点表创建成功或已存在");
                }
            }
        }

        private static async Task CreateAgvWaitPointsTable()
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                
                string createTableSql = @"
                    CREATE TABLE IF NOT EXISTS agv_wait_points (
                        agv_id VARCHAR(50) PRIMARY KEY,
                        wait_point_id VARCHAR(50) NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB;";
                
                using (var command = new MySqlCommand(createTableSql, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine("AGV待命点表创建成功或已存在");
                }
            }
        }

        private static async Task InsertTestData()
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                
                // 清空现有数据以避免主键冲突
                string clearSql1 = "DELETE FROM switch_points;";
                string clearSql2 = "DELETE FROM agv_wait_points;";
                
                using (var command = new MySqlCommand(clearSql1, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
                
                using (var command = new MySqlCommand(clearSql2, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
                
                // 插入权限切换点测试数据
                string insertSwitchPointsSql = @"
                    INSERT INTO switch_points (machine_id, entry_switch_point_id, exit_switch_point_id) VALUES 
                    ('M001', '1001', '1002'),
                    ('M002', '2001', '2002'),
                    ('M003', '3001', '3002');";
                
                using (var command = new MySqlCommand(insertSwitchPointsSql, connection))
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    Console.WriteLine($"已插入 {rowsAffected} 条权限切换点数据");
                }
                
                // 插入AGV待命点测试数据
                string insertAgvWaitPointsSql = @"
                    INSERT INTO agv_wait_points (agv_id, wait_point_id) VALUES 
                    ('AGV1', '5001'),
                    ('AGV2', '5002'),
                    ('AGV3', '5003');";
                
                using (var command = new MySqlCommand(insertAgvWaitPointsSql, connection))
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    Console.WriteLine($"已插入 {rowsAffected} 条AGV待命点数据");
                }
            }
        }
    }
} 