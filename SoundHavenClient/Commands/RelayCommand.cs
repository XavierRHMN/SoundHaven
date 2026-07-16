using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace SoundHaven.Commands;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Predicate<T>? _canExecute;

    public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    public bool CanExecute(object? parameter)
    {
        T converted = ConvertParameter(parameter);
        return _canExecute?.Invoke(converted) ?? true;
    }

    public void Execute(object? parameter) => _execute(ConvertParameter(parameter));

    private static T ConvertParameter(object? parameter)
    {
        if (parameter is null)
        {
            return default!;
        }

        if (parameter is T converted)
        {
            return converted;
        }

        throw new InvalidCastException($"Parameter type mismatch. Expected {typeof(T).Name}.");
    }
}

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Predicate<T?>? _canExecute;
    private readonly Action<Exception>? _onException;
    private int _isRunning;

    public AsyncRelayCommand(
        Func<T?, Task> execute,
        Predicate<T?>? canExecute = null,
        Action<Exception>? onException = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public bool CanExecute(object? parameter)
    {
        return !IsRunning && (_canExecute?.Invoke(ConvertParameter(parameter)) ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _execute(ConvertParameter(parameter));
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected command outcome.
        }
        catch (Exception exception)
        {
            if (_onException is not null)
            {
                _onException(exception);
            }
            else
            {
                Debug.WriteLine(exception);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private static T? ConvertParameter(object? parameter)
    {
        if (parameter is null)
        {
            return default;
        }

        if (parameter is T converted)
        {
            return converted;
        }

        throw new InvalidCastException($"Parameter type mismatch. Expected {typeof(T).Name}.");
    }
}
