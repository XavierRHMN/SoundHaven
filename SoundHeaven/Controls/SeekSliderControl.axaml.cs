using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using System;

namespace SoundHeaven.Controls
{
    public partial class SeekSliderControl : UserControl
    {
        private bool _isDragging = false;
        private double _newSeekPosition;

        public SeekSliderControl()
        {
            InitializeComponent();
        }

        private void OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                if (_isDragging)
                {
                    // Set the seek position when dragging stops
                    viewModel.AudioPlayerService?.Seek(TimeSpan.FromSeconds(viewModel.SeekPosition));
                    _isDragging = false;
                }
            }
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            Console.WriteLine(_isDragging);
            _isDragging = true;
        }
    }
}
