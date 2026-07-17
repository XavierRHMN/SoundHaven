using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class LastFmViewModel : ViewModelBase
{
    private readonly ILastFmDataService _lastFmDataService;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private int _disposeState;

    public ObservableCollection<Song> RecentlyPlayedTracks { get; }
    public ObservableCollection<Song> RecommendedAlbums { get; }
    public bool IsLastFmConfigured => _lastFmDataService.IsConfigured;
    public bool IsLastFmUnavailable => !IsLastFmConfigured;
    public bool IsLastFmContentVisible =>
        IsLastFmConfigured && !IsUsernamePromptVisible;
    public string LastFmConfigurationMessage =>
        _lastFmDataService.LastError
        ?? "Last.fm is not configured. Set LASTFM_API_KEY and LASTFM_API_SECRET to enable it.";

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isSubmitting;
    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set => SetProperty(ref _isSubmitting, value);
    }

    private bool _isUsernamePromptVisible;
    public bool IsUsernamePromptVisible
    {
        get => _isUsernamePromptVisible;
        set
        {
            if (SetProperty(ref _isUsernamePromptVisible, value))
            {
                OnPropertyChanged(nameof(IsLastFmContentVisible));
            }
        }
    }

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public LastFmViewModel(ILastFmDataService lastFmDataService)
    {
        _lastFmDataService = lastFmDataService
            ?? throw new ArgumentNullException(nameof(lastFmDataService));

        RecentlyPlayedTracks = new ObservableCollection<Song>();
        RecommendedAlbums = new ObservableCollection<Song>();
        _isUsernamePromptVisible = IsLastFmConfigured;
    }

    public async Task SubmitDetailsAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (!IsLastFmConfigured)
        {
            ErrorMessage = LastFmConfigurationMessage;
            return;
        }

        if (IsSubmitting)
        {
            return;
        }

        string normalizedUsername = Username?.Trim() ?? string.Empty;
        if (normalizedUsername.Length == 0 || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }

        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellationTokenSource.Token);
        CancellationToken linkedCancellationToken =
            linkedCancellationTokenSource.Token;

        IsSubmitting = true;
        ErrorMessage = string.Empty;

        try
        {
            bool userExists = await _lastFmDataService.UserExistsAsync(
                normalizedUsername,
                password,
                linkedCancellationToken);

            if (!userExists)
            {
                ErrorMessage = _lastFmDataService.LastError
                    ?? "Invalid Last.fm username or password.";
                return;
            }

            Username = normalizedUsername;
            IsUsernamePromptVisible = false;
            await LoadDataAsync(linkedCancellationToken);
        }
        catch (OperationCanceledException)
            when (linkedCancellationToken.IsCancellationRequested)
        {
            // Closing the view or cancelling sign-in should not surface as an error.
        }
        catch (Exception)
        {
            ErrorMessage = _lastFmDataService.LastError
                ?? "Last.fm sign-in is currently unavailable. Please try again.";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        try
        {
            var recentlyPlayedTracks =
                await _lastFmDataService.GetRecentlyPlayedTracksAsync(cancellationToken);
            string? recentlyPlayedError = _lastFmDataService.LastError;

            var recommendedAlbums =
                await _lastFmDataService.GetRecommendedAlbumsAsync(cancellationToken);
            string? recommendationsError = _lastFmDataService.LastError;

            cancellationToken.ThrowIfCancellationRequested();

            RecentlyPlayedTracks.Clear();
            RecommendedAlbums.Clear();

            var shuffledAlbums = recommendedAlbums
                .OrderBy(_ => Guid.NewGuid())
                .ToList();

            foreach (var song in shuffledAlbums)
            {
                RecommendedAlbums.Add(song);
            }

            foreach (var song in recentlyPlayedTracks)
            {
                RecentlyPlayedTracks.Add(song);
            }

            ErrorMessage = recommendationsError
                ?? recentlyPlayedError
                ?? string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _lifetimeCancellationTokenSource.Cancel();
        _lifetimeCancellationTokenSource.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
