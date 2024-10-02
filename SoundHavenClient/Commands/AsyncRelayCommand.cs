using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SoundHaven.Commands
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        public event EventHandler? CanExecuteChanged;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public async void Execute(object? parameter)
        {
            await _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}
