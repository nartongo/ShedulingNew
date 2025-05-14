using System;
using System.Collections.Generic;
using System.Text; // Required for Encoding
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using ShedulingNew.BusinessLogic; // 假设 EventHub 在此命名空间

namespace ShedulingNew.BusinessLogic.Services.AgvStructures
{
    /// <summary>
    /// 表示AGV导航任务中的一个动作 (对应文档 0xAE 命令中的 Action 结构)
    /// </summary>
    public class AgvActionStructure
    {
        /// <summary>
        /// 动作类型 (U16) - 参见文档附录一
        /// </summary>
        public ushort ActionType { get; set; }

        /// <summary>
        /// 执行动作并行方式 (U8)
        /// 0x00: 移动和动作间都可并行
        /// 0x01: 动作间可以并行, 不能移动
        /// 0x02: 只能执行当前动作
        /// </summary>
        public byte ParallelExecutionMode { get; set; }

        // 预留 (U8) - 在序列化时处理

        /// <summary>
        /// 动作ID (U32) - 要求每个动作ID唯一
        /// </summary>
        public uint ActionId { get; set; }

        // 参数长度 (U8, param_size) - 将根据 Parameters 自动计算
        // 预留 (U8[3]) - 在序列化时处理

        /// <summary>
        /// 参数内容 (byte[]) - 由对应动作决定，需要4字节对齐
        /// </summary>
        public byte[] Parameters { get; set; } = []; // 默认为空参数

        public AgvActionStructure(ushort actionType, uint actionId, byte parallelMode = 0x00, byte[] parameters = null)
        {
            ActionType = actionType;
            ActionId = actionId;
            ParallelExecutionMode = parallelMode;
            if (parameters != null)
            {
                // 确保参数长度是4的倍数 (文档要求4字节对齐)
                int originalLength = parameters.Length;
                int paddedLength = (originalLength + 3) & ~3; // 向上取整到最近的4的倍数
                if (originalLength != paddedLength)
                {
                    Parameters = new byte[paddedLength];
                    Buffer.BlockCopy(parameters, 0, Parameters, 0, originalLength);
                    // 剩余部分将保持为0 (null-padding)
                }
                else
                {
                    Parameters = parameters;
                }
            }
            else
            {
                Parameters = new byte[0];
            }
        }

        /// <summary>
        /// 获取此动作结构序列化后的字节长度
        /// </summary>
        public int GetByteLength()
        {
            // 动作类型(2) + 并行方式(1) + 预留(1) + 动作ID(4) + 参数长度(1) + 预留(3) + 参数内容(N)
            return 2 + 1 + 1 + 4 + 1 + 3 + Parameters.Length;
        }

        /// <summary>
        /// 将动作结构序列化到字节数组的指定偏移位置
        /// </summary>
        public int Serialize(byte[] buffer, int offset)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(ActionType), 0, buffer, offset, 2);
            offset += 2;
            buffer[offset++] = ParallelExecutionMode;
            buffer[offset++] = 0x00; // 预留 (U8)
            Buffer.BlockCopy(BitConverter.GetBytes(ActionId), 0, buffer, offset, 4);
            offset += 4;
            buffer[offset++] = (byte)Parameters.Length; // 参数长度
            buffer[offset++] = 0x00; // 预留 (U8[3])
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x00;
            if (Parameters.Length > 0)
            {
                Buffer.BlockCopy(Parameters, 0, buffer, offset, Parameters.Length);
                offset += Parameters.Length;
            }
            return offset;
        }
    }

    /// <summary>
    /// 表示AGV导航任务中的一个路径点 (对应文档 0xAE 命令中的 Point 结构)
    /// </summary>
    public class AgvPointStructure
    {
        /// <summary>
        /// 序列号 (U32) - 从0开始偶数递增 (例如: 0->2->4->6)
        /// </summary>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// 路径点 ID (U32)
        /// </summary>
        public uint PointId { get; set; }

        /// <summary>
        /// 指定角度时路径点的车头角度 (Float) - 单位: rad
        /// </summary>
        public float SpecifiedHeadingAngle { get; set; } = 0.0f;

        /// <summary>
        /// 是否指定路径点角度 (U8) - 1=指定; 0=不指定
        /// </summary>
        public byte IsHeadingAngleSpecified { get; set; } = 0;

        // 任务中点上动作信息个数 (U8, point_action_size) - 将根据 Actions 列表自动计算
        // 预留 (U8[6]) - 在序列化时处理

        /// <summary>
        /// 此路径点上的动作列表
        /// </summary>
        public List<AgvActionStructure> Actions { get; set; } = new List<AgvActionStructure>();

        public AgvPointStructure(uint sequenceNumber, uint pointId)
        {
            SequenceNumber = sequenceNumber;
            PointId = pointId;
        }

        /// <summary>
        /// 获取此路径点结构序列化后的字节长度
        /// </summary>
        public int GetByteLength()
        {
            // 序列号(4) + 路径点ID(4) + 车头角度(4) + 是否指定角度(1) + 动作个数(1) + 预留(6)
            int length = 4 + 4 + 4 + 1 + 1 + 6;
            foreach (var action in Actions)
            {
                length += action.GetByteLength();
            }
            return length;
        }

        /// <summary>
        /// 将路径点结构序列化到字节数组的指定偏移位置
        /// </summary>
        public int Serialize(byte[] buffer, int offset)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(SequenceNumber), 0, buffer, offset, 4);
            offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(PointId), 0, buffer, offset, 4);
            offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(SpecifiedHeadingAngle), 0, buffer, offset, 4);
            offset += 4;
            buffer[offset++] = IsHeadingAngleSpecified;
            buffer[offset++] = (byte)Actions.Count; // 动作个数

            // 预留 (U8[6])
            for (int i = 0; i < 6; i++) buffer[offset++] = 0x00;

            foreach (var action in Actions)
            {
                offset = action.Serialize(buffer, offset);
            }
            return offset;
        }
    }

    /// <summary>
    /// 表示AGV导航任务中的一个路径段 (对应文档 0xAE 命令中的 Path 结构)
    /// </summary>
    public class AgvPathStructure
    {
        /// <summary>
        /// 序列号 (U32) - 从1开始奇数递增 (例如: 1->3->5->7)
        /// </summary>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// 段 ID (U32)
        /// </summary>
        public uint PathId { get; set; }

        /// <summary>
        /// 机器人固定角度 (Float) - 单位 rad, 范围 0~2π (当IsAngleFixed为1或2时生效)
        /// </summary>
        public float FixedAngle { get; set; } = 0.0f;

        /// <summary>
        /// 是否固定角度 (U8)
        /// 0x00: 不使能
        /// 0x01: 固定地图角度 (FixedAngle生效)
        /// 0x02: 固定路径角度 (FixedAngle生效)
        /// </summary>
        public byte IsAngleFixed { get; set; } = 0;

        /// <summary>
        /// 行驶姿态 (U8)
        /// 0x00: 正走 (Forward)
        /// 0x01: 倒走 (Backward)
        /// 0x02: 左横移 (二维码导航时生效)
        /// 0x03: 右横移 (二维码导航时生效)
        /// </summary>
        public byte DrivingPosture { get; set; } = 0;

        // 任务中边上动作信息个数 (U8, action_size) - 将根据 Actions 列表自动计算
        // 预留 (U8) - 在序列化时处理

        /// <summary>
        /// 指定的目标最大速度 (Float) - 单位 m/s, 为0时使用地图默认速度
        /// </summary>
        public float MaxSpeed { get; set; } = 0.0f;

        /// <summary>
        /// 指定的目标最大角速度 (Float) - 单位 rad/s, 为0时使用地图默认角速度
        /// </summary>
        public float MaxAngularSpeed { get; set; } = 0.0f;

        // 预留 (U8[4]) - 在序列化时处理

        /// <summary>
        /// 此路径段上的动作列表
        /// </summary>
        public List<AgvActionStructure> Actions { get; set; } = new List<AgvActionStructure>();

        public AgvPathStructure(uint sequenceNumber, uint pathId)
        {
            SequenceNumber = sequenceNumber;
            PathId = pathId;
        }

        /// <summary>
        /// 获取此路径段结构序列化后的字节长度
        /// </summary>
        public int GetByteLength()
        {
            // 序列号(4) + 段ID(4) + 固定角度(4) + 是否固定角度(1) + 行驶姿态(1) + 动作个数(1) + 预留(1)
            // + 最大速度(4) + 最大角速度(4) + 预留(4)
            int length = 4 + 4 + 4 + 1 + 1 + 1 + 1 + 4 + 4 + 4;
            foreach (var action in Actions)
            {
                length += action.GetByteLength();
            }
            return length;
        }

        /// <summary>
        /// 将路径段结构序列化到字节数组的指定偏移位置
        /// </summary>
        public int Serialize(byte[] buffer, int offset)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(SequenceNumber), 0, buffer, offset, 4);
            offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(PathId), 0, buffer, offset, 4);
            offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(FixedAngle), 0, buffer, offset, 4);
            offset += 4;
            buffer[offset++] = IsAngleFixed;
            buffer[offset++] = DrivingPosture;
            buffer[offset++] = (byte)Actions.Count; // 动作个数
            buffer[offset++] = 0x00; // 预留 (U8)
            Buffer.BlockCopy(BitConverter.GetBytes(MaxSpeed), 0, buffer, offset, 4);
            offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(MaxAngularSpeed), 0, buffer, offset, 4);
            offset += 4;

            // 预留 (U8[4])
            for (int i = 0; i < 4; i++) buffer[offset++] = 0x00;

            foreach (var action in Actions)
            {
                offset = action.Serialize(buffer, offset);
            }
            return offset;
        }
    }
}