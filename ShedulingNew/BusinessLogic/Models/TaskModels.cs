using System;
using System.Collections.Generic;

namespace ShedulingNew.BusinessLogic.Models
{
    /// <summary>
    /// 断头锭子信息
    /// </summary>
    public class BrokenSpindle
    {
        /// <summary>
        /// 唯一标识符
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// 所属细纱机ID
        /// </summary>
        public string MachineId { get; set; }
        
        /// <summary>
        /// 锭子距离值（用于定位）
        /// </summary>
        public int SpindleDistance { get; set; }
        
        /// <summary>
        /// 当前状态
        /// </summary>
        public string Status { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// 所属任务批次ID
        /// </summary>
        public string TaskBatchId { get; set; }
        
        /// <summary>
        /// 处理顺序
        /// </summary>
        public int ProcessOrder { get; set; }
        
        /// <summary>
        /// 备注
        /// </summary>
        public string Notes { get; set; }
    }
    
    /// <summary>
    /// 断头修复任务信息
    /// </summary>
    public class RepairTask
    {
        /// <summary>
        /// 唯一标识符
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// 任务批次ID
        /// </summary>
        public string TaskBatchId { get; set; }
        
        /// <summary>
        /// 细纱机ID
        /// </summary>
        public string MachineId { get; set; }
        
        /// <summary>
        /// 任务开始时间
        /// </summary>
        public DateTime? StartTime { get; set; }
        
        /// <summary>
        /// 任务结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// 任务状态
        /// </summary>
        public string Status { get; set; }
        
        /// <summary>
        /// 总共的断头锭子数量
        /// </summary>
        public int TotalSpindles { get; set; }
        
        /// <summary>
        /// 已完成的断头锭子数量
        /// </summary>
        public int CompletedSpindles { get; set; }
        
        /// <summary>
        /// 备注
        /// </summary>
        public string Notes { get; set; }
        
        /// <summary>
        /// 所有断头锭子的列表
        /// </summary>
        public List<BrokenSpindle> Spindles { get; set; } = new List<BrokenSpindle>();
    }
} 