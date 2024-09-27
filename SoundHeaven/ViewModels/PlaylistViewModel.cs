using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SoundHeaven.Commands;
using SoundHeaven.Helpers;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly IOpenFileDialogService _openFileDialogService;
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly PlaybackViewModel _playbackViewModel;

        public Playlist? CurrentPlaylist => _playbackViewModel.CurrentPlaylist;

        private Playlist _displayedPlaylist;
        public Playlist DisplayedPlaylist
        {
            get => _displayedPlaylist;
            set
            {
                if (_displayedPlaylist != value)
                {
                    _displayedPlaylist = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Songs));
                }
            }
        }

        public ObservableCollection<Song> Songs => DisplayedPlaylist?.Songs;

        private Song _selectedSong;
        public Song SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (_selectedSong != value)
                {
                    _selectedSong = value;
                    OnPropertyChanged();
                    if (_selectedSong != null)
                    {
                        // Update the active playlist and current song in PlaybackViewModel
                        _playbackViewModel.CurrentPlaylist = DisplayedPlaylist;
                        _playbackViewModel.CurrentSong = _selectedSong;
                    }
                }
            }
        }

        public AsyncRelayCommand AddSongCommand { get; }
        public RelayCommand EditSongCommand { get; }
        public RelayCommand DeleteSongCommand { get; }

        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel, PlaybackViewModel playbackViewModel, IOpenFileDialogService openFileDialogService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _playbackViewModel = playbackViewModel;
            _openFileDialogService = openFileDialogService;

            AddSongCommand = new AsyncRelayCommand(AddSongAsync);
            EditSongCommand = new RelayCommand(EditSong);
            DeleteSongCommand = new RelayCommand(DeleteSong);

            _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;

            SelectedSong = _playbackViewModel.CurrentSong;
        }

        public async Task AddSongAsync()
        {
            if (DisplayedPlaylist != null)
            {
                var applicationLifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
                var parentWindow = applicationLifetime.MainWindow;
                if (parentWindow == null)
                {
                    Console.WriteLine("Parent window is not available.");
                    return;
                }

                string? filePath = await _openFileDialogService.ShowOpenFileDialogAsync(parentWindow);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var newSong = Mp3ToSongHelper.GetSongFromMp3(filePath);
                    DisplayedPlaylist.Songs.Add(newSong);
                    Console.WriteLine($"Added song: {newSong.Title} to playlist: {DisplayedPlaylist.Name}");
                }
            }
            else
            {
                Console.WriteLine("No playlist is currently displayed.");
            }
        }

        private void EditSong()
        {
            if (SelectedSong != null)
            {
                SelectedSong.Title = "Edited Song Title";
                OnPropertyChanged(nameof(SelectedSong));
                Console.WriteLine($"Edited song: {SelectedSong.Title}");
            }
        }

        private void DeleteSong()
        {
            if (DisplayedPlaylist != null && SelectedSong != null)
            {
                Console.WriteLine($"Deleted song: {SelectedSong.Title} from playlist: {DisplayedPlaylist.Name}");
                DisplayedPlaylist.Songs.Remove(SelectedSong);
            }
        }

        private void PlaybackViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentPlaylist))
            {
                // Update the displayed playlist when the CurrentPlaylist changes in PlaybackViewModel
                DisplayedPlaylist = _playbackViewModel.CurrentPlaylist;
            }
        }
    }
}