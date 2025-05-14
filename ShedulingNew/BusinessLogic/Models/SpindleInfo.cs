using System;

namespace ShedulingNew.BusinessLogic.Models
{
    /// <summary>
    /// 锭子信息类
    /// 存储锭子的ID、距离值等信息，用于断头修复任务
    /// </summary>
    public class SpindleInfo
    {
        /// <summary>
        /// 锭子ID
        /// </summary>
        public int SpindleId { get; set; }
        
        /// <summary>
        /// 距离值，用于PLC定位
        /// </summary>
        public int Distance { get; set; }
        
        /// <summary>
        /// 锭子状态：PENDING(待修复), REPAIRING(修复中), SUCCESS(修复成功), FAILED(修复失败)
        /// </summary>
        public string Status { get; set; } = "PENDING";
        
        /// <summary>
        /// 锭子所属机器ID
        /// </summary>
        public string MachineId { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
} 