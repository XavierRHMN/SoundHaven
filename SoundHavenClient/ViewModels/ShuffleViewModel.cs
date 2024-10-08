﻿using System;
using System.Windows.Input;
using Avalonia.Threading;
using SoundHaven.Commands;

namespace SoundHaven.ViewModels
{
    public class ShuffleViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private bool _isUpdating;
        
        
        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                if (_isUpdating) return; // Prevent recursive calls

                _isUpdating = true;

                if (SetProperty(ref _isShuffleEnabled, value))
                {
                    Console.WriteLine($"Shuffle is now {(_isShuffleEnabled ? "enabled" : "disabled")}");

                    // Update the PlaybackViewModel
                    _playbackViewModel.IsShuffleEnabled = _isShuffleEnabled;

                    // Ensure UI update on the UI thread
                    Dispatcher.UIThread.Post(() => OnPropertyChanged());
                }

                _isUpdating = false;
            }
        }

        public RelayCommand ToggleShuffleCommand { get; }

        public ShuffleViewModel(PlaybackViewModel playbackViewModel)
        {
            _playbackViewModel = playbackViewModel;
            ToggleShuffleCommand = new RelayCommand(ToggleShuffle);
            IsShuffleEnabled = false;
        }
        
        private void ToggleShuffle()
        {
            IsShuffleEnabled = !IsShuffleEnabled;
            Console.WriteLine($"IsShuffleEnabled is now {IsShuffleEnabled}");
        }
    }
}
