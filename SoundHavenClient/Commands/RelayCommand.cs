using SoundHaven.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SoundHaven.Commands
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public void RaiseCanExecuteChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter)
        {
            _execute();
        }
    }


    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool CanExecute(object parameter)
        {
            if (parameter == null && typeof(T).IsValueType)
                return _canExecute == null || _canExecute(default);

            return _canExecute == null || _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            if (parameter == null && typeof(T).IsValueType)
                _execute(default);
            else
                _execute((T)parameter);
        }
    }
    
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Predicate<T?>? _canExecute;
        public event EventHandler? CanExecuteChanged;

        public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(ConvertParameter(parameter));

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public async Task ExecuteAsync(object? parameter)
        {
            await _execute(ConvertParameter(parameter));
        }

        public void RaiseCanExecuteChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private static T? ConvertParameter(object? parameter)
        {
            if (parameter is null && default(T) is null)
            {
                return default;
            }

            if (parameter is T tParameter)
            {
                return tParameter;
            }

            throw new InvalidCastException($"Parameter type mismatch. Expected {typeof(T).Name}.");
        }
    }
}
