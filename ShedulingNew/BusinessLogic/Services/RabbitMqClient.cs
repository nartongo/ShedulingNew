using System;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ShedulingNew.BusinessLogic.Models;

namespace ShedulingNew.BusinessLogic.Services
{
    /// <summary>
    /// RabbitMQ客户端
    /// 负责与RabbitMQ服务器建立连接并处理消息的发送和接收
    /// 实现异步消息通信机制
    /// </summary>
    public class RabbitMqClient : IDisposable
    {
        private IConnection _connection;
        private IModel _channel;
        private readonly BackendConfig _config;
        private readonly string _machineId;
        private bool _isConnected = false;
        private readonly EventHub _eventHub;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="config">后端配置信息，包含RabbitMQ连接参数</param>
        /// <param name="machineId">机器ID，用于构建路由键</param>
        public RabbitMqClient(BackendConfig config, string machineId)
        {
            _config = config;
            _machineId = machineId;
            _eventHub = EventHub.Instance;
        }

        /// <summary>
        /// 连接到RabbitMQ服务器
        /// 创建连接和通道，声明常用的交换器
        /// </summary>
        /// <returns>连接是否成功</returns>
        public bool Connect()
        {
            try
            {
                // 创建连接工厂
                var factory = new ConnectionFactory
                {
                    HostName = _config.RabbitMqHost,
                    Port = _config.RabbitMqPort,
                    UserName = _config.RabbitMqUser,
                    Password = _config.RabbitMqPwd,
                    VirtualHost = _config.RabbitMqVHost,
                    AutomaticRecoveryEnabled = true, // 自动重连
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10) // 重连间隔
                };

                // 创建连接和通道
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // 声明常用的交换器
                _channel.ExchangeDeclare("backend.start", ExchangeType.Topic, true);
                _channel.ExchangeDeclare("backend.ack", ExchangeType.Topic, true);
                _channel.ExchangeDeclare($"machine.{_machineId}.status", ExchangeType.Topic, true);
                _channel.ExchangeDeclare($"machine.{_machineId}.task", ExchangeType.Topic, true);

                _isConnected = true;
                Console.WriteLine($"已成功连接到RabbitMQ服务器: {_config.RabbitMqHost}:{_config.RabbitMqPort}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接到RabbitMQ服务器失败: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 发布消息到指定的交换器和路由键
        /// </summary>
        /// <param name="exchange">交换器名称</param>
        /// <param name="routingKey">路由键</param>
        /// <param name="message">要发送的消息对象，将被序列化为JSON</param>
        /// <returns>是否发送成功</returns>
        public bool Publish(string exchange, string routingKey, object message)
        {
            if (!_isConnected)
            {
                Console.WriteLine("RabbitMQ客户端未连接，无法发送消息");
                return false;
            }

            try
            {
                // 序列化消息为JSON
                string jsonMessage = JsonConvert.SerializeObject(message);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                // 发布消息
                _channel.BasicPublish(
                    exchange: exchange,
                    routingKey: routingKey,
                    basicProperties: null,
                    body: body);

                Console.WriteLine($"已发送消息到: {exchange} - {routingKey}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送消息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 订阅指定队列的消息
        /// </summary>
        /// <param name="queueName">队列名称</param>
        /// <param name="exchange">交换器名称</param>
        /// <param name="routingKey">路由键</param>
        /// <param name="handler">消息处理回调函数</param>
        public void Subscribe(string queueName, string exchange, string routingKey, Action<string> handler)
        {
            if (!_isConnected)
            {
                Console.WriteLine("RabbitMQ客户端未连接，无法订阅消息");
                return;
            }

            try
            {
                // 确保队列存在
                _channel.QueueDeclare(queueName, true, false, false, null);
                
                // 将队列绑定到交换器的指定路由键
                _channel.QueueBind(queueName, exchange, routingKey);

                // 创建消费者
                var consumer = new EventingBasicConsumer(_channel);
                
                // 设置消息接收事件
                consumer.Received += (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    
                    try
                    {
                        // 调用处理函数
                        handler(message);
                        
                        // 确认消息已处理
                        _channel.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理订阅消息出错: {ex.Message}");
                        // 拒绝消息并重新入队
                        _channel.BasicNack(ea.DeliveryTag, false, true);
                    }
                };

                // 开始消费
                _channel.BasicConsume(queueName, false, consumer);
                Console.WriteLine($"已订阅队列: {queueName}, 交换器: {exchange}, 路由键: {routingKey}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"订阅消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送启动握手请求
        /// </summary>
        /// <returns>是否发送成功</returns>
        public bool SendStartRequest()
        {
            var startRequest = new
            {
                type = "StartRequest",
                machineId = _machineId,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            return Publish("backend.start", $"start.{_machineId}", startRequest);
        }

        /// <summary>
        /// 发送状态报告
        /// </summary>
        /// <param name="status">机器状态对象</param>
        /// <returns>是否发送成功</returns>
        public bool SendStatusReport(object status)
        {
            var statusReport = new
            {
                type = "StatusReport",
                machineId = _machineId,
                timestamp = DateTime.UtcNow.ToString("o"),
                status = status
            };

            return Publish($"machine.{_machineId}.status", "status", statusReport);
        }

        /// <summary>
        /// 发送任务确认
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="status">任务状态</param>
        /// <returns>是否发送成功</returns>
        public bool SendTaskAck(string taskId, string status)
        {
            var taskAck = new
            {
                type = "TaskAck",
                machineId = _machineId,
                taskId = taskId,
                status = status,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            return Publish("backend.ack", $"ack.{_machineId}", taskAck);
        }

        /// <summary>
        /// 设置启动确认处理函数
        /// </summary>
        public void SetupStartAckHandler()
        {
            string queueName = $"ack.{_machineId}.queue";
            Subscribe(queueName, "backend.ack", $"ack.{_machineId}", message =>
            {
                try
                {
                    dynamic ack = JsonConvert.DeserializeObject(message);
                    if (ack != null && ack.type == "StartAck")
                    {
                        Console.WriteLine($"收到启动确认: {message}");
                        _eventHub.Publish("BackendStartAck", ack);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理启动确认出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 设置状态确认处理函数
        /// </summary>
        public void SetupStatusAckHandler()
        {
            string queueName = $"status.ack.{_machineId}.queue";
            Subscribe(queueName, "backend.ack", $"status.ack.{_machineId}", message =>
            {
                try
                {
                    dynamic ack = JsonConvert.DeserializeObject(message);
                    if (ack != null && ack.type == "StatusAck")
                    {
                        Console.WriteLine($"收到状态确认: {message}");
                        _eventHub.Publish("BackendStatusAck", ack);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理状态确认出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 设置任务分配处理函数

        /// jin:  接受后端收到的任务信息，先解析后，再发布到EventHub
        /// </summary>
        public void SetupTaskAssignmentHandler()
        {
            string queueName = $"task.{_machineId}.queue";
            Subscribe(queueName, $"machine.{_machineId}.task", "task", message =>
            {
                try
                {
                    // jin: 解析json消息为动态对象
                    dynamic taskAssignment = JsonConvert.DeserializeObject(message);
                    if (taskAssignment != null && taskAssignment.type == "TaskAssignment")
                    {
                        Console.WriteLine($"收到任务分配: {message}");
                        
                        // 创建包含正确Type属性的事件数据
                        var eventData = new 
                        {
                            Type = "StartRepairTask",  // jin: 修改类型为TaskController期望的类型  因为此类期望的是StartRepairTask类型
                            SideNumber = taskAssignment.spinningMachineId, //jin：后台传过来的边号
                            TaskId = taskAssignment.taskId,
                            OriginalMessage = taskAssignment
                        };
                        
                        // 发布事件到EventHub
                        _eventHub.Publish("BackendCommandReceived", eventData);  //jin: 发布事件到EventHub, TaskController订阅了此事件.
                        
                        // 发送任务确认
                        SendTaskAck(taskAssignment.taskId.ToString(), "Received");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理任务分配出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            _channel?.Dispose();
            _connection?.Dispose();
            Console.WriteLine("RabbitMQ客户端资源已释放");
        }
    }
} 