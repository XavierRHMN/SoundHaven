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
    }
}
