using Avalonia.Controls;
using SoundHeaven.Controls;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using SoundHeaven.Stores;
using System;

namespace SoundHeaven.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
            ;
        }
    }
}
