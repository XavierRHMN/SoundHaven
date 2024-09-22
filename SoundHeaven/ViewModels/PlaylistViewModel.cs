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

        public Playlist? MainWindowViewModelCurrentPlaylist => _mainWindowViewModel.CurrentPlaylist;

        private Song? _selectedSong;
        public Song? SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (_selectedSong != value)
                {
                    _selectedSong = value;
                    OnPropertyChanged();

                    if (_selectedSong != null && _mainWindowViewModel.CurrentSong != _selectedSong)
                    {
                        _mainWindowViewModel.CurrentSong = _selectedSong;
                    }
                }
            }
        }

        public ObservableCollection<Song>? Songs => MainWindowViewModelCurrentPlaylist?.Songs;

        public AsyncRelayCommand AddSongCommand { get; }
        public RelayCommand EditSongCommand { get; }
        public RelayCommand DeleteSongCommand { get; }

        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel, IOpenFileDialogService openFileDialogService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _openFileDialogService = openFileDialogService;

            AddSongCommand = new AsyncRelayCommand(AddSongAsync);
            EditSongCommand = new RelayCommand(EditSong);
            DeleteSongCommand = new RelayCommand(DeleteSong);

            _mainWindowViewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;

            SelectedSong = _mainWindowViewModel.CurrentSong;
        }

        public async Task AddSongAsync()
        {
            if (MainWindowViewModelCurrentPlaylist != null)
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
                    MainWindowViewModelCurrentPlaylist.Songs.Add(newSong);
                    Console.WriteLine($"Added song: {newSong.Title} to playlist: {MainWindowViewModelCurrentPlaylist.Name}");
                }
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
            if (MainWindowViewModelCurrentPlaylist != null && SelectedSong != null)
            {
                Console.WriteLine($"Deleted song: {SelectedSong.Title} from playlist: {MainWindowViewModelCurrentPlaylist.Name}");
                MainWindowViewModelCurrentPlaylist.Songs.Remove(SelectedSong);
            }
        }

        private void MainWindowViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPlaylist))
            {
                OnPropertyChanged(nameof(MainWindowViewModelCurrentPlaylist));
                OnPropertyChanged(nameof(Songs));
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentSong))
            {
                if (SelectedSong != _mainWindowViewModel.CurrentSong)
                {
                    SelectedSong = _mainWindowViewModel.CurrentSong;
                }
            }
        }
    }
}
