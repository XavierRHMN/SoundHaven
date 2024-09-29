﻿using System;
using System.Windows.Input;
using Avalonia.Threading;
using SoundHaven.Commands;

namespace SoundHaven.ViewModels
{
    public class ShuffleViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private bool _isUpdating = false;

        public ShuffleViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            ToggleShuffleCommand = new RelayCommand(ToggleShuffle);

            // Ensure the initial state is set correctly
            IsShuffleEnabled = false;
        }

        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                if (_isUpdating) return; // Prevent recursive calls

                _isUpdating = true;

                if (_isShuffleEnabled != value)
                {
                    _isShuffleEnabled = value;
                    OnPropertyChanged(nameof(IsShuffleEnabled));
                    Console.WriteLine($"Shuffle is now {(_isShuffleEnabled ? "enabled" : "disabled")}");

                    // Update the PlaybackViewModel
                    _mainWindowViewModel.PlaybackViewModel.IsShuffleEnabled = _isShuffleEnabled;

                    // Ensure UI update on the UI thread
                    Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsShuffleEnabled)));
                }

                _isUpdating = false;
            }
        }

        public ICommand ToggleShuffleCommand { get; }

        private void ToggleShuffle()
        {
            Console.WriteLine("ToggleShuffle called");
            IsShuffleEnabled = !IsShuffleEnabled;
            Console.WriteLine($"IsShuffleEnabled is now {IsShuffleEnabled}");
        }
    }
}
