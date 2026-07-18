using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

/// <summary>
/// Backs the Liked Albums page: the albums hearted from Home, shown as a card
/// grid under a Liked Songs-style header. Cards open (or play) the album page.
/// </summary>
public sealed class LikedAlbumsViewModel : ViewModelBase
{
    private readonly LikedAlbumsStore _likedAlbumsStore;
    private readonly AlbumViewModel _albumViewModel;
    private readonly NavigationService _navigation;
    private readonly IUserNotificationService _notifications;

    public LikedAlbumsViewModel(
        LikedAlbumsStore likedAlbumsStore,
        AlbumViewModel albumViewModel,
        NavigationService navigation,
        IUserNotificationService notifications)
    {
        _likedAlbumsStore = likedAlbumsStore
            ?? throw new ArgumentNullException(nameof(likedAlbumsStore));
        _albumViewModel = albumViewModel ?? throw new ArgumentNullException(nameof(albumViewModel));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        OpenAlbumCommand = new RelayCommand<Song>(OpenAlbum, album => album is not null);
        PlayAlbumCommand = new AsyncRelayCommand<Song>(
            PlayAlbumAsync,
            album => album is not null,
            exception => _notifications.ShowError(exception.Message));
        UnlikeAlbumCommand = new RelayCommand<Song>(UnlikeAlbum, album => album is not null);

        Albums.CollectionChanged += OnAlbumsChanged;
        _ = EnsureCoversAsync();
    }

    public ObservableCollection<Song> Albums => _likedAlbumsStore.Albums;

    public bool HasAlbums => Albums.Count > 0;

    public string AlbumStatsText => Albums.Count == 1 ? "1 ALBUM" : $"{Albums.Count} ALBUMS";

    public RelayCommand<Song> OpenAlbumCommand { get; }

    public AsyncRelayCommand<Song> PlayAlbumCommand { get; }

    public RelayCommand<Song> UnlikeAlbumCommand { get; }

    private void OpenAlbum(Song? album)
    {
        if (album is null)
        {
            return;
        }

        _ = _albumViewModel.ShowAlbumAsync(album);
        _navigation.NavigateTo(_albumViewModel);
    }

    private async Task PlayAlbumAsync(Song? album)
    {
        if (album is null)
        {
            return;
        }

        await _albumViewModel.ShowAlbumAsync(album);
        if (_albumViewModel.PlayAlbumCommand.CanExecute(null))
        {
            _albumViewModel.PlayAlbumCommand.Execute(null);
        }
    }

    private void UnlikeAlbum(Song? album)
    {
        if (album is null)
        {
            return;
        }

        _likedAlbumsStore.Toggle(album);
        _notifications.ShowInfo($"Removed “{album.Title}” from Liked Albums.");
    }

    private void OnAlbumsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAlbums));
        OnPropertyChanged(nameof(AlbumStatsText));
        _ = EnsureCoversAsync();
    }

    // Liked albums persist only their cover URL; load the bitmaps lazily so the
    // grid shows art after a restart.
    private async Task EnsureCoversAsync()
    {
        foreach (Song album in Albums)
        {
            if (album.Artwork is not null)
            {
                continue;
            }

            try
            {
                await album.LoadThumbnailAsync();
            }
            catch
            {
                // Covers are decorative; the placeholder shows if one fails.
            }
        }
    }

    public override void Dispose()
    {
        Albums.CollectionChanged -= OnAlbumsChanged;
        base.Dispose();
    }
}
