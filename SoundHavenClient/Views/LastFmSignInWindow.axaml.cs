using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SoundHaven.Services;

namespace SoundHaven.Views;

/// <summary>
/// Last.fm browser-approval sign-in: opens the approval page, polls until the
/// account is linked, then closes itself.
/// </summary>
public partial class LastFmSignInWindow : Window, IDisposable
{
    private readonly ILastFmDataService? _lastFmService;
    private readonly CancellationTokenSource _closeCancellation = new();
    private LastFmWebAuth? _webAuth;

    // Designer/runtime-loader constructor.
    public LastFmSignInWindow()
    {
        InitializeComponent();
    }

    public LastFmSignInWindow(ILastFmDataService lastFmService)
        : this()
    {
        _lastFmService = lastFmService ?? throw new ArgumentNullException(nameof(lastFmService));
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_lastFmService is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Requesting authorization…";
            _webAuth = await _lastFmService.StartWebAuthAsync(_closeCancellation.Token);
            OpenApprovalPage();
            StatusText.Text = "Waiting for you to approve in the browser…";

            bool linked = await _lastFmService.WaitForWebAuthAsync(
                _webAuth,
                _closeCancellation.Token);
            if (linked)
            {
                StatusText.Text = $"Connected as {_lastFmService.Username}!";
                await Task.Delay(700, _closeCancellation.Token);
                Close(true);
            }
            else
            {
                StatusText.Text = "The approval expired. Close this window and try again.";
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed mid-flow.
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closeCancellation.Cancel();
        Dispose();
    }

    public void Dispose()
    {
        _closeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        OpenApprovalPage();
    }

    private void OpenApprovalPage()
    {
        if (_webAuth is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_webAuth.ApprovalUrl) { UseShellExecute = true });
        }
        catch
        {
            StatusText.Text = "Could not open a browser — visit last.fm/api/auth manually.";
        }
    }
}
