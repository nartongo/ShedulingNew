using System.Windows;
using System.Windows.Controls;
using ShedulingNew.UI.ViewModels;

namespace ShedulingNew.UI.Views
{
    /// <summary>
    /// PLCDataView.xaml 的交互逻辑
    /// </summary>
    public partial class PLCDataView : UserControl
    {
        private PLCDataViewModel _viewModel;
        
        public PLCDataView()
        {
            InitializeComponent();
            
            // 获取ViewModel实例
            _viewModel = Resources["PLCDataViewModel"] as PLCDataViewModel;
        }
        
        /// <summary>
        /// 刷新按钮点击事件处理
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.RefreshPLCData();
            }
        }
    }
} 