using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Text; // For Encoding
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Services.AgvStructures;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// AGV状态数据类 (AGV Status Data Class)
    /// </summary>
    public class AgvStatus
    {
        // 位置与朝向 (Position and Heading)
        public float X { get; set; } // X坐标
        public float Y { get; set; } // Y坐标
        public float Heading { get; set; } // 朝向角

        // 运行速度 (Operational Speed)
        public float Vx { get; set; } // X方向速度
        public float Vy { get; set; } // Y方向速度
        public float Omega { get; set; } // 角速度

        // 电池状态 (Battery Status)
        public float StateOfCharge { get; set; }  // 百分比 0~100 (Percentage 0~100)
        public float Voltage { get; set; }        // 单位 V (Unit: V)
        public float Current { get; set; }        // 单位 A (Unit: A)
        public bool IsCharging { get; set; }      // true=充电 (charging)，false=放电 (discharging)

        // --- 新增的详细状态属性 ---
        // Location Status Info from 0xAF response
        public uint LastPassedPointId { get; set; } // 最后通过点 ID
        public uint CurrentPointSequenceNumber { get; set; } // 当前任务中的点序列号
        public byte LocationConfidence { get; set; } // 定位置信度

        // Running Status Info from 0xAF response
        public byte WorkMode { get; set; } // 工作模式 (例如: 手动, 自动)
        public byte AgvOperationalStatus { get; set; } // AGV运行状态 (例如: 空闲, 运行中, 暂停)
        public byte CapabilitySetStatus { get; set; } // 机器人能力集设置状态

        // Task Status Info from 0xAF response
        public uint CurrentOrderId { get; set; } // 当前订单ID
        public uint CurrentTaskKey { get; set; } // 当前任务KEY
        public byte RemainingPointsInTask { get; set; } // 当前任务剩余点数量
        public byte RemainingPathsInTask { get; set; } // 当前任务剩余段数量
    }

    /// <summary>
    /// AGV通信服务类 (AGV Communication Service Class)
    /// </summary>
    public class AGVService
    {
        private EventHub _eventHub; // 事件中心实例
        private CancellationTokenSource _cancellationTokenSource; // 用于取消异步操作的Token源
        private bool _isRunning; // 服务是否正在运行的标志
        
        private UdpClient _udpClient; // UDP客户端
        private IPEndPoint _agvEndPoint; // AGV的IP和端口信息
        private const string AGV_IP = "192.168.100.178"; // 默认AGV IP地址
        private const int AGV_PORT = 17804; // 默认AGV端口号
        
        private ushort _sequenceNumber = 0; // 通信序列号
        
        private readonly byte[] _authCode = new byte[16]; // TODO: 替换为实际授权码

        // 用于简单导航 (0x16 命令) 的跟踪字段
        private uint _lastCommandedOrderId = 0; // 上一个简单导航指令的订单ID (上位机跟踪用)
        private uint _lastCommandedTaskKey = 0; // 上一个简单导航指令的任务KEY (上位机跟踪用)
        private uint _lastCommandedTargetPointId = 0; // 上一个简单导航指令的目标点ID
        private bool _isNavigateToPointCommandActive = false; // 标志，指示是否正在监控一个简单点导航指令的到达情况

        // 用于路径导航 (0xAE 命令) 的跟踪字段
        private uint _lastCommandedPathOrderId = 0; // 上一个路径导航指令的订单ID
        private uint _lastCommandedPathTaskKey = 0; // 上一个路径导航指令的任务KEY
        private bool _isNavigateWithPathCommandActive = false; // 标志，指示是否正在监控一个路径导航指令的完成情况

        
        public AGVService()
        {
            _eventHub = EventHub.Instance; // 假设EventHub是单例模式
            // TODO: 使用供应商提供的实际授权码初始化 _authCode
            // Example: _authCode = new byte[] { 0x01, 0x02, ..., 0x10 };
        }
        
        /// <summary>
        /// 配置AGV连接参数
        /// </summary>
        /// <param name="ipAddress">AGV的IP地址</param>
        /// <param name="port">AGV的端口号</param>
        public void Configure(string ipAddress = AGV_IP, int port = AGV_PORT)
        {
            try
            {
                _udpClient = new UdpClient();
                _agvEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                Console.WriteLine($"[AGVService] 已配置AGV连接: IP={ipAddress}, Port={port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 配置AGV连接失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动AGV通信服务
        /// </summary>
        public void Start()
        {
            if (_isRunning) return; // 如果已在运行，则不执行任何操作
            try
            {
                if (_udpClient == null) Configure(); // 如果尚未配置，则使用默认值进行配置
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                // 启动AGV通信循环的后台任务
                System.Threading.Tasks.Task.Run(() => RunAGVCommunication(_cancellationTokenSource.Token));
                Console.WriteLine("[AGVService] AGV通信服务已启动。");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Console.WriteLine($"[AGVService] 启动AGV通信服务失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 停止AGV通信服务
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return; // 如果未运行，则不执行任何操作
            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel(); // 请求取消通信循环
                _udpClient?.Close(); // 关闭UDP客户端
                _udpClient = null;
                Console.WriteLine("[AGVService] AGV通信服务已停止。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 停止AGV通信服务失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 运行AGV通信循环，定期查询状态并检查导航任务完成情况
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的Token</param>
        private async System.Threading.Tasks.Task RunAGVCommunication(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested) // 循环直到请求取消
            {
                try
                {
                    AgvStatus status = await QueryStatusAsync(); // 查询AGV状态
                    if (status != null)
                    {
                        _eventHub.Publish("AGVStatusChanged", status); // 发布状态变更事件

                        // 简单导航 (0x16) 到达判断
                        if (_isNavigateToPointCommandActive)
                        {
                            bool isIdle = status.AgvOperationalStatus == 0x00; // AGV是否空闲
                            bool isAtTargetPointId = status.LastPassedPointId == _lastCommandedTargetPointId; // AGV最后通过点是否为目标点
                            
                            // 对于0x16命令，任务完成的判断较为间接
                            bool taskConsideredComplete = true; // 默认认为完成，除非有更明确的失败或进行中指示
                            // 如果AGV上报的OrderID与我们跟踪的ID一致 (虽然0x16不直接传递，但AGV可能有自己的逻辑)
                            if (status.CurrentOrderId != 0 && status.CurrentOrderId == _lastCommandedOrderId)
                            {
                                taskConsideredComplete = status.RemainingPointsInTask == 0; // 检查该任务是否还有剩余点
                            } 
                            // 如果AGV清空了任务ID，也认为之前派发的简单导航任务已结束
                            else if (status.CurrentOrderId == 0 && _lastCommandedOrderId != 0) {
                                taskConsideredComplete = true;
                            }
                            // 注意：如果AGV在执行0x16任务时，其CurrentOrderId和CurrentTaskKey没有可预测的行为，
                            // 那么这里的taskConsideredComplete判断可能需要进一步依赖AGV的特定行为或文档说明。

                            if (isAtTargetPointId && isIdle && taskConsideredComplete)
                            {
                                Console.WriteLine($"[AGVService] 事件: AGV到达目标点 ID: {_lastCommandedTargetPointId} (通过0x16命令发起). AGV状态: 空闲。");
                                _eventHub.Publish("AGVTargetReached", new 
                                { 
                                    OrderId = _lastCommandedOrderId, 
                                    TaskKey = _lastCommandedTaskKey, 
                                    TargetPointId = _lastCommandedTargetPointId,
                                    CommandType = "0x16"
                                });
                                _isNavigateToPointCommandActive = false; // 重置简单导航跟踪标志
                            }
                        }

                        // 路径导航 (0xAE) 完成判断
                        if (_isNavigateWithPathCommandActive)
                        {
                            bool isIdle = status.AgvOperationalStatus == 0x00; // AGV是否空闲
                            // 路径任务完成的明确标志：OrderID和TaskKey匹配，且剩余点数和路径段数均为0
                            bool pathTaskCompleted = (status.CurrentOrderId == _lastCommandedPathOrderId &&
                                                      status.CurrentTaskKey == _lastCommandedPathTaskKey &&
                                                      status.RemainingPointsInTask == 0 &&
                                                      status.RemainingPathsInTask == 0);
                            
                            // 或者，如果AGV清空了任务ID，也认为之前派发的路径任务已结束
                            bool pathTaskImplicitlyCompleted = (status.CurrentOrderId == 0 && _lastCommandedPathOrderId != 0);

                            if (isIdle && (pathTaskCompleted || pathTaskImplicitlyCompleted))
                            {
                                Console.WriteLine($"[AGVService] 事件: AGV完成路径导航任务 (通过0xAE命令发起). OrderId: {_lastCommandedPathOrderId}, TaskKey: {_lastCommandedPathTaskKey}. AGV状态: 空闲。");
                                _eventHub.Publish("AGVPathNavigationCompleted", new
                                {
                                    OrderId = _lastCommandedPathOrderId,
                                    TaskKey = _lastCommandedPathTaskKey,
                                    CommandType = "0xAE"
                                });
                                _isNavigateWithPathCommandActive = false; // 重置路径导航跟踪标志
                            }
                        }
                    }
                    await System.Threading.Tasks.Task.Delay(1000, cancellationToken); // 查询间隔1秒
                }
                catch (OperationCanceledException)
                {
                    break; // 接收到取消请求，正常退出循环
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AGVService] AGV通信循环中发生错误: {ex.Message}");
                    await System.Threading.Tasks.Task.Delay(5000, cancellationToken); // 等待5秒后重试
                }
            }
            Console.WriteLine("[AGVService] AGV通信循环已结束。");
        }

        #region 报文构建 (Packet Building)
        /// <summary>
        /// 获取下一个通信序列号
        /// </summary>
        private ushort GetNextSequenceNumber()
        {
            return _sequenceNumber++;
        }
        
        /// <summary>
        /// 构建报文头
        /// </summary>
        /// <param name="commandCode">命令码</param>
        /// <param name="dataLength">数据区长度</param>
        /// <returns>构建好的报文头字节数组</returns>
        private byte[] BuildPacketHeader(byte commandCode, ushort dataLength)
        {
            byte[] header = new byte[0x1C]; // 报文头固定长度28字节
            Buffer.BlockCopy(_authCode, 0, header, 0x00, 16); // 授权码
            header[0x10] = 0x01;        // 协议版本
            header[0x11] = 0x00;        // 报文类型 (请求)
            ushort seqNum = GetNextSequenceNumber();
            header[0x12] = (byte)(seqNum & 0xFF);       // 序列号 (低字节)
            header[0x13] = (byte)(seqNum >> 8);         // 序列号 (高字节)
            header[0x14] = 0x10;        // 服务码
            header[0x15] = commandCode; // 命令码
            header[0x16] = 0x00;        // 执行码 (请求报文为0)
            header[0x17] = 0x00;        // 保留
            header[0x18] = (byte)(dataLength & 0xFF);   // 数据区长度 (低字节)
            header[0x19] = (byte)(dataLength >> 8);     // 数据区长度 (高字节)
            header[0x1A] = 0x00;        // 保留
            header[0x1B] = 0x00;        // 保留
            return header;
        }

        /// <summary>
        /// 构建简单导航命令 (使用0x16命令码，仅指定目标点ID)。
        /// orderId 和 taskKey 参数在此方法构建0x16报文时不会被包含在发送给AGV的数据中，
        /// 但仍可用于上层应用的跟踪。
        /// </summary>
        /// <param name="orderId">订单ID (上层应用跟踪用)</param>
        /// <param name="taskKey">任务KEY (上层应用跟踪用)</param>
        /// <param name="targetPointId">目标点ID (将转换为ASCII字符串发送)</param>
        /// <returns>构建好的导航命令报文</returns>
        private byte[] BuildSimpleNavigationCommand(uint orderId, uint taskKey, uint targetPointId)
        {
            const byte COMMAND_CODE = 0x16; // 使用导航控制命令码
            const ushort DATA_LENGTH = 12;  // 0x16简单导航数据区长度 (操作1+导航方式1+指定路径1+交管1+路径点ID8 = 12)
            
            byte[] header = BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
            byte[] packet = new byte[header.Length + DATA_LENGTH];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length); // 复制报文头
            
            int offset = header.Length; // 数据区开始的偏移量
            
            // 填充0x16命令的特定字段
            packet[offset++] = 0x00; // 操作: 0x00 = 开始导航
            packet[offset++] = 0x00; // 导航方式: 0x00 = 导航到路径点 (目标点ID)
            packet[offset++] = 0x00; // 是否指定导航路径: 0x00 = 不指定
            packet[offset++] = 0x00; // 是否启用交通管理: 0x00 = 不启用

            // 路径点ID (U8[8], ASCII字符串)
            string pointIdStr = targetPointId.ToString();
            byte[] pointIdAscii = new byte[8]; // 初始化为全0 (null)
            byte[] tempAscii = Encoding.ASCII.GetBytes(pointIdStr);
            
            int lengthToCopy = Math.Min(tempAscii.Length, 8); // 确保不超过8字节
            Buffer.BlockCopy(tempAscii, 0, pointIdAscii, 0, lengthToCopy); // 不足部分保持为null
            
            Buffer.BlockCopy(pointIdAscii, 0, packet, offset, 8);
            // offset += 8; // 后面没有更多数据了
            
            return packet;
        }
        
        /// <summary>
        /// 构建完整导航命令 (使用0xAE命令码，指定详细的路径点和路径段结构)
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <param name="taskKey">任务KEY</param>
        /// <param name="points">路径点结构列表</param>
        /// <param name="paths">路径段结构列表 (可以为null或空)</param>
        /// <param name="navigationMode">导航方式 (U8), 例如 0x00: 路径拼接 (对应文档0xAE命令中偏移0AH的字段)</param>
        /// <returns>构建好的导航命令报文</returns>
        private byte[] BuildFullNavigationCommand(uint orderId, uint taskKey, 
                                                  List<AgvPointStructure> points, 
                                                  List<AgvPathStructure> paths,
                                                  byte navigationMode = 0x00) // 默认导航方式为路径拼接
        {
            const byte COMMAND_CODE = 0xAE; // 下发混合导航任务命令码

            // 计算数据区总长度
            // 基础部分: 订单ID(4) + 任务KEY(4) + 点数量(1) + 边数量(1) + 导航方式(1) + 预留(1) = 12字节
            int currentDataLength = 12; 
            foreach (var point in points)
            {
                currentDataLength += point.GetByteLength();
            }
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    currentDataLength += path.GetByteLength();
                }
            }
            ushort totalDataLength = (ushort)currentDataLength;

            byte[] header = BuildPacketHeader(COMMAND_CODE, totalDataLength);
            byte[] packet = new byte[header.Length + totalDataLength];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length); // 复制报文头
            
            int offset = header.Length; // 数据区开始的偏移量
            
            // 订单ID (U32)
            Buffer.BlockCopy(BitConverter.GetBytes(orderId), 0, packet, offset, 4);
            offset += 4;
            // 任务KEY (U32)
            Buffer.BlockCopy(BitConverter.GetBytes(taskKey), 0, packet, offset, 4);
            offset += 4;
            
            // 点信息结构体数组中结构体个数 (U8)
            packet[offset++] = (byte)points.Count;
            // 边信息结构体数组中结构体个数 (U8)
            packet[offset++] = (byte)(paths?.Count ?? 0);
            // 导航方式 (U8) - 对应文档0xAE命令中偏移0AH的字段
            packet[offset++] = navigationMode; 
            // 预留 (U8) - 对应文档0xAE命令中偏移0BH的字段
            packet[offset++] = 0x00; 
            
            // 任务中路径点信息结构体 (Point[point_size])
            foreach (var point in points)
            {
                offset = point.Serialize(packet, offset);
            }
            
            // 任务中路径段信息结构体 (Path[path_size])
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    offset = path.Serialize(packet, offset);
                }
            }
            return packet;
        }
        #endregion

        #region UDP通信 (UDP Communication)
        /// <summary>
        /// 异步发送UDP报文
        /// </summary>
        /// <param name="packet">要发送的报文</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendPacketAsync(byte[] packet)
        {
            try
            {
                if (_udpClient == null)
                {
                    Console.WriteLine("[AGVService] UDP客户端未初始化。使用默认值进行配置。");
                    Configure(); // 如果为null，尝试配置
                    if (_udpClient == null) throw new InvalidOperationException("UDP客户端初始化失败。");
                }
                await _udpClient.SendAsync(packet, packet.Length, _agvEndPoint);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 发送UDP报文失败: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region AGV移动控制 (AGV Motion Control)
        /// <summary>
        /// 简单导航命令 (仅指定目标点，使用0x16命令)
        /// </summary>
        /// <param name="orderId">订单ID (上层应用跟踪用)</param>
        /// <param name="taskKey">任务KEY (上层应用跟踪用)</param>
        /// <param name="targetPointId">目标点ID</param>
        /// <returns>命令是否成功发送</returns>
        public async Task<bool> NavigateToPointAsync(uint orderId, uint taskKey, uint targetPointId)
        {
            try
            {
                byte[] packet = BuildSimpleNavigationCommand(orderId, taskKey, targetPointId);
                bool success = await SendPacketAsync(packet);
                
                if (success)
                {
                    Console.WriteLine($"[AGVService] (使用0x16) 导航命令已发送: 目标点={targetPointId}。(上层跟踪ID: Order={orderId}, Task={taskKey})");
                    
                    // 设置跟踪字段，即使0x16不直接使用orderId/taskKey与AGV通信，上层仍可跟踪
                    _lastCommandedOrderId = orderId; 
                    _lastCommandedTaskKey = taskKey;
                    _lastCommandedTargetPointId = targetPointId;
                    _isNavigateToPointCommandActive = true; // 激活简单导航跟踪
                    _isNavigateWithPathCommandActive = false; // 确保路径导航跟踪未激活

                    // 发布导航开始事件
                    _eventHub.Publish("AGVNavigationStarted", new 
                    { 
                        OrderId = orderId,
                        TaskKey = taskKey,
                        TargetPoint = targetPointId,
                        CommandType = "0x16" 
                    });
                }
                else
                {
                    Console.WriteLine($"[AGVService] (使用0x16) 发送导航命令失败。");
                    _isNavigateToPointCommandActive = false;
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] (使用0x16) 执行导航命令时发生错误: {ex.Message}");
                _isNavigateToPointCommandActive = false;
                return false;
            }
        }
        
        /// <summary>
        /// 完整导航命令 (使用0xAE命令码，指定详细的路径点和路径段结构)
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <param name="taskKey">任务KEY</param>
        /// <param name="points">路径点结构列表</param>
        /// <param name="paths">路径段结构列表 (可以为null或空)</param>
        /// <param name="navigationMode">导航方式 (U8), 例如 0x00: 路径拼接 (对应文档0xAE命令中偏移0AH的字段)</param>
        /// <returns>命令是否成功发送</returns>
        public async Task<bool> NavigateWithPathAsync(uint orderId, uint taskKey, 
                                                      List<AgvPointStructure> points, 
                                                      List<AgvPathStructure> paths,
                                                      byte navigationMode = 0x00)
        {
            try
            {
                if (points == null || points.Count == 0)
                {
                    throw new ArgumentException("必须至少指定一个路径点 (points列表不能为空)。");
                }
                
                byte[] packet = BuildFullNavigationCommand(orderId, taskKey, points, paths, navigationMode);
                bool success = await SendPacketAsync(packet);
                
                if (success)
                {
                    Console.WriteLine($"[AGVService] (使用0xAE) 路径导航命令已发送: 订单={orderId}, 任务={taskKey}, 点数量={points.Count}, 路径段数量={paths?.Count ?? 0}");
                    
                    // 设置路径导航跟踪ID
                    _lastCommandedPathOrderId = orderId; 
                    _lastCommandedPathTaskKey = taskKey;
                    _isNavigateWithPathCommandActive = true; // 激活路径导航跟踪
                    _isNavigateToPointCommandActive = false; // 确保简单导航跟踪未激活


                    _eventHub.Publish("AGVNavigationStarted", new 
                    { 
                        OrderId = orderId,
                        TaskKey = taskKey,
                        PointCount = points.Count,
                        PathCount = paths?.Count ?? 0,
                        CommandType = "0xAE"
                    });
                }
                else
                {
                    Console.WriteLine($"[AGVService] (使用0xAE) 发送路径导航命令失败。");
                    _isNavigateWithPathCommandActive = false;
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] (使用0xAE) 执行路径导航命令时发生错误: {ex.Message}");
                _isNavigateWithPathCommandActive = false;
                return false;
            }
        }
        #endregion

        #region AGV状态查询 (AGV Status Query)
        /// <summary>
        /// 构建状态查询命令 (0xAF)
        /// </summary>
        /// <returns>状态查询命令报文</returns>
        private byte[] BuildStatusQueryCommand()
        {
            const byte COMMAND_CODE = 0xAF; // 状态查询命令码
            const ushort DATA_LENGTH = 0;    // 数据区长度为0
            return BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
        }

        /// <summary>
        /// 异步查询AGV状态
        /// </summary>
        /// <returns>AGV状态对象，如果查询失败则为null</returns>
        private async Task<AgvStatus> QueryStatusAsync()
        {
            try
            {
                byte[] queryPacket = BuildStatusQueryCommand();
                bool sendSuccess = await SendPacketAsync(queryPacket);
                if (!sendSuccess)
                {
                    Console.WriteLine("[AGVService] 发送状态查询报文失败。");
                    return null;
                }
                if (_udpClient == null) // SendPacketAsync 应该已经初始化了
                {
                     Console.WriteLine("[AGVService] UDP客户端在尝试发送状态查询后仍为null。");
                     return null;
                }
                // 为接收数据设置超时
                var receiveTask = _udpClient.ReceiveAsync();
                if (await System.Threading.Tasks.Task.WhenAny(receiveTask, System.Threading.Tasks.Task.Delay(3000)) == receiveTask && receiveTask.IsCompletedSuccessfully) // 3秒超时
                {
                    UdpReceiveResult result = receiveTask.Result;
                    if (result.Buffer.Length < 28) // 最小报文头长度
                    {
                        Console.WriteLine("[AGVService] 接收到的状态响应数据过短 (小于报文头长度)。");
                        return null;
                    }
                    return ParseStatusResponse(result.Buffer); // 解析响应数据
                }
                else
                {
                    Console.WriteLine("[AGVService] 接收状态响应超时或发生错误。");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 查询AGV状态时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析AGV状态响应报文 (0xAF)。
        /// </summary>
        /// <param name="data">收到的原始字节数据</param>
        /// <returns>解析后的AgvStatus对象，如果解析失败则为null</returns>
        private AgvStatus ParseStatusResponse(byte[] data)
        {
            try
            {
                int payloadStartOffset = 28; // AGV状态数据在28字节的通用报文头之后开始。
                if (data.Length < payloadStartOffset + 4) // 初始大小字段的最小长度 (abnormal_size等)
                {
                    Console.WriteLine($"[AGVService] ParseStatusResponse: 报文过短，无法解析初始字段。长度: {data.Length}");
                    return null;
                }
                AgvStatus status = new AgvStatus();
                
                // --- 位置状态信息 (LocationStatusInfo) --- (大小 0x20 = 32 字节)
                int locStatusBase = payloadStartOffset + 0x04; // 根据文档，LocationStatusInfo[] 紧随 abnormal_size, action_size, info_size (各1字节) 之后
                if (data.Length < locStatusBase + 32) {
                    Console.WriteLine($"[AGVService] ParseStatusResponse: LocationStatusInfo数据不足。长度: {data.Length}, 期望至少: {locStatusBase + 32}");
                    return null; 
                }
                status.X = BitConverter.ToSingle(data, locStatusBase + 0x00);
                status.Y = BitConverter.ToSingle(data, locStatusBase + 0x04);
                status.Heading = BitConverter.ToSingle(data, locStatusBase + 0x08);
                status.LastPassedPointId = BitConverter.ToUInt32(data, locStatusBase + 0x0C);
                status.CurrentPointSequenceNumber = BitConverter.ToUInt32(data, locStatusBase + 0x14);
                status.LocationConfidence = data[locStatusBase + 0x18];

                // --- 运行状态信息 (RunningStatusInfo) --- (大小 0x14 = 20 字节)
                int runStatusBase = payloadStartOffset + 0x24; // LocationStatusInfo (32B) 之后
                 if (data.Length < runStatusBase + 20) {
                    Console.WriteLine($"[AGVService] ParseStatusResponse: RunningStatusInfo数据不足。长度: {data.Length}, 期望至少: {runStatusBase + 20}");
                    return null; 
                }
                status.Vx = BitConverter.ToSingle(data, runStatusBase + 0x00);
                status.Vy = BitConverter.ToSingle(data, runStatusBase + 0x04);
                status.Omega = BitConverter.ToSingle(data, runStatusBase + 0x08);
                status.WorkMode = data[runStatusBase + 0x0C];
                status.AgvOperationalStatus = data[runStatusBase + 0x0D];
                status.CapabilitySetStatus = data[runStatusBase + 0x0E];

                // --- 任务状态信息 (TaskStatusInfo) --- (基本大小 0x0C = 12 字节, 后跟动态列表)
                int taskStatusBase = payloadStartOffset + 0x38; // RunningStatusInfo (20B) 之后
                if (data.Length < taskStatusBase + 12) { // 检查TaskStatusInfo的基础部分
                    Console.WriteLine($"[AGVService] ParseStatusResponse: TaskStatusInfo基本数据不足。长度: {data.Length}, 期望至少: {taskStatusBase + 12}");
                    return null; 
                }
                status.CurrentOrderId = BitConverter.ToUInt32(data, taskStatusBase + 0x00);
                status.CurrentTaskKey = BitConverter.ToUInt32(data, taskStatusBase + 0x04);
                status.RemainingPointsInTask = data[taskStatusBase + 0x08]; // point_size
                status.RemainingPathsInTask = data[taskStatusBase + 0x09]; // path_size
                
                // 计算TaskStatusInfo的动态部分的长度
                int taskStatusDynamicPartLength = (status.RemainingPointsInTask * 8) + (status.RemainingPathsInTask * 8);
                int taskStatusEndOffset = taskStatusBase + 12 + taskStatusDynamicPartLength;

                // --- 电池状态信息 (BatteryStatusInfo) --- (大小 0x14 = 20字节)
                int batteryStatusBase = taskStatusEndOffset; // 紧随TaskStatusInfo的动态部分之后
                if (data.Length < batteryStatusBase + 20) { 
                    Console.WriteLine($"[AGVService] ParseStatusResponse: BatteryStatusInfo数据不足。长度: {data.Length}, 期望至少: {batteryStatusBase + 20}. 计算偏移: {batteryStatusBase}");
                    // 设置默认值或错误指示符
                    status.StateOfCharge = -1f; status.Voltage = -1f; status.Current = -1f; status.IsCharging = false;
                } else {
                    status.StateOfCharge = BitConverter.ToSingle(data, batteryStatusBase + 0x00) * 100;
                    status.Voltage = BitConverter.ToSingle(data, batteryStatusBase + 0x04);
                    status.Current = BitConverter.ToSingle(data, batteryStatusBase + 0x08);
                    status.IsCharging = data[batteryStatusBase + 0x0C] != 0;
                }
                return status;
            }
            catch (IndexOutOfRangeException ioex)
            {
                Console.WriteLine($"[AGVService] 解析状态响应因数据长度不足而出错: {ioex.Message}. 数据长度: {data.Length}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 解析状态响应时发生错误: {ex.Message}");
                return null;
            }
        }
        #endregion

        /// <summary>
        /// 发送AGV指令 (将高级指令转换为具体的导航命令)
        /// </summary>
        /// <param name="agvId">AGV标识符</param>
        /// <param name="command">指令类型，如"GoToSwitchPoint"、"GoToWaitPoint"等</param>
        /// <param name="parameters">可选参数</param>
        /// <returns>是否成功发送指令</returns>
        public async Task<bool> SendCommandToAGV(string agvId, string command, Dictionary<string, object> parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(agvId) || string.IsNullOrEmpty(command))
                {
                    throw new ArgumentException("AGV ID和指令类型不能为空。");
                }
                // 生成唯一的订单ID和任务ID (AGV使用，不要弄错)
                uint orderId = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                uint taskKey = (uint)new Random().Next(1000, 9999); // 简单任务KEY生成
                
                switch (command.ToUpper())
                {
                    case "GOTOSWITCHPOINT": // 前往切换点
                        uint switchPointId = parameters != null && parameters.ContainsKey("SwitchPointId") 
                            ? Convert.ToUInt32(parameters["SwitchPointId"]) 
                            : 1001; // 默认切换点ID
                        
                        Console.WriteLine($"[AGVService] 指令AGV前往切换点: ID={switchPointId}");
                        return await NavigateToPointAsync(orderId, taskKey, switchPointId);
                        
                    case "GOTOWAITPOINT": // 前往待命点
                        uint waitPointId = parameters != null && parameters.ContainsKey("WaitPointId") 
                            ? Convert.ToUInt32(parameters["WaitPointId"]) 
                            : 1002; // 默认待命点ID
                        
                        Console.WriteLine($"[AGVService] 指令AGV前往待命点: ID={waitPointId}");
                        return await NavigateToPointAsync(orderId, taskKey, waitPointId);
                        
                    // 示例: 通过SendCommandToAGV发送路径任务
                    // case "NAVIGATEFULLPATH":
                    //    if (parameters != null && 
                    //        parameters.ContainsKey("Points") && parameters["Points"] is List<AgvPointStructure> points &&
                    //        parameters.ContainsKey("Paths") && parameters["Paths"] is List<AgvPathStructure> paths) // 确保类型正确
                    //    {
                    //        byte navMode = parameters.ContainsKey("NavigationMode") ? Convert.ToByte(parameters["NavigationMode"]) : (byte)0x00;
                    //        Console.WriteLine($"[AGVService] 指令AGV执行完整路径导航");
                    //        return await NavigateWithPathAsync(orderId, taskKey, points, paths, navMode);
                    //    }
                    //    else
                    //    {
                    //        Console.WriteLine("[AGVService] NAVIGATEFULLPATH 命令缺少 Points 或 Paths 参数，或者参数类型不正确。");
                    //        return false;
                    //    }
                        
                    default:
                        Console.WriteLine($"[AGVService] 不支持的指令类型: {command}");
                        return false; // 或者抛出异常
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 发送AGV指令失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查AGV是否已到达上一个简单导航指令 (0x16) 的目标点。
        /// </summary>
        public async Task<bool> HasReachedLastTargetAsync()
        {
            if (!_isRunning || !_isNavigateToPointCommandActive) // 仅当简单导航激活时检查
            {
                if (!_isNavigateToPointCommandActive) Console.WriteLine("[AGVService] HasReachedLastTargetAsync: 当前没有简单导航指令正在监控。");
                return false;
            }
            AgvStatus currentStatus = await QueryStatusAsync();
            if (currentStatus == null)
            {
                Console.WriteLine("[AGVService] HasReachedLastTargetAsync: 查询AGV状态失败。");
                return false;
            }

            bool isIdle = currentStatus.AgvOperationalStatus == 0x00;
            bool isAtTargetPointId = currentStatus.LastPassedPointId == _lastCommandedTargetPointId;
            
            bool taskConsideredComplete = true; 
            if (currentStatus.CurrentOrderId != 0 && currentStatus.CurrentOrderId == _lastCommandedOrderId)
            {
                taskConsideredComplete = currentStatus.RemainingPointsInTask == 0;
            }
            else if (currentStatus.CurrentOrderId == 0 && _lastCommandedOrderId != 0)
            {
                taskConsideredComplete = true;
            }

            if (isIdle && isAtTargetPointId && taskConsideredComplete)
            {
                Console.WriteLine($"[AGVService] HasReachedLastTargetAsync: AGV已到达目标点 ID: {_lastCommandedTargetPointId}. AGV状态: 空闲，任务视为完成。");
                _isNavigateToPointCommandActive = false; // 重置标志
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查AGV是否已完成上一个路径导航任务 (0xAE)。
        /// </summary>
        public async Task<bool> HasCompletedLastPathTaskAsync()
        {
            if (!_isRunning || !_isNavigateWithPathCommandActive) // 仅当路径导航激活时检查
            {
                if (!_isNavigateWithPathCommandActive) Console.WriteLine("[AGVService] HasCompletedLastPathTaskAsync: 当前没有路径导航指令正在监控。");
                return false;
            }

            AgvStatus currentStatus = await QueryStatusAsync();
            if (currentStatus == null)
            {
                Console.WriteLine("[AGVService] HasCompletedLastPathTaskAsync: 查询AGV状态失败。");
                return false;
            }

            bool isIdle = currentStatus.AgvOperationalStatus == 0x00;
            bool pathTaskCompleted = (currentStatus.CurrentOrderId == _lastCommandedPathOrderId &&
                                      currentStatus.CurrentTaskKey == _lastCommandedPathTaskKey &&
                                      currentStatus.RemainingPointsInTask == 0 &&
                                      currentStatus.RemainingPathsInTask == 0);
            bool pathTaskImplicitlyCompleted = (currentStatus.CurrentOrderId == 0 && _lastCommandedPathOrderId != 0);


            if (isIdle && (pathTaskCompleted || pathTaskImplicitlyCompleted))
            {
                Console.WriteLine($"[AGVService] HasCompletedLastPathTaskAsync: AGV已完成路径任务. OrderId: {_lastCommandedPathOrderId}, TaskKey: {_lastCommandedPathTaskKey}. AGV状态: 空闲。");
                _isNavigateWithPathCommandActive = false; // 重置标志
                return true;
            }
            return false;
        }
    }
}
