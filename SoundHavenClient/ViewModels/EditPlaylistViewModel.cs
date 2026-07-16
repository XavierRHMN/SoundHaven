using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;

namespace SoundHaven.ViewModels;

public sealed class EditPlaylistViewModel : ViewModelBase
{
    public const int MaxDescriptionLength = 500;

    private readonly Playlist _playlist;
    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly Window _hostWindow;
    private readonly Bitmap?[] _mosaicSlots = new Bitmap?[4];
    private byte[] _coverImageData;
    private bool _hasCustomCoverPreview;

    public EditPlaylistViewModel(
        Playlist playlist,
        IOpenFileDialogService openFileDialogService,
        Window hostWindow,
        bool isCreating = false)
    {
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _openFileDialogService = openFileDialogService
            ?? throw new ArgumentNullException(nameof(openFileDialogService));
        _hostWindow = hostWindow ?? throw new ArgumentNullException(nameof(hostWindow));

        DialogTitle = isCreating ? "Create playlist" : "Edit playlist";
        PlaylistTitle = playlist.Name;
        Description = playlist.Description ?? string.Empty;
        _coverImageData = playlist.CoverImageData is { Length: > 0 }
            ? (byte[])playlist.CoverImageData.Clone()
            : [];

        LoadMosaicFromPlaylist();
        ApplyCoverPreviewFromData();

        ChangeImageCommand = new AsyncRelayCommand(ChangeImageAsync);
        SaveCommand = new RelayCommand(Save);
        CloseCommand = new RelayCommand(CloseWithoutSaving);
    }

    public event EventHandler<bool>? CloseRequested;

    public string DialogTitle { get; }

    private string _playlistTitle = string.Empty;
    public string PlaylistTitle
    {
        get => _playlistTitle;
        set => SetProperty(ref _playlistTitle, value ?? string.Empty);
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set
        {
            string next = value ?? string.Empty;
            if (next.Length > MaxDescriptionLength)
            {
                next = next[..MaxDescriptionLength];
            }

            if (SetProperty(ref _description, next))
            {
                OnPropertyChanged(nameof(DescriptionCounterText));
            }
        }
    }

    public string DescriptionCounterText => $"{Description.Length}/{MaxDescriptionLength}";

    private Bitmap? _coverPreview;
    public Bitmap? CoverPreview
    {
        get => _coverPreview;
        private set
        {
            if (SetProperty(ref _coverPreview, value))
            {
                NotifyCoverState();
            }
        }
    }

    public Bitmap? CoverSlot0 => _hasCustomCoverPreview ? CoverPreview : _mosaicSlots[0];
    public Bitmap? CoverSlot1 => _hasCustomCoverPreview ? null : _mosaicSlots[1];
    public Bitmap? CoverSlot2 => _hasCustomCoverPreview ? null : _mosaicSlots[2];
    public Bitmap? CoverSlot3 => _hasCustomCoverPreview ? null : _mosaicSlots[3];

    public bool HasCoverArt =>
        _hasCustomCoverPreview
        || _mosaicSlots.Any(slot => slot is not null);

    public bool HasMosaicCover =>
        !_hasCustomCoverPreview
        && _playlist.Songs.Count >= 4
        && _mosaicSlots[0] is not null;

    public bool HasSingleCover =>
        (_hasCustomCoverPreview && CoverPreview is not null)
        || (HasCoverArt && !HasMosaicCover);

    public AsyncRelayCommand ChangeImageCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }

    public string SavedTitle { get; private set; } = string.Empty;
    public string SavedDescription { get; private set; } = string.Empty;
    public byte[] SavedCoverImageData { get; private set; } = [];

    private async Task ChangeImageAsync()
    {
        string? path = await _openFileDialogService.ShowOpenImageDialogAsync(_hostWindow);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        byte[] bytes = await File.ReadAllBytesAsync(path);
        Bitmap? bitmap = CreateBitmap(bytes);
        if (bitmap is null)
        {
            return;
        }

        _coverImageData = bytes;
        _hasCustomCoverPreview = true;
        CoverPreview = bitmap;
    }

    private void Save()
    {
        SavedTitle = string.IsNullOrWhiteSpace(PlaylistTitle) ? "New playlist" : PlaylistTitle.Trim();
        SavedDescription = Description.Trim();
        SavedCoverImageData = _coverImageData is { Length: > 0 }
            ? (byte[])_coverImageData.Clone()
            : [];
        CloseRequested?.Invoke(this, true);
    }

    private void CloseWithoutSaving() => CloseRequested?.Invoke(this, false);

    private void LoadMosaicFromPlaylist()
    {
        var artworks = _playlist.Songs
            .Select(song => song.Artwork)
            .Where(artwork => artwork is not null)
            .Take(4)
            .Cast<Bitmap>()
            .ToList();

        for (int i = 0; i < _mosaicSlots.Length; i++)
        {
            _mosaicSlots[i] = i < artworks.Count ? artworks[i] : null;
        }
    }

    private void ApplyCoverPreviewFromData()
    {
        if (_coverImageData is { Length: > 0 })
        {
            _hasCustomCoverPreview = true;
            CoverPreview = CreateBitmap(_coverImageData);
            return;
        }

        _hasCustomCoverPreview = false;
        CoverPreview = null;
        NotifyCoverState();
    }

    private void NotifyCoverState()
    {
        OnPropertyChanged(nameof(CoverSlot0));
        OnPropertyChanged(nameof(CoverSlot1));
        OnPropertyChanged(nameof(CoverSlot2));
        OnPropertyChanged(nameof(CoverSlot3));
        OnPropertyChanged(nameof(HasCoverArt));
        OnPropertyChanged(nameof(HasMosaicCover));
        OnPropertyChanged(nameof(HasSingleCover));
    }

    private static Bitmap? CreateBitmap(byte[] data)
    {
        if (data is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public static Window? GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
