using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SoundHaven.ViewModels;

namespace SoundHaven.Services;

public interface IUserNotificationService
{
    void ShowError(string message);

    void ShowInfo(string message);

    void Clear();
}

public sealed class NotificationService : ViewModelBase, IUserNotificationService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _clearCancellation;
    private string _message = string.Empty;
    private bool _isError;

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public bool IsVisible => !string.IsNullOrWhiteSpace(Message);

    public void ShowError(string message)
    {
        Show(message, true, TimeSpan.FromSeconds(4));
    }

    public void ShowInfo(string message)
    {
        Show(message, false, TimeSpan.FromSeconds(2.5));
    }

    public void Clear()
    {
        RunOnUiThread(() =>
        {
            CancelPendingClear();
            Message = string.Empty;
            IsError = false;
        });
    }

    public override void Dispose()
    {
        CancelPendingClear();
        base.Dispose();
    }

    private void Show(string message, bool isError, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            CancelPendingClear();
            Message = message.Trim();
            IsError = isError;

            var cancellation = new CancellationTokenSource();
            lock (_gate)
            {
                _clearCancellation = cancellation;
            }

            _ = ClearAfterDelayAsync(duration, cancellation.Token);
        });
    }

    private async Task ClearAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            RunOnUiThread(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Message = string.Empty;
                    IsError = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // A newer notification replaced this one.
        }
    }

    private void CancelPendingClear()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _clearCancellation;
            _clearCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
