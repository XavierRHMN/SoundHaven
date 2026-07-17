using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SoundHaven.ViewModels;
using SoundHaven.Views;

namespace SoundHaven.Helpers;

/// <summary>
/// Maps view models to views and caches the created control per view-model
/// instance. Plain DataTemplates rebuild the whole view on every navigation,
/// which makes tab switches (especially the card-heavy Home) visibly lag;
/// caching turns navigation into a cheap control swap.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<ViewModelBase, Control> _cache = new();

    public Control? Build(object? param)
    {
        if (param is not ViewModelBase viewModel)
        {
            return null;
        }

        if (_cache.TryGetValue(viewModel, out Control? cached))
        {
            return cached;
        }

        Control? view = viewModel switch
        {
            HomeViewModel => new HomeView(),
            PlaylistViewModel => new PlaylistView(),
            PlayerViewModel => new PlayerView(),
            LastFmViewModel => new LastFmView(),
            ThemesViewModel => new ThemesView(),
            _ => null
        };

        if (view is null)
        {
            return null;
        }

        _cache[viewModel] = view;
        return view;
    }

    public bool Match(object? data) =>
        data is HomeViewModel
            or PlaylistViewModel
            or PlayerViewModel
            or LastFmViewModel
            or ThemesViewModel;
}
