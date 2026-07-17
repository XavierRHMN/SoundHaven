using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.ViewModels;
using SoundHaven.Views;

namespace SoundHaven
{
    public partial class MainWindow : Window
    {
        private MiniPlayerWindow? _miniPlayer;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnMiniPlayerClick(object? sender, RoutedEventArgs e)
        {
            if (_miniPlayer is not null)
            {
                return;
            }

            e.Handled = true;
            _miniPlayer = new MiniPlayerWindow
            {
                DataContext = DataContext
            };
            _miniPlayer.Closed += (_, _) =>
            {
                _miniPlayer = null;
                Show();
                Activate();
            };
            _miniPlayer.Show();
            Hide();
        }

        private void OnSoundOutputClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            e.Handled = true;

            var flyout = DarkMenuFlyout.Create(PlacementMode.TopEdgeAlignedLeft);
            flyout.Items.Add(new MenuItem { Header = "Sound output", IsEnabled = false });

            string currentId = viewModel.CurrentOutputDeviceId;
            foreach (AudioOutputDevice device in viewModel.GetOutputDevices())
            {
                bool isCurrent = string.Equals(device.Id, currentId, System.StringComparison.Ordinal);
                string deviceId = device.Id;
                flyout.Items.Add(new MenuItem
                {
                    Header = device.Name,
                    Icon = CreateSoundOutputIcon(isCurrent),
                    Command = new RelayCommand(() => _ = viewModel.SetOutputDeviceAsync(deviceId))
                });
            }

            flyout.ShowAt(button);
        }

        // Selected device shows a check; the rest show a speaker, mirroring TIDAL.
        private static PathIcon CreateSoundOutputIcon(bool isCurrent)
        {
            const string checkGeometry =
                "M9,16.17L4.83,12l-1.42,1.41L9,19L21,7l-1.41-1.41L9,16.17z";
            const string speakerGeometry = "M3,9v6h4l5,5V4L7,9H3z";

            return new PathIcon
            {
                Data = StreamGeometry.Parse(isCurrent ? checkGeometry : speakerGeometry),
                Width = 15,
                Height = 15,
                Foreground = isCurrent
                    ? Brushes.White
                    : new SolidColorBrush(Color.Parse("#B3B3B3"))
            };
        }

        private void OnNowPlayingMoreClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button
                || DataContext is not MainWindowViewModel viewModel
                || viewModel.PlaybackViewModel.CurrentSong is not { } song)
            {
                return;
            }

            e.Handled = true;
            HomeViewModel home = viewModel.HomeViewModel;
            home.SetMenuSong(song);

            var flyout = DarkMenuFlyout.Create(PlacementMode.TopEdgeAlignedLeft);
            var addToPlaylist = new MenuItem { Header = "Add to playlist" };
            foreach (Playlist playlist in home.Playlists)
            {
                addToPlaylist.Items.Add(new MenuItem
                {
                    Header = playlist.Name,
                    Command = home.AddToPlaylistCommand,
                    CommandParameter = playlist
                });
            }

            if (home.Playlists.Count > 0)
            {
                addToPlaylist.Items.Add(new Separator());
            }

            addToPlaylist.Items.Add(new MenuItem
            {
                Header = "Create one",
                Command = home.CreatePlaylistAndAddSongCommand,
                Icon = new PathIcon
                {
                    Data = StreamGeometry.Parse("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"),
                    Width = 16,
                    Height = 16,
                    Foreground = Brushes.White
                }
            });

            flyout.Items.Add(addToPlaylist);
            flyout.ShowAt(button);
        }

        private void MinimizeButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void OnPointerPressedTitleBar(object sender, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }

        private void CloseButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
