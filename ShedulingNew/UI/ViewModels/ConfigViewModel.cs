using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ShedulingNew.BusinessLogic.Models;
using ShedulingNew.Coordinators;
using ShedulingNew.UI.Commands;

namespace ShedulingNew.UI.ViewModels
{
    public class ConfigViewModel : INotifyPropertyChanged
    {
        private AppConfig _originalConfig;
        private AppConfig _editingConfig;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        // 配置属性
        public DatabaseConfig Database { get; set; }
        public RobotConfig Robot { get; set; }
        public PlcConfig Plc { get; set; }
        public AgvConfig Agv { get; set; }
        public BackendConfig Backend { get; set; }
        
        // 命令
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        
        public ConfigViewModel()
        {
            // 获取原始配置的深拷贝
            _originalConfig = SystemCoordinator.Instance.Config;
            CreateEditingCopy();
            
            // 初始化命令
            SaveCommand = new RelayCommand(SaveConfig);
            ResetCommand = new RelayCommand(ResetConfig);
        }
        
        private void CreateEditingCopy()
        {
            // 创建正在编辑的配置的拷贝
            _editingConfig = new AppConfig
            {
                Database = new DatabaseConfig
                {
                    MySqlConnectionString = _originalConfig.Database.MySqlConnectionString,
                    SQLiteConnectionString = _originalConfig.Database.SQLiteConnectionString
                },
                Robot = new RobotConfig
                {
                    MachineId = _originalConfig.Robot.MachineId,
                    Name = _originalConfig.Robot.Name,
                    Model = _originalConfig.Robot.Model,
                    WorkstationId = _originalConfig.Robot.WorkstationId
                },
                Plc = new PlcConfig
                {
                    IpAddress = _originalConfig.Plc.IpAddress,
                    Port = _originalConfig.Plc.Port,
                    PollingIntervalMs = _originalConfig.Plc.PollingIntervalMs,
                    Addresses = new System.Collections.Generic.Dictionary<string, int>(_originalConfig.Plc.Addresses)
                },
                Agv = new AgvConfig
                {
                    IpAddress = _originalConfig.Agv.IpAddress,
                    Port = _originalConfig.Agv.Port,
                    PollingIntervalMs = _originalConfig.Agv.PollingIntervalMs,
                    DefaultWaitPosition = _originalConfig.Agv.DefaultWaitPosition
                },
                Backend = new BackendConfig
                {
                    ApiBaseUrl = _originalConfig.Backend.ApiBaseUrl,
                    ApiKey = _originalConfig.Backend.ApiKey,
                    StatusReportIntervalMs = _originalConfig.Backend.StatusReportIntervalMs
                }
            };
            
            // 设置UI绑定属性
            Database = _editingConfig.Database;
            Robot = _editingConfig.Robot;
            Plc = _editingConfig.Plc;
            Agv = _editingConfig.Agv;
            Backend = _editingConfig.Backend;
        }
        
        private void SaveConfig(object parameter)
        {
            try
            {
                // 更新系统配置
                SystemCoordinator.Instance.UpdateConfig(config => 
                {
                    // 数据库配置
                    config.Database.MySqlConnectionString = Database.MySqlConnectionString;
                    config.Database.SQLiteConnectionString = Database.SQLiteConnectionString;
                    
                    // 机器人配置
                    config.Robot.MachineId = Robot.MachineId;
                    config.Robot.Name = Robot.Name;
                    config.Robot.Model = Robot.Model;
                    config.Robot.WorkstationId = Robot.WorkstationId;
                    
                    // PLC配置
                    config.Plc.IpAddress = Plc.IpAddress;
                    config.Plc.Port = Plc.Port;
                    config.Plc.PollingIntervalMs = Plc.PollingIntervalMs;
                    // 注意：PLC地址映射在UI中没有编辑，所以这里不更新
                    
                    // AGV配置
                    config.Agv.IpAddress = Agv.IpAddress;
                    config.Agv.Port = Agv.Port;
                    config.Agv.PollingIntervalMs = Agv.PollingIntervalMs;
                    config.Agv.DefaultWaitPosition = Agv.DefaultWaitPosition;
                    
                    // 后端配置
                    config.Backend.ApiBaseUrl = Backend.ApiBaseUrl;
                    config.Backend.ApiKey = Backend.ApiKey;
                    config.Backend.StatusReportIntervalMs = Backend.StatusReportIntervalMs;
                });
                
                // 更新原始配置引用
                _originalConfig = SystemCoordinator.Instance.Config;
                
                MessageBox.Show("配置保存成功，部分配置可能需要重启应用后生效", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ResetConfig(object parameter)
        {
            CreateEditingCopy();
            OnPropertyChanged(nameof(Database));
            OnPropertyChanged(nameof(Robot));
            OnPropertyChanged(nameof(Plc));
            OnPropertyChanged(nameof(Agv));
            OnPropertyChanged(nameof(Backend));
        }
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    
} 