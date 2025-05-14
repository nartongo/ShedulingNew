using System.Windows.Controls;
using ShedulingNew.UI.ViewModels;

namespace ShedulingNew.UI.Views
{
    /// <summary>
    /// ConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigView : UserControl
    {
        public ConfigView()
        {
            InitializeComponent();
            DataContext = new ConfigViewModel();
        }
    }
} 