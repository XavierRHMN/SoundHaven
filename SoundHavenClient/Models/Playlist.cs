using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using SoundHaven.ViewModels;

namespace SoundHaven.Models
{
    public class Playlist : ViewModelBase
    {
        private readonly Bitmap?[] _sidebarSlots = new Bitmap?[4];
        private ObservableCollection<Song> _songs = new();

        public Playlist()
        {
            AttachSongs(_songs);
        }

        public ObservableCollection<Song> Songs
        {
            get => _songs;
            set
            {
                ObservableCollection<Song> next = value ?? new ObservableCollection<Song>();
                if (ReferenceEquals(_songs, next))
                {
                    return;
                }

                DetachSongs(_songs);
                _songs = next;
                AttachSongs(_songs);
                OnPropertyChanged();
                RefreshSidebarCovers();
            }
        }

        public int Id { get; set; }

        /// <summary>UTC creation/last-modified stamps persisted in SQLite (null for
        /// playlists created before the v3 schema; sorters fall back to Id order).</summary>
        public DateTime? CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value ?? string.Empty);
        }

        private byte[] _coverImageData = [];
        public byte[] CoverImageData
        {
            get => _coverImageData;
            set
            {
                byte[] next = value ?? [];
                if (SetProperty(ref _coverImageData, next))
                {
                    _coverImage = null;
                    OnPropertyChanged(nameof(CoverImage));
                    OnPropertyChanged(nameof(HasCustomCover));
                    RefreshSidebarCovers();
                }
            }
        }

        public bool HasCustomCover => CoverImageData is { Length: > 0 };

        private Bitmap? _coverImage;
        public Bitmap? CoverImage
        {
            get
            {
                if (_coverImage is null && CoverImageData is { Length: > 0 })
                {
                    try
                    {
                        using var stream = new MemoryStream(CoverImageData);
                        _coverImage = new Bitmap(stream);
                    }
                    catch
                    {
                        return null;
                    }
                }

                return _coverImage;
            }
        }

        public string ItemCountText =>
            Songs.Count == 1 ? "1 item" : $"{Songs.Count} items";

        public Bitmap? SidebarSlot0 => HasCustomCover ? CoverImage : _sidebarSlots[0];
        public Bitmap? SidebarSlot1 => HasCustomCover ? null : _sidebarSlots[1];
        public Bitmap? SidebarSlot2 => HasCustomCover ? null : _sidebarSlots[2];
        public Bitmap? SidebarSlot3 => HasCustomCover ? null : _sidebarSlots[3];

        public bool HasSidebarCover =>
            HasCustomCover || _sidebarSlots.Any(slot => slot is not null);

        public bool HasSidebarMosaic =>
            !HasCustomCover
            && Songs.Count >= 4
            && _sidebarSlots[0] is not null;

        public bool HasSidebarSingleCover =>
            HasCustomCover || (HasSidebarCover && !HasSidebarMosaic);

        public Song? GetPreviousNextSong(Song? currentSong, PlaybackViewModel.Direction direction)
        {
            if (currentSong == null || Songs.Count == 0)
            {
                return null;
            }

            int index = Songs.IndexOf(currentSong);
            if (index < 0)
            {
                return null;
            }

            int newIndex = (index + (int)direction) % Songs.Count;
            if (newIndex < 0)
            {
                newIndex += Songs.Count;
            }

            return Songs[newIndex];
        }

        public void RefreshSidebarCovers()
        {
            if (HasCustomCover)
            {
                Array.Clear(_sidebarSlots);
            }
            else
            {
                var artworks = Songs
                    .Select(song => song.Artwork)
                    .Where(artwork => artwork is not null)
                    .Take(4)
                    .Cast<Bitmap>()
                    .ToList();

                for (int i = 0; i < _sidebarSlots.Length; i++)
                {
                    _sidebarSlots[i] = i < artworks.Count ? artworks[i] : null;
                }
            }

            OnPropertyChanged(nameof(ItemCountText));
            OnPropertyChanged(nameof(SidebarSlot0));
            OnPropertyChanged(nameof(SidebarSlot1));
            OnPropertyChanged(nameof(SidebarSlot2));
            OnPropertyChanged(nameof(SidebarSlot3));
            OnPropertyChanged(nameof(HasSidebarCover));
            OnPropertyChanged(nameof(HasSidebarMosaic));
            OnPropertyChanged(nameof(HasSidebarSingleCover));
        }

        private void AttachSongs(ObservableCollection<Song> songs)
        {
            songs.CollectionChanged += OnSongsCollectionChanged;
            foreach (Song song in songs)
            {
                song.PropertyChanged += OnSongPropertyChanged;
            }
        }

        private void DetachSongs(ObservableCollection<Song> songs)
        {
            songs.CollectionChanged -= OnSongsCollectionChanged;
            foreach (Song song in songs)
            {
                song.PropertyChanged -= OnSongPropertyChanged;
            }
        }

        private void OnSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Song song in e.OldItems.OfType<Song>())
                {
                    song.PropertyChanged -= OnSongPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (Song song in e.NewItems.OfType<Song>())
                {
                    song.PropertyChanged += OnSongPropertyChanged;
                }
            }

            RefreshSidebarCovers();
        }

        private void OnSongPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Song.Artwork))
            {
                RefreshSidebarCovers();
            }
        }
    }
}
