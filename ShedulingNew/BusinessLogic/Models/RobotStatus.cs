using System;

namespace ShedulingNew.BusinessLogic.Models
{
    /// <summary>
    /// 机器人状态类
    /// 描述机器人当前的各项状态指标，用于上报和存储
    /// </summary>
    public class RobotStatus
    {
        /// <summary>
        /// 机器人ID
        /// </summary>
        public string MachineId { get; set; }
        
        /// <summary>
        /// 工作状态：IDLE(空闲), BUSY(工作中), ERROR(错误), MAINTENANCE(维护中)
        /// </summary>
        public string WorkStatus { get; set; } = "IDLE";
        
        /// <summary>
        /// 当前位置
        /// </summary>
        public string CurrentPosition { get; set; } = "WAIT_POINT";
        
        /// <summary>
        /// 电池电量，百分比
        /// </summary>
        public int BatteryLevel { get; set; } = 100;
        
        /// <summary>
        /// 网络信号强度，百分比
        /// </summary>
        public int NetworkSignal { get; set; } = 100;
        
        /// <summary>
        /// 当前任务ID，如果无任务则为null或空
        /// </summary>
        public string CurrentTaskId { get; set; }
        
        /// <summary>
        /// 任务进度，百分比
        /// </summary>
        public int TaskProgress { get; set; } = 0;
        
        /// <summary>
        /// 断头修复总数
        /// </summary>
        public int TotalRepairs { get; set; } = 0;
        
        /// <summary>
        /// 成功修复数
        /// </summary>
        public int SuccessfulRepairs { get; set; } = 0;
        
        /// <summary>
        /// 失败修复数
        /// </summary>
        public int FailedRepairs { get; set; } = 0;
        
        /// <summary>
        /// 是否处于警告状态
        /// </summary>
        public bool IsWarning { get; set; } = false;
        
        /// <summary>
        /// 警告信息
        /// </summary>
        public string WarningMessage { get; set; }
        
        /// <summary>
        /// 是否处于错误状态
        /// </summary>
        public bool IsError { get; set; } = false;
        
        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
        
        /// <summary>
        /// 软件版本号
        /// </summary>
        public string SoftwareVersion { get; set; } = "1.0.0";
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 机器运行时间，单位秒
        /// </summary>
        public long UptimeSeconds { get; set; } = 0;
        
        /// <summary>
        /// 当前移动速度
        /// </summary>
        public double CurrentSpeed { get; set; } = 0.0;
        
        /// <summary>
        /// 获取当前状态的深拷贝
        /// </summary>
        /// <returns>状态的副本</returns>
        public RobotStatus Clone()
        {
            return new RobotStatus
            {
                MachineId = this.MachineId,
                WorkStatus = this.WorkStatus,
                CurrentPosition = this.CurrentPosition,
                BatteryLevel = this.BatteryLevel,
                NetworkSignal = this.NetworkSignal,
                CurrentTaskId = this.CurrentTaskId,
                TaskProgress = this.TaskProgress,
                TotalRepairs = this.TotalRepairs,
                SuccessfulRepairs = this.SuccessfulRepairs,
                FailedRepairs = this.FailedRepairs,
                IsWarning = this.IsWarning,
                WarningMessage = this.WarningMessage,
                IsError = this.IsError,
                ErrorMessage = this.ErrorMessage,
                SoftwareVersion = this.SoftwareVersion,
                LastUpdateTime = DateTime.UtcNow,
                UptimeSeconds = this.UptimeSeconds,
                CurrentSpeed = this.CurrentSpeed
            };
        }
    }
} 