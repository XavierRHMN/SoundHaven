using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHaven.Services;
using SoundHaven.ViewModels;
using System;

namespace SoundHaven.Controls
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
