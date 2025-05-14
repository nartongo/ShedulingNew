using System;
using System.Collections.Generic;

namespace ShedulingNew.BusinessLogic.Models
{
    /// <summary>
    /// 应用程序配置类
    /// 包含系统所有可配置参数，按功能模块分组
    /// 作为系统配置的根节点
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 数据库配置
        /// 包含MySQL和SQLite连接信息
        /// </summary>
        public DatabaseConfig Database { get; set; } = new();
        
        /// <summary>
        /// 机器人配置
        /// 包含机器人标识和基本信息
        /// </summary>
        public RobotConfig Robot { get; set; } = new();
        
        /// <summary>
        /// PLC配置
        /// 包含PLC连接和通信参数
        /// </summary>
        public PlcConfig Plc { get; set; } = new();
        
        /// <summary>
        /// AGV配置
        /// 包含AGV连接和控制参数
        /// </summary>
        public AgvConfig Agv { get; set; } = new();
        
        /// <summary>
        /// 后端配置
        /// 包含后端API连接和通信参数
        /// </summary>
        public BackendConfig Backend { get; set; } = new();
    }

    /// <summary>
    /// 数据库配置类
    /// 存储数据库连接字符串和相关配置
    /// </summary>
    public class DatabaseConfig
    {
        /// <summary>
        /// MySQL数据库连接字符串
        /// 用于连接远程主数据库
        /// </summary>
        public string MySqlConnectionString { get; set; } = "Server=localhost;Database=textiledb;User=root;Password=password;";
        
        /// <summary>
        /// SQLite数据库连接字符串
        /// 用于连接本地缓存数据库
        /// </summary>
        public string SQLiteConnectionString { get; set; } = "Data Source=localcache.db;Version=3;";
    }

    /// <summary>
    /// 机器人配置类
    /// 存储当前机器人的标识和基本信息
    /// </summary>
    public class RobotConfig
    {
        /// <summary>
        /// 机器人ID
        /// 系统中的唯一标识符
        /// </summary>
        public string MachineId { get; set; } = "R001";
        
        /// <summary>
        /// 机器人名称
        /// 用于显示和日志记录
        /// </summary>
        public string Name { get; set; } = "纺织修复机器人";
        
        /// <summary>
        /// 机器人型号
        /// 标识机器人的硬件型号
        /// </summary>
        public string Model { get; set; } = "TR-100";
        
        /// <summary>
        /// 工作站ID
        /// 机器人所属的工作站标识
        /// </summary>
        public string WorkstationId { get; set; } = "WS001";
        
        /// <summary>
        /// 软件版本号
        /// 用于追踪和报告系统版本
        /// </summary>
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// PLC配置类
    /// 存储PLC连接参数和地址映射
    /// </summary>
    public class PlcConfig
    {
        /// <summary>
        /// PLC的IP地址
        /// </summary>
        public string IpAddress { get; set; } = "192.168.1.10";
        
        /// <summary>
        /// PLC的通信端口，默认为502
        /// </summary>
        public int Port { get; set; } = 502;
        
        /// <summary>
        /// PLC轮询间隔，单位毫秒
        /// 系统定期查询PLC状态的时间间隔
        /// </summary>
        public int PollingIntervalMs { get; set; } = 1000;
        
        /// <summary>
        /// PLC地址映射表
        /// 将逻辑名称映射到实际PLC地址
        /// </summary>
        public Dictionary<string, int> Addresses { get; set; } = new Dictionary<string, int>
        {
            { "StartRepair", 1000 },    // 开始修复指令地址
            { "StopRepair", 1001 },     // 停止修复指令地址
            { "CurrentPosition", 2000 }, // 当前位置寄存器地址
            { "TargetPosition", 2001 }, // 目标位置寄存器地址
            { "StatusRegister", 3000 }, // 状态寄存器地址
            { "ErrorRegister", 3001 }   // 错误寄存器地址
        };
    }

    /// <summary>
    /// AGV配置类
    /// 存储AGV连接参数和控制设置
    /// </summary>
    public class AgvConfig
    {
        /// <summary>
        /// AGV的唯一标识符
        /// 用于在发送命令时指定目标AGV
        /// </summary>
        public string AgvId { get; set; } = "AGV1";
        
        /// <summary>
        /// AGV的IP地址
        /// </summary>
        public string IpAddress { get; set; } = "192.168.100.178";
        
        /// <summary>
        /// AGV的通信端口
        /// </summary>
        public int Port { get; set; } = 17804;
        
        /// <summary>
        /// AGV状态轮询间隔，单位毫秒
        /// 系统定期查询AGV状态的时间间隔
        /// </summary>
        public int PollingIntervalMs { get; set; } = 1000;
        
        /// <summary>
        /// AGV默认等待位置
        /// 任务结束后AGV返回的待命位置标识
        /// </summary>
        public string DefaultWaitPosition { get; set; } = "W001";
    }

    /// <summary>
    /// 后端配置类
    /// 存储后端API连接参数和通信设置
    /// </summary>
    public class BackendConfig
    {
        /// <summary>
        /// API基础URL
        /// 后端服务的基础地址
        /// </summary>
        public string ApiBaseUrl { get; set; } = "http://localhost:8080/api";
        
        /// <summary>
        /// API访问密钥
        /// 用于API认证，确保安全访问
        /// </summary>
        public string ApiKey { get; set; } = "";
        
        /// <summary>
        /// 状态上报间隔，单位毫秒
        /// 系统向后端上报状态的时间间隔
        /// </summary>
        public int StatusReportIntervalMs { get; set; } = 5000;
        
        /// <summary>
        /// RabbitMQ主机地址
        /// </summary>
        public string RabbitMqHost { get; set; } = "localhost";
        
        /// <summary>
        /// RabbitMQ端口
        /// </summary>
        public int RabbitMqPort { get; set; } = 5672;
        
        /// <summary>
        /// RabbitMQ用户名
        /// </summary>
        public string RabbitMqUser { get; set; } = "guest";
        
        /// <summary>
        /// RabbitMQ密码
        /// </summary>
        public string RabbitMqPwd { get; set; } = "guest";
        
        /// <summary>
        /// RabbitMQ虚拟主机
        /// </summary>
        public string RabbitMqVHost { get; set; } = "/";
    }
} 