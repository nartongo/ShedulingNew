using System;
using System.Windows.Input;

namespace ShedulingNew.UI.Commands
{
    /// <summary>
    /// RelayCommand命令实现
    /// 用于MVVM模式中Command的实现，将UI事件绑定到ViewModel的方法
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;
        
        /// <summary>
        /// 构造函数，创建一个始终可执行的命令
        /// </summary>
        /// <param name="execute">执行方法委托</param>
        public RelayCommand(Action<object> execute) : this(execute, null) { }
        
        /// <summary>
        /// 构造函数，创建一个带有可执行条件的命令
        /// </summary>
        /// <param name="execute">执行方法委托</param>
        /// <param name="canExecute">确定命令是否可执行的方法委托</param>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        /// <summary>
        /// 确定此命令是否可在其当前状态下执行
        /// </summary>
        /// <param name="parameter">命令参数</param>
        /// <returns>如果可执行此命令，则为true；否则为false</returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }
        
        /// <summary>
        /// CommandManager的RequerySuggested事件，当命令可用性可能已更改时发生
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        
        /// <summary>
        /// 执行命令关联的操作
        /// </summary>
        /// <param name="parameter">命令参数</param>
        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
} 