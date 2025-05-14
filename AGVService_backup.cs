using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using ShedulingNew.BusinessLogic;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// AGV状态数据类
    /// </summary>
    public class AgvStatus
    {
        // 位置与朝向
        public float X { get; set; }
        public float Y { get; set; }
        public float Heading { get; set; }

        // 运行速度
        public float Vx { get; set; }
        public float Vy { get; set; }
        public float Omega { get; set; }

        // 电池状态
        public float StateOfCharge { get; set; }  // 百分比 0~100
        public float Voltage { get; set; }        // 单位 V
        public float Current { get; set; }        // 单位 A
        public bool IsCharging { get; set; }      // true=充电，false=放电
    }

    /// <summary>
    /// AGV通信服务类
    /// </summary>
    public class AGVService
    {
        private EventHub _eventHub;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private bool _isConnected;
        
        // UDP通信相关
        private UdpClient _udpClient;
        private IPEndPoint _agvEndPoint;
        private const string AGV_IP = "192.168.100.178";
        private const int AGV_PORT = 17804;
        
        // 通信序列号
        private ushort _sequenceNumber = 0;
        
        // 授权码 (需要从供应商获取)
        private readonly byte[] _authCode = new byte[16]; // TODO: 替换为实际授权码
        
        public AGVService()
        {
            _eventHub = EventHub.Instance;
        }
        
        /// <summary>
        /// 配置AGV连接参数
        /// </summary>
        public void Configure(string ipAddress = AGV_IP, int port = AGV_PORT)
        {
            try
            {
                // 创建UDP客户端
                _udpClient = new UdpClient();
                _agvEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                
                Console.WriteLine($"[AGVService] 已配置AGV连接参数: IP={ipAddress}, Port={port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 配置AGV连接参数失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动AGV通信服务
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            try
            {
                // 确保UDP客户端已创建
                if (_udpClient == null)
                {
                    Configure();
                }
                
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // 启动AGV通信线程
                Task.Run(() => RunAGVCommunication(_cancellationTokenSource.Token));
                
                Console.WriteLine("[AGVService] AGV通信服务启动成功");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Console.WriteLine($"[AGVService] AGV通信服务启动失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 停止AGV通信服务
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();
                
                // 关闭UDP客户端
                _udpClient?.Close();
                _udpClient = null;
                
                Console.WriteLine("[AGVService] AGV通信服务停止成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] AGV通信服务停止失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 运行AGV通信循环
        /// </summary>
        private async Task RunAGVCommunication(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 查询AGV状态
                    AgvStatus status = await QueryStatusAsync();
                    if (status != null)
                    {
                        // 发布状态变更事件
                        _eventHub.Publish("AGVStatusChanged", status);
                    }
                    
                    // 等待下一次查询
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 操作被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AGVService] AGV通信过程中发生错误: {ex.Message}");
                    await Task.Delay(5000, cancellationToken); // 延迟后重试
                }
            }
            
            Console.WriteLine("[AGVService] AGV通信循环结束");
        }

        #region 报文构建

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
        private byte[] BuildPacketHeader(byte commandCode, ushort dataLength)
        {
            byte[] header = new byte[0x1C]; // 报文头固定长度28字节
            
            // 复制授权码（16字节）
            Buffer.BlockCopy(_authCode, 0, header, 0x00, 16);
            
            // 填充其他字段
            header[0x10] = 0x01;            // 协议版本
            header[0x11] = 0x00;            // 报文类型（请求）
            
            // 序列号（2字节）
            ushort seqNum = GetNextSequenceNumber();
            header[0x12] = (byte)(seqNum & 0xFF);
            header[0x13] = (byte)(seqNum >> 8);
            
            header[0x14] = 0x10;            // 服务码
            header[0x15] = commandCode;      // 命令码
            header[0x16] = 0x00;            // 执行码（请求报文填0）
            header[0x17] = 0x00;            // 保留
            
            // 数据区长度（2字节）
            header[0x18] = (byte)(dataLength & 0xFF);
            header[0x19] = (byte)(dataLength >> 8);
            
            // 保留（2字节）
            header[0x1A] = 0x00;
            header[0x1B] = 0x00;
            
            return header;
        }

        /// <summary>
        /// 构建简单导航命令（仅指定目标点）
        /// </summary>
        private byte[] BuildSimpleNavigationCommand(uint orderId, uint taskKey, uint targetPointId)
        {
            const byte COMMAND_CODE = 0xAE; // 混合导航任务命令码
            const int DATA_LENGTH = 12;     // 数据区长度：订单ID(4) + 任务KEY(4) + 目标点ID(4)
            
            // 构建报文头
            byte[] header = BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
            byte[] packet = new byte[header.Length + DATA_LENGTH];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            
            // 填充数据区
            int offset = header.Length;
            
            // 订单ID
            packet[offset++] = (byte)(orderId & 0xFF);
            packet[offset++] = (byte)((orderId >> 8) & 0xFF);
            packet[offset++] = (byte)((orderId >> 16) & 0xFF);
            packet[offset++] = (byte)((orderId >> 24) & 0xFF);
            
            // 任务KEY
            packet[offset++] = (byte)(taskKey & 0xFF);
            packet[offset++] = (byte)((taskKey >> 8) & 0xFF);
            packet[offset++] = (byte)((taskKey >> 16) & 0xFF);
            packet[offset++] = (byte)((taskKey >> 24) & 0xFF);
            
            // 目标点ID
            packet[offset++] = (byte)(targetPointId & 0xFF);
            packet[offset++] = (byte)((targetPointId >> 8) & 0xFF);
            packet[offset++] = (byte)((targetPointId >> 16) & 0xFF);
            packet[offset++] = (byte)((targetPointId >> 24) & 0xFF);
            
            return packet;
        }
        
        /// <summary>
        /// 构建完整导航命令（指定路径点和路径段）
        /// </summary>
        private byte[] BuildFullNavigationCommand(uint orderId, uint taskKey, List<uint> pointIds, List<uint> pathIds)
        {
            const byte COMMAND_CODE = 0xAE; // 混合导航任务命令码
            
            // 计算数据区长度：订单ID(4) + 任务KEY(4) + 点数量(2) + 路径段数量(2) + 点ID列表(4*n) + 路径段ID列表(4*m)
            int dataLength = 12 + (pointIds.Count * 4) + (pathIds.Count * 4);
            
            // 构建报文头
            byte[] header = BuildPacketHeader(COMMAND_CODE, (ushort)dataLength);
            byte[] packet = new byte[header.Length + dataLength];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            
            int offset = header.Length;
            
            // 订单ID
            packet[offset++] = (byte)(orderId & 0xFF);
            packet[offset++] = (byte)((orderId >> 8) & 0xFF);
            packet[offset++] = (byte)((orderId >> 16) & 0xFF);
            packet[offset++] = (byte)((orderId >> 24) & 0xFF);
            
            // 任务KEY
            packet[offset++] = (byte)(taskKey & 0xFF);
            packet[offset++] = (byte)((taskKey >> 8) & 0xFF);
            packet[offset++] = (byte)((taskKey >> 16) & 0xFF);
            packet[offset++] = (byte)((taskKey >> 24) & 0xFF);
            
            // 点数量
            ushort pointCount = (ushort)pointIds.Count;
            packet[offset++] = (byte)(pointCount & 0xFF);
            packet[offset++] = (byte)(pointCount >> 8);
            
            // 路径段数量
            ushort pathCount = (ushort)pathIds.Count;
            packet[offset++] = (byte)(pathCount & 0xFF);
            packet[offset++] = (byte)(pathCount >> 8);
            
            // 点ID列表
            foreach (uint pointId in pointIds)
            {
                packet[offset++] = (byte)(pointId & 0xFF);
                packet[offset++] = (byte)((pointId >> 8) & 0xFF);
                packet[offset++] = (byte)((pointId >> 16) & 0xFF);
                packet[offset++] = (byte)((pointId >> 24) & 0xFF);
            }
            
            // 路径段ID列表
            foreach (uint pathId in pathIds)
            {
                packet[offset++] = (byte)(pathId & 0xFF);
                packet[offset++] = (byte)((pathId >> 8) & 0xFF);
                packet[offset++] = (byte)((pathId >> 16) & 0xFF);
                packet[offset++] = (byte)((pathId >> 24) & 0xFF);
            }
            
            return packet;
        }

        #endregion

        #region UDP通信
        
        /// <summary>
        /// 发送UDP报文
        /// </summary>
        private async Task<bool> SendPacketAsync(byte[] packet)
        {
            try
            {
                if (_udpClient == null)
                {
                    throw new InvalidOperationException("UDP客户端未初始化");
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

        #region AGV移动控制

        /// <summary>
        /// 简单导航命令（仅指定目标点）
        /// </summary>
        public async Task<bool> NavigateToPointAsync(uint orderId, uint taskKey, uint targetPointId)
        {
            try
            {
                byte[] packet = BuildSimpleNavigationCommand(orderId, taskKey, targetPointId);
                bool success = await SendPacketAsync(packet);
                
                if (success)
                {
                    Console.WriteLine($"[AGVService] 发送导航命令成功: 订单={orderId}, 任务={taskKey}, 目标点={targetPointId}");
                    
                    // 发布导航命令事件
                    _eventHub.Publish("AGVNavigationStarted", new 
                    { 
                        OrderId = orderId,
                        TaskKey = taskKey,
                        TargetPoint = targetPointId
                    });
                }
                else
                {
                    Console.WriteLine($"[AGVService] 发送导航命令失败");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 执行导航命令时发生错误: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 完整导航命令（指定路径点和路径段）
        /// </summary>
        public async Task<bool> NavigateWithPathAsync(uint orderId, uint taskKey, List<uint> pointIds, List<uint> pathIds)
        {
            try
            {
                if (pointIds == null || pointIds.Count == 0)
                {
                    throw new ArgumentException("必须指定至少一个路径点");
                }
                
                byte[] packet = BuildFullNavigationCommand(orderId, taskKey, pointIds, pathIds);
                bool success = await SendPacketAsync(packet);
                
                if (success)
                {
                    Console.WriteLine($"[AGVService] 发送导航命令成功: 订单={orderId}, 任务={taskKey}, 点数量={pointIds.Count}");
                    
                    // 发布导航命令事件
                    _eventHub.Publish("AGVNavigationStarted", new 
                    { 
                        OrderId = orderId,
                        TaskKey = taskKey,
                        PointCount = pointIds.Count,
                        PathCount = pathIds?.Count ?? 0
                    });
                }
                else
                {
                    Console.WriteLine($"[AGVService] 发送导航命令失败");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 执行导航命令时发生错误: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region AGV状态查询

        /// <summary>
        /// 构建状态查询命令
        /// </summary>
        private byte[] BuildStatusQueryCommand()
        {
            const byte COMMAND_CODE = 0xAF; // 状态查询命令码
            const ushort DATA_LENGTH = 0;    // 数据区长度为0
            
            // 构建报文头
            return BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
        }

        /// <summary>
        /// 查询AGV状态
        /// </summary>
        private async Task<AgvStatus> QueryStatusAsync()
        {
            try
            {
                // 构建并发送查询报文
                byte[] queryPacket = BuildStatusQueryCommand();
                bool sendSuccess = await SendPacketAsync(queryPacket);
                
                if (!sendSuccess)
                {
                    Console.WriteLine("[AGVService] 发送状态查询报文失败");
                    return null;
                }
                
                // 接收应答数据
                UdpReceiveResult result = await _udpClient.ReceiveAsync();
                if (result.Buffer.Length < 28) // 至少包含报文头
                {
                    Console.WriteLine("[AGVService] 接收到的状态应答数据长度不足");
                    return null;
                }
                
                // 解析应答数据
                return ParseStatusResponse(result.Buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 查询AGV状态时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析状态应答数据
        /// </summary>
        private AgvStatus ParseStatusResponse(byte[] data)
        {
            try
            {
                int offset = 28; // 跳过报文头
                
                AgvStatus status = new AgvStatus();
                
                // 解析位置信息
                status.X = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.Y = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.Heading = BitConverter.ToSingle(data, offset);
                offset += 4;
                
                // 跳过RunningStatusInfo前面的字段，直接读取速度信息
                offset += 0x14;
                status.Vx = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.Vy = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.Omega = BitConverter.ToSingle(data, offset);
                offset += 4;
                
                // 跳过TaskStatusInfo，读取电池信息
                offset += 0x20;
                status.StateOfCharge = BitConverter.ToSingle(data, offset) * 100;
                offset += 4;
                status.Voltage = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.Current = BitConverter.ToSingle(data, offset);
                offset += 4;
                status.IsCharging = data[offset] != 0;
                
                return status;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 解析状态应答数据时发生错误: {ex.Message}");
                return null;
            }
        }

        #endregion

        /// <summary>
        /// 发送AGV指令
        /// 将高级指令转换为具体的导航命令
        /// </summary>
        /// <param name="agvId">AGV标识符</param>
        /// <param name="command">指令类型，如"GoToSwitchPoint"、"GoToWaitPoint"等</param>
        /// <param name="parameters">可选参数</param>
        /// <returns>是否成功发送指令</returns>
        /// parameters = null 表示没有参数
        public async System.Threading.Tasks.Task<bool> SendCommandToAGV(string agvId, string command, Dictionary<string, object> parameters )
        {
            try
            {
                if (string.IsNullOrEmpty(agvId) || string.IsNullOrEmpty(command))
                {
                    throw new ArgumentException("AGV ID和指令类型不能为空");
                }
                
                // 生成唯一的订单ID和任务ID agv用的别弄错了
                uint orderId = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                uint taskKey = (uint)new Random().Next(1000, 9999);
                
                // 根据指令类型分发到相应的导航命令
                switch (command.ToUpper())
                {
                    case "GOTOSWITCHPOINT":
                        // 导航到切换点
                        uint switchPointId = parameters != null && parameters.ContainsKey("SwitchPointId") 
                            ? Convert.ToUInt32(parameters["SwitchPointId"]) 
                            : 1001; // 默认使用ID 1001作为切换点
                        
                        Console.WriteLine($"[AGVService] 指令AGV前往切换点: ID={switchPointId}");
                        return await NavigateToPointAsync(orderId, taskKey, switchPointId);
                        
                    case "GOTOWAITPOINT":
                        // 导航到待命点
                        uint waitPointId = parameters != null && parameters.ContainsKey("WaitPointId") 
                            ? Convert.ToUInt32(parameters["WaitPointId"]) 
                            : 1002; // 默认使用ID 1002作为待命点
                        
                        Console.WriteLine($"[AGVService] 指令AGV返回待命点: ID={waitPointId}");
                        return await NavigateToPointAsync(orderId, taskKey, waitPointId);
                        
                    // 其他命令类型...
                        
                    default:
                        throw new ArgumentException($"不支持的指令类型: {command}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 发送AGV指令失败: {ex.Message}");
                return false;
            }
        }
    }
} 