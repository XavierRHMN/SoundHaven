using System;

namespace SoundHaven.ViewModels;

public sealed class NavigationService : ViewModelBase
{
    private ViewModelBase _currentViewModel;

    public NavigationService(ViewModelBase initialViewModel)
    {
        _currentViewModel = initialViewModel
            ?? throw new ArgumentNullException(nameof(initialViewModel));
    }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public void NavigateTo(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
