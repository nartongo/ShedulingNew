using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using ShedulingNew.BusinessLogic.Models;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// 配置服务类
    /// 负责系统配置的加载、保存和更新
    /// 使用单例模式确保全局只有一个配置实例
    /// </summary>
    public class ConfigService
    {
        // 单例实例和线程安全锁
        private static ConfigService _instance;
        private static readonly object _lock = new object();
        
        // 配置对象和配置文件路径
        private AppConfig _config;
        private readonly string _configFilePath;
        
        /// <summary>
        /// 获取ConfigService的全局单例实例
        /// 线程安全的延迟初始化实现
        /// </summary>
        public static ConfigService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConfigService();
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 私有构造函数，实现单例模式
        /// 初始化配置文件路径并加载配置
        /// </summary>
        private ConfigService()
        {
            // 配置文件位于应用程序根目录下的appsettings.json
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            // 初始化时加载配置
            LoadConfiguration();
        }
        
        /// <summary>
        /// 获取当前配置对象
        /// 只读属性，防止外部直接修改配置
        /// </summary>
        public AppConfig Config => _config;
        
        /// <summary>
        /// 加载配置
        /// 从JSON文件加载配置，如果文件不存在则创建默认配置
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                // 检查配置文件是否存在
                if (!File.Exists(_configFilePath))
                {
                    // 如果不存在，创建默认配置并保存
                    _config = new AppConfig();
                    SaveConfiguration();
                    return;
                }
                
                // 使用Microsoft.Extensions.Configuration从JSON文件加载配置
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                
                IConfigurationRoot configuration = configBuilder.Build();
                
                // 将配置绑定到AppConfig对象
                _config = new AppConfig();
                configuration.Bind(_config);
            }
            catch (Exception ex)
            {
                // 加载失败时使用默认配置，确保系统能够继续运行
                _config = new AppConfig();
                Console.WriteLine($"配置加载失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 保存配置
        /// 将当前配置对象序列化为JSON并保存到文件
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                // 配置JSON序列化选项
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,  // 格式化JSON，使其更易读
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never  // 不忽略任何属性
                };
                
                // 序列化配置对象为JSON
                string json = JsonSerializer.Serialize(_config, options);
                // 写入配置文件
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"配置保存失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新并保存配置
        /// 通过委托更新配置，然后保存到文件
        /// </summary>
        /// <param name="updateAction">配置更新委托，用于修改配置</param>
        public void UpdateAndSave(Action<AppConfig> updateAction)
        {
            // 调用委托更新配置
            updateAction(_config);
            // 保存更新后的配置
            SaveConfiguration();
        }
    }
} 