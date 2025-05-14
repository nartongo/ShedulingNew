using System;
using System.Threading;
using System.Threading.Tasks;
using ShedulingNew.BusinessLogic;
using EasyModbus;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// PLC通信服务类 - 基于EasyModbus实现PLC通信功能
    /// </summary>
    public class PLCService
    {
        private EventHub _eventHub;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private bool _isConnected;
        private string _plcIpAddress = "192.168.0.1"; // 默认PLC IP地址
        private int _plcPort = 502; // 默认PLC端口(ModbusTCP默认端口)
        private ModbusClient _modbusClient;
        
        // PLC地址定义和约束
        private const int MAX_M_ADDRESS = 7999; // M地址最大值
        private const int MAX_D_ADDRESS = 7999; // D地址最大值
        private const byte DEFAULT_SLAVE_ID = 17; // 默认从站ID
        
        // 常用PLC地址名称定义
        public static class PLCAddresses
        {
            // 线圈地址
            public const string SWITCH_POINT_ARRIVED = "M500"; // 告知PLC到达切换点信号
            public const string TRIGGER_ROLLERS = "M501";      // 左右皮辊信号
            public const string SPINDLE_ARRIVAL = "M600";      // PLC到达锭位信号
            public const string REPAIR_DONE = "M601";          // 接头完成信号
            public const string BACK_TO_SWITCH_POINT = "M602"; // PLC告知到达权限切换点
            
            // 寄存器地址
            public const string SPINDLE_POSITION = "D500";     // 锭子距离寄存器地址
        }
        
        public PLCService()
        {
            _eventHub = EventHub.Instance;
            _modbusClient = new ModbusClient();
            _modbusClient.UnitIdentifier = DEFAULT_SLAVE_ID; // 设置从站ID
            _modbusClient.ConnectionTimeout = 3000;          // 设置连接超时(3秒)
        }

        /// <summary>
        /// 设置PLC连接参数
        /// </summary>
        /// <param name="ipAddress">PLC IP地址</param>
        /// <param name="port">PLC端口</param>
        /// <param name="slaveId">从站ID，默认为17</param>
        public void Configure(string ipAddress, int port = 502, byte slaveId = DEFAULT_SLAVE_ID)
        {
            _plcIpAddress = ipAddress;
            _plcPort = port;
            
            // 设置ModbusClient参数
            _modbusClient.IPAddress = ipAddress;
            _modbusClient.Port = port;
            _modbusClient.UnitIdentifier = slaveId;
            
            Console.WriteLine($"[PLCService] 已配置PLC连接参数: IP={ipAddress}, Port={port}, SlaveID={slaveId}");
        }
        
        /// <summary>
        /// 启动PLC通信服务
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            try
            {
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // 启动PLC通信线程
                System.Threading.Tasks.Task.Run(() => RunPLCCommunication(_cancellationTokenSource.Token));
                
                Console.WriteLine("[PLCService] PLC通信服务启动成功");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Console.WriteLine($"[PLCService] PLC通信服务启动失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 停止PLC通信服务
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();
                
                // 断开Modbus连接
                if (_modbusClient.Connected)
                {
                    _modbusClient.Disconnect();
                }
                
                Console.WriteLine("[PLCService] PLC通信服务停止成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] PLC通信服务停止失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 运行PLC通信循环
        /// </summary>
        private async System.Threading.Tasks.Task RunPLCCommunication(CancellationToken cancellationToken)
        {
            int reconnectAttempts = 0;
            const int maxReconnectAttempts = 5;
            const int reconnectDelay = 5000; // 重连延迟5秒
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 确保连接
                    if (!_isConnected)
                    {
                        _isConnected = await ConnectToPLC();
                        if (!_isConnected)
                        {
                            reconnectAttempts++;
                            if (reconnectAttempts > maxReconnectAttempts)
                            {
                                Console.WriteLine($"[PLCService] 连接尝试次数过多，暂停重连 ({reconnectAttempts})");
                                await System.Threading.Tasks.Task.Delay(reconnectDelay * 2, cancellationToken); // 加倍延迟
                                reconnectAttempts = 0; // 重置尝试次数
                            }
                            else
                            {
                                await System.Threading.Tasks.Task.Delay(reconnectDelay, cancellationToken);
                            }
                            continue;
                        }
                        reconnectAttempts = 0; // 连接成功后重置尝试次数
                    }
                    
                    // 周期性读取PLC状态数据
                    await System.Threading.Tasks.Task.Delay(500, cancellationToken);
                    
                    try
                    {
                        // 读取关键状态位
                        bool spindleArrived = await ReadCoilAsync(PLCAddresses.SPINDLE_ARRIVAL);
                        bool repairDone = await ReadCoilAsync(PLCAddresses.REPAIR_DONE);
                        bool backToSwitchPoint = await ReadCoilAsync(PLCAddresses.BACK_TO_SWITCH_POINT);
                        
                        // 构造并发布PLC状态数据
                        var statusData = new
                        {
                            Timestamp = DateTime.Now,
                            SpindleArrived = spindleArrived,
                            RepairDone = repairDone,
                            BackToSwitchPoint = backToSwitchPoint
                        };
                        
                        // 根据状态触发相应事件
                        if (spindleArrived)
                        {
                            _eventHub.Publish("PLCDataChanged", "ArrivedSpindle");
                        }
                        
                        if (repairDone)
                        {
                            _eventHub.Publish("PLCDataChanged", "RepairDone");
                        }
                        
                        if (backToSwitchPoint)
                        {
                            _eventHub.Publish("PLCDataChanged", "BackToSwitchPoint");
                        }
                        
                        // 发布PLC数据变化事件
                        _eventHub.Publish("PLCDataChanged", statusData);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PLCService] 读取PLC状态数据失败: {ex.Message}");
                        // 读取失败但不断开连接，避免频繁重连
                    }
                }
                catch (OperationCanceledException)
                {
                    // 操作被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    _isConnected = false; // 发生错误，标记为断开连接
                    Console.WriteLine($"[PLCService] PLC通信过程中发生错误: {ex.Message}");
                    await System.Threading.Tasks.Task.Delay(5000, cancellationToken); // 延迟后重试
                }
            }
            
            // 断开PLC连接
            if (_isConnected)
            {
                DisconnectFromPLC();
            }
            
            Console.WriteLine("[PLCService] PLC通信循环结束");
        }

        /// <summary>
        /// 连接到PLC
        /// </summary>
        private async System.Threading.Tasks.Task<bool> ConnectToPLC()
        {
            try
            {
                // 断开已有连接
                if (_modbusClient.Connected)
                {
                    _modbusClient.Disconnect();
                }
                
                Console.WriteLine($"[PLCService] 正在连接到PLC: {_plcIpAddress}:{_plcPort}, SlaveID={_modbusClient.UnitIdentifier}");
                
                // 异步包装连接操作
                await System.Threading.Tasks.Task.Run(() => _modbusClient.Connect());
                
                if (_modbusClient.Connected)
                {
                    Console.WriteLine("[PLCService] PLC连接成功");
                    
                    // 发布连接状态变更事件
                    _eventHub.Publish("PLCConnectionChanged", new { Status = "Connected", Address = _plcIpAddress });
                    
                    return true;
                }
                else
                {
                    Console.WriteLine("[PLCService] PLC连接失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] PLC连接失败: {ex.Message}");
                
                // 发布连接状态变更事件
                _eventHub.Publish("PLCConnectionChanged", new { Status = "Disconnected", Error = ex.Message });
                
                return false;
            }
        }
        
        /// <summary>
        /// 断开PLC连接
        /// </summary>
        private void DisconnectFromPLC()
        {
            try
            {
                if (_modbusClient.Connected)
                {
                    _modbusClient.Disconnect();
                    Console.WriteLine("[PLCService] 断开PLC连接");
                }
                
                // 发布连接状态变更事件
                _eventHub.Publish("PLCConnectionChanged", new { Status = "Disconnected" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 断开PLC连接失败: {ex.Message}");
            }
        }

        #region PLC线圈操作

        /// <summary>
        /// 将PLC地址转换为Modbus地址
        /// </summary>
        private int ConvertToModbusCoilAddress(string address)
        {
            // 格式示例: "M500" -> 500
            if (address.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(address.Substring(1), out int result))
                {
                    // 验证地址范围
                    if (result < 0 || result > MAX_M_ADDRESS)
                    {
                        throw new ArgumentException($"M地址超出范围(0-{MAX_M_ADDRESS}): {address}");
                    }
                    return result;
                }
            }
            
            throw new ArgumentException($"无效的线圈地址格式: {address}，应为'Mxxx'格式");
        }
        
        /// <summary>
        /// 将PLC地址转换为Modbus寄存器地址
        /// </summary>
        private int ConvertToModbusRegisterAddress(string address)
        {
            // 格式示例: "D500" -> 500
            if (address.StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(address.Substring(1), out int result))
                {
                    // 验证地址范围
                    if (result < 0 || result > MAX_D_ADDRESS)
                    {
                        throw new ArgumentException($"D地址超出范围(0-{MAX_D_ADDRESS}): {address}");
                    }
                    return result;
                }
            }
            
            throw new ArgumentException($"无效的寄存器地址格式: {address}，应为'Dxxx'格式");
        }

        /// <summary>
        /// 读取PLC线圈状态
        /// </summary>
        /// <param name="address">线圈地址，如"M500"</param>
        /// <returns>线圈状态 (true=ON, false=OFF)</returns>
        public bool ReadCoil(string address)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusCoilAddress(address);
                Console.WriteLine($"[PLCService] 读取PLC线圈: {address} (Modbus地址: {modbusAddress})");
                
                // 读取单个线圈
                bool[] result = _modbusClient.ReadCoils(modbusAddress, 1);
                
                if (result != null && result.Length > 0)
                {
                    Console.WriteLine($"[PLCService] 线圈{address}状态: {(result[0] ? "ON" : "OFF")}");
                    return result[0];
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 读取PLC线圈失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步读取PLC线圈状态
        /// </summary>
        /// <param name="address">线圈地址</param>
        public async System.Threading.Tasks.Task<bool> ReadCoilAsync(string address)
        {
            // 异步包装同步方法
            return await System.Threading.Tasks.Task.Run(() => ReadCoil(address));
        }
        
        /// <summary>
        /// 读取多个PLC线圈状态
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="count">读取数量</param>
        /// <returns>线圈状态数组</returns>
        public bool[] ReadCoils(string startAddress, int count)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusCoilAddress(startAddress);
                Console.WriteLine($"[PLCService] 读取多个PLC线圈: 从{startAddress}开始读取{count}个");
                
                // 读取多个线圈
                bool[] results = _modbusClient.ReadCoils(modbusAddress, count);
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 读取多个PLC线圈失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步读取多个PLC线圈状态
        /// </summary>
        public async System.Threading.Tasks.Task<bool[]> ReadCoilsAsync(string startAddress, int count)
        {
            return await System.Threading.Tasks.Task.Run(() => ReadCoils(startAddress, count));
        }
        
        /// <summary>
        /// 写入PLC线圈状态
        /// </summary>
        /// <param name="address">线圈地址</param>
        /// <param name="value">写入值 (true=ON, false=OFF)</param>
        /// <returns>是否写入成功</returns>
        public bool WriteCoil(string address, bool value)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusCoilAddress(address);
                Console.WriteLine($"[PLCService] 写入PLC线圈: {address} = {(value ? "ON" : "OFF")} (Modbus地址: {modbusAddress})");
                
                // 写入单个线圈
                _modbusClient.WriteSingleCoil(modbusAddress, value);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 写入PLC线圈失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步写入PLC线圈状态
        /// </summary>
        public async System.Threading.Tasks.Task<bool> WriteCoilAsync(string address, bool value)
        {
            return await System.Threading.Tasks.Task.Run(() => WriteCoil(address, value));
        }
        
        /// <summary>
        /// 写入多个PLC线圈状态
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="values">写入值数组</param>
        /// <returns>是否写入成功</returns>
        public bool WriteCoils(string startAddress, bool[] values)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusCoilAddress(startAddress);
                Console.WriteLine($"[PLCService] 写入多个PLC线圈: 从{startAddress}开始写入{values.Length}个值");
                
                // 写入多个线圈
                _modbusClient.WriteMultipleCoils(modbusAddress, values);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 写入多个PLC线圈失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步写入多个PLC线圈状态
        /// </summary>
        public async System.Threading.Tasks.Task<bool> WriteCoilsAsync(string startAddress, bool[] values)
        {
            return await System.Threading.Tasks.Task.Run(() => WriteCoils(startAddress, values));
        }

        #endregion

        #region PLC寄存器操作
        
        /// <summary>
        /// 读取PLC寄存器值
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <returns>寄存器值</returns>
        public short ReadRegister(string address)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusRegisterAddress(address);
                Console.WriteLine($"[PLCService] 读取PLC寄存器: {address} (Modbus地址: {modbusAddress})");
                
                // 读取单个寄存器
                int[] result = _modbusClient.ReadHoldingRegisters(modbusAddress, 1);
                
                if (result != null && result.Length > 0)
                {
                    short value = (short)result[0];
                    Console.WriteLine($"[PLCService] 寄存器{address}值: {value}");
                    return value;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 读取PLC寄存器失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步读取PLC寄存器值
        /// </summary>
        public async System.Threading.Tasks.Task<short> ReadRegisterAsync(string address)
        {
            return await System.Threading.Tasks.Task.Run(() => ReadRegister(address));
        }
        
        /// <summary>
        /// 读取多个PLC寄存器值
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="count">读取数量</param>
        /// <returns>寄存器值数组</returns>
        public short[] ReadRegisters(string startAddress, int count)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusRegisterAddress(startAddress);
                Console.WriteLine($"[PLCService] 读取多个PLC寄存器: 从{startAddress}开始读取{count}个");
                
                // 读取多个寄存器
                int[] results = _modbusClient.ReadHoldingRegisters(modbusAddress, count);
                
                // 转换为short[]
                short[] shortResults = new short[results.Length];
                for (int i = 0; i < results.Length; i++)
                {
                    shortResults[i] = (short)results[i];
                }
                
                return shortResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 读取多个PLC寄存器失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步读取多个PLC寄存器值
        /// </summary>
        public async System.Threading.Tasks.Task<short[]> ReadRegistersAsync(string startAddress, int count)
        {
            return await System.Threading.Tasks.Task.Run(() => ReadRegisters(startAddress, count));
        }
        
        /// <summary>
        /// 写入PLC寄存器值
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="value">写入值</param>
        /// <returns>是否写入成功</returns>
        public bool WriteRegister(string address, short value)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusRegisterAddress(address);
                Console.WriteLine($"[PLCService] 写入PLC寄存器: {address} = {value} (Modbus地址: {modbusAddress})");
                
                // 写入单个寄存器
                _modbusClient.WriteSingleRegister(modbusAddress, value);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 写入PLC寄存器失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步写入PLC寄存器值
        /// </summary>
        public async System.Threading.Tasks.Task<bool> WriteRegisterAsync(string address, short value)
        {
            return await System.Threading.Tasks.Task.Run(() => WriteRegister(address, value));
        }
        
        /// <summary>
        /// 写入多个PLC寄存器值
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="values">写入值数组</param>
        /// <returns>是否写入成功</returns>
        public bool WriteRegisters(string startAddress, short[] values)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("PLC未连接");
            }
            
            try
            {
                int modbusAddress = ConvertToModbusRegisterAddress(startAddress);
                Console.WriteLine($"[PLCService] 写入多个PLC寄存器: 从{startAddress}开始写入{values.Length}个值");
                
                // 转换为int[]
                int[] intValues = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    intValues[i] = values[i];
                }
                
                // 写入多个寄存器
                _modbusClient.WriteMultipleRegisters(modbusAddress, intValues);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 写入多个PLC寄存器失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 异步写入多个PLC寄存器值
        /// </summary>
        public async System.Threading.Tasks.Task<bool> WriteRegistersAsync(string startAddress, short[] values)
        {
            return await System.Threading.Tasks.Task.Run(() => WriteRegisters(startAddress, values));
        }
        
        #endregion

        #region 业务操作方法
        
        /// <summary>
        /// 告知PLC AGV已到达切换点
        /// </summary>
        public async System.Threading.Tasks.Task<bool> NotifySwitchPointArrivalAsync(bool arrived)
        {
            return await WriteCoilAsync(PLCAddresses.SWITCH_POINT_ARRIVED, arrived);
        }
        
        /// <summary>
        /// 发送锭子距离到PLC
        /// </summary>
        public async System.Threading.Tasks.Task<bool> SendSpindlePositionAsync(short position)
        {
            return await WriteRegisterAsync(PLCAddresses.SPINDLE_POSITION, position);
        }
        
        /// <summary>
        /// 控制皮辊启停
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ControlRollersAsync(bool enable)
        {
            return await WriteCoilAsync(PLCAddresses.TRIGGER_ROLLERS, enable);
        }
        
        /// <summary>
        /// 检查PLC是否已到达锭位
        /// </summary>
        public async System.Threading.Tasks.Task<bool> CheckSpindleArrivalAsync()
        {
            return await ReadCoilAsync(PLCAddresses.SPINDLE_ARRIVAL);
        }
        
        /// <summary>
        /// 检查修复是否完成
        /// </summary>
        public async System.Threading.Tasks.Task<bool> CheckRepairDoneAsync()
        {
            return await ReadCoilAsync(PLCAddresses.REPAIR_DONE);
        }
        
        /// <summary>
        /// 检查PLC是否已返回切换点
        /// </summary>
        public async System.Threading.Tasks.Task<bool> CheckBackToSwitchPointAsync()
        {
            return await ReadCoilAsync(PLCAddresses.BACK_TO_SWITCH_POINT);
        }
        
        #endregion
    }
} 