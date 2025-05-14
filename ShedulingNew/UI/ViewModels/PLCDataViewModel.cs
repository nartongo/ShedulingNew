using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using ShedulingNew.BusinessLogic;
using ShedulingNew.BusinessLogic.Services;

namespace ShedulingNew.UI.ViewModels
{
    /// <summary>
    /// PLC数据视图模型
    /// </summary>
    public class PLCDataViewModel : INotifyPropertyChanged
    {
        private EventHub _eventHub;
        private PLCService _plcService;
        private string _plcStatus = "未连接";
        private DateTime _lastUpdateTime = DateTime.Now;
        private ObservableCollection<PLCDataItem> _plcDataItems = new ObservableCollection<PLCDataItem>();
        private ObservableCollection<PLCCoilItem> _plcCoilItems = new ObservableCollection<PLCCoilItem>();
        
        // 实现INotifyPropertyChanged接口
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// PLC状态
        /// </summary>
        public string PLCStatus
        {
            get => _plcStatus;
            set
            {
                if (_plcStatus != value)
                {
                    _plcStatus = value;
                    OnPropertyChanged(nameof(PLCStatus));
                }
            }
        }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime
        {
            get => _lastUpdateTime;
            set
            {
                if (_lastUpdateTime != value)
                {
                    _lastUpdateTime = value;
                    OnPropertyChanged(nameof(LastUpdateTime));
                }
            }
        }
        
        /// <summary>
        /// PLC数据项集合
        /// </summary>
        public ObservableCollection<PLCDataItem> PLCDataItems
        {
            get => _plcDataItems;
            set
            {
                _plcDataItems = value;
                OnPropertyChanged(nameof(PLCDataItems));
            }
        }

        /// <summary>
        /// PLC线圈状态集合
        /// </summary>
        public ObservableCollection<PLCCoilItem> PLCCoilItems
        {
            get => _plcCoilItems;
            set
            {
                _plcCoilItems = value;
                OnPropertyChanged(nameof(PLCCoilItems));
            }
        }
        
        public PLCDataViewModel()
        {
            _eventHub = EventHub.Instance;
            _plcService = new PLCService();
            
            // 添加一些演示数据
            _plcDataItems.Add(new PLCDataItem { Name = "生产线速度", Value = "120", Unit = "件/分钟", Address = "D100" });
            _plcDataItems.Add(new PLCDataItem { Name = "温度", Value = "24.5", Unit = "°C", Address = "D101" });
            _plcDataItems.Add(new PLCDataItem { Name = "压力", Value = "2.4", Unit = "MPa", Address = "D102" });
            _plcDataItems.Add(new PLCDataItem { Name = "电机1转速", Value = "1200", Unit = "rpm", Address = "D103" });
            _plcDataItems.Add(new PLCDataItem { Name = "电机2转速", Value = "850", Unit = "rpm", Address = "D104" });
            _plcDataItems.Add(new PLCDataItem { Name = "料仓1液位", Value = "78", Unit = "%", Address = "D105" });
            
            // 添加线圈状态项
            _plcCoilItems.Add(new PLCCoilItem { Name = "AGV到达切换点", Address = "M500", Status = false });
            _plcCoilItems.Add(new PLCCoilItem { Name = "启动皮辊", Address = "M501", Status = false });
            _plcCoilItems.Add(new PLCCoilItem { Name = "PLC到达锭位", Address = "M600", Status = false });
            _plcCoilItems.Add(new PLCCoilItem { Name = "修复完成", Address = "M601", Status = false });
            _plcCoilItems.Add(new PLCCoilItem { Name = "PLC到达切换点", Address = "M602", Status = false });
            
            // 订阅事件
            SubscribeToEvents();
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventHub.Subscribe("PLCDataChanged", async data =>
            {
                // 更新PLC状态信息
                PLCStatus = "已连接";
                LastUpdateTime = DateTime.Now;
                
                // 读取PLC数据
                await UpdatePLCData();
            });
            
            _eventHub.Subscribe("PLCConnectionChanged", data =>
            {
                dynamic connectionData = data;
                string status = connectionData.Status;
                
                if (status == "Connected")
                {
                    PLCStatus = "已连接";
                }
                else
                {
                    PLCStatus = "未连接";
                }
                
                LastUpdateTime = DateTime.Now;
            });
        }
        
        /// <summary>
        /// 更新PLC数据
        /// </summary>
        private async Task UpdatePLCData()
        {
            try
            {
                // 读取PLC寄存器
                foreach (var item in PLCDataItems)
                {
                    if (!string.IsNullOrEmpty(item.Address))
                    {
                        try
                        {
                            short value = await _plcService.ReadRegisterAsync(item.Address);
                            item.Value = FormatRegisterValue(value, item.Unit);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PLCDataViewModel] 读取寄存器{item.Address}失败: {ex.Message}");
                        }
                    }
                }
                
                // 读取PLC线圈
                foreach (var item in PLCCoilItems)
                {
                    if (!string.IsNullOrEmpty(item.Address))
                    {
                        try
                        {
                            bool status = await _plcService.ReadCoilAsync(item.Address);
                            item.Status = status;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PLCDataViewModel] 读取线圈{item.Address}失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCDataViewModel] 更新PLC数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 格式化寄存器值
        /// </summary>
        private string FormatRegisterValue(short value, string unit)
        {
            // 根据单位格式化值
            if (unit == "°C" || unit == "MPa")
            {
                // 温度和压力显示小数点
                double doubleValue = value / 10.0;
                return doubleValue.ToString("F1");
            }
            
            return value.ToString();
        }
        
        /// <summary>
        /// 手动刷新PLC数据
        /// </summary>
        public async Task RefreshPLCData()
        {
            PLCStatus = "正在刷新...";
            LastUpdateTime = DateTime.Now;
            
            await UpdatePLCData();
            
            PLCStatus = "已刷新";
            LastUpdateTime = DateTime.Now;
        }
        
        /// <summary>
        /// 属性改变通知
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// PLC数据项
    /// </summary>
    public class PLCDataItem : INotifyPropertyChanged
    {
        private string _name;
        private string _value;
        private string _unit;
        private string _address;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// 数据项名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        
        /// <summary>
        /// 数据项值
        /// </summary>
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
        
        /// <summary>
        /// 单位
        /// </summary>
        public string Unit
        {
            get => _unit;
            set
            {
                if (_unit != value)
                {
                    _unit = value;
                    OnPropertyChanged(nameof(Unit));
                }
            }
        }
        
        /// <summary>
        /// PLC地址
        /// </summary>
        public string Address
        {
            get => _address;
            set
            {
                if (_address != value)
                {
                    _address = value;
                    OnPropertyChanged(nameof(Address));
                }
            }
        }
        
        /// <summary>
        /// 属性改变通知
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// PLC线圈状态项
    /// </summary>
    public class PLCCoilItem : INotifyPropertyChanged
    {
        private string _name;
        private string _address;
        private bool _status;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// 线圈名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        
        /// <summary>
        /// 线圈地址
        /// </summary>
        public string Address
        {
            get => _address;
            set
            {
                if (_address != value)
                {
                    _address = value;
                    OnPropertyChanged(nameof(Address));
                }
            }
        }
        
        /// <summary>
        /// 线圈状态 (true=ON, false=OFF)
        /// </summary>
        public bool Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }
        
        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText => Status ? "ON" : "OFF";
        
        /// <summary>
        /// 属性改变通知
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 