using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShedulingNew.DataAccess
{
    /// <summary>
    /// 数据同步服务 - 负责MySQL数据同步到SQLite本地缓存
    /// </summary>
    public class DataSyncService
    {
        private static readonly DataSyncService _instance = new DataSyncService();
        public static DataSyncService Instance => _instance;

        private MySqlHelper _mysqlHelper;
        private SQLiteHelper _sqliteHelper;
        private bool _isInitialized = false;

        private DataSyncService()
        {
            _mysqlHelper = new MySqlHelper();
            _sqliteHelper = SQLiteHelper.Instance;
        }

        /// <summary>
        /// 初始化数据同步服务
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // 确保SQLite已初始化
                if (!_sqliteHelper.IsInitialized)
                {
                    _sqliteHelper.Initialize();
                }
                
                _isInitialized = true;
                Console.WriteLine("[DataSyncService] 数据同步服务初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从MySQL获取断头数据并保存到SQLite
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <returns>断头锭子列表</returns>
        public async Task<List<int>> SyncBrokenSpindlesAsync(string sideNumber, string taskBatchId)
        {
            CheckInitialized();

            try
            {
                Console.WriteLine($"[DataSyncService] 开始同步边序号 {sideNumber} 的断头数据");
                
                // 1. 从MySQL获取断头数据
                List<int> brokenSpindles = await _mysqlHelper.GetBrokenSpindlesAsync(sideNumber);
                Console.WriteLine($"[DataSyncService] 从MySQL获取到 {brokenSpindles.Count} 条断头数据");
                
                // 2. 保存到SQLite缓存
                if (brokenSpindles.Count > 0)
                {
                    bool saved = await _sqliteHelper.SaveBrokenSpindlesAsync(sideNumber, brokenSpindles, taskBatchId);
                    if (!saved)
                    {
                        Console.WriteLine($"[DataSyncService] 警告: 保存断头数据到SQLite失败");
                    }
                    else
                    {
                        Console.WriteLine($"[DataSyncService] 已成功将断头数据保存到SQLite缓存");
                    }
                }
                else
                {
                    Console.WriteLine($"[DataSyncService] 警告: 没有找到边序号 {sideNumber} 的断头数据");
                }
                
                // 3. 返回断头数据列表
                return brokenSpindles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 同步断头数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从SQLite获取断头锭子列表
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <returns>断头锭子列表</returns>
        public async Task<List<int>> GetBrokenSpindleDistancesFromCacheAsync(string sideNumber, string taskBatchId)
        {
            CheckInitialized();
            
            try
            {
                return await _sqliteHelper.GetBrokenSpindleDistancesAsync(sideNumber, taskBatchId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 从SQLite获取断头数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新断头锭子状态并同步到MySQL
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumber">锭号</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <param name="status">状态，例如"Completed"</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> UpdateSpindleStatusAsync(string sideNumber, int spindleNumber, string taskBatchId, string status)
        {
            CheckInitialized();
            
            try
            {
                // 1. 首先更新本地SQLite
                await _sqliteHelper.UpdateSpindleStatusAsync(sideNumber, spindleNumber, taskBatchId, status);
                
                // 2. 尝试更新MySQL主数据库
                bool mysqlUpdated = false;
                try
                {
                    mysqlUpdated = await _mysqlHelper.UpdateBrokenSpindleStatusAsync(sideNumber, spindleNumber, status);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DataSyncService] MySQL更新失败，将稍后重试: {ex.Message}");
                    // 可以在这里添加重试队列逻辑
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 更新断头锭子状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加单个断头锭子并同步到MySQL和SQLite
        /// </summary>
        /// <param name="sideNumber">边序号</param>
        /// <param name="spindleNumber">锭号</param>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> AddBrokenSpindleAsync(string sideNumber, int spindleNumber, string taskBatchId)
        {
            CheckInitialized();
            
            try
            {
                // 1. 保存到SQLite缓存
                bool sqliteSaved = await _sqliteHelper.SaveBrokenSpindleAsync(sideNumber, spindleNumber, taskBatchId);
                
                // 2. 尝试保存到MySQL主数据库
                bool mysqlSaved = false;
                try
                {
                    mysqlSaved = await _mysqlHelper.AddBrokenSpindleAsync(sideNumber, spindleNumber);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DataSyncService] MySQL保存失败，将稍后重试: {ex.Message}");
                    // 可以在这里添加重试队列逻辑
                }
                
                return sqliteSaved;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 添加断头锭子失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取任务进度信息
        /// </summary>
        /// <param name="taskBatchId">任务批次ID</param>
        /// <returns>任务进度信息</returns>
        public async Task<dynamic> GetTaskProgressAsync(string taskBatchId)
        {
            CheckInitialized();
            
            try
            {
                // 从SQLite获取任务进度
                return await _sqliteHelper.GetTaskProgressAsync(taskBatchId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSyncService] 获取任务进度失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 检查服务是否已初始化
        /// </summary>
        private void CheckInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("数据同步服务未初始化，请先调用Initialize方法");
            }
        }
    }
} 