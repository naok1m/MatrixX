using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class WeaponBrowserViewModel : ViewModelBase
{
    private readonly IWeaponLibraryService _library;
    private readonly IMacroProcessor _macroProcessor;
    private readonly ILogger<WeaponBrowserViewModel> _logger;

    // ── Game selector ─────────────────────────────────────────────────────
    public ObservableCollection<string> Games { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Categories))]
    private string _selectedGame = "";

    // ── Category selector ─────────────────────────────────────────────────
    public ObservableCollection<string> Categories { get; } = [];

    [ObservableProperty]
    private string _selectedCategory = "All";

    partial void OnSelectedGameChanged(string value)     { RefreshCategories(); ApplyFilters(); }
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();

    // ── Search ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value) => ApplyFilters();

    // ── Results ───────────────────────────────────────────────────────────
    public ObservableCollection<WeaponLibraryEntry> Weapons { get; } = [];

    // ── Active weapon ─────────────────────────────────────────────────────
    [ObservableProperty] private string _activeWeaponName = "Nenhuma";
    [ObservableProperty] private WeaponLibraryEntry? _activeEntry;

    public WeaponBrowserViewModel(
        IWeaponLibraryService library,
        IMacroProcessor macroProcessor,
        ILogger<WeaponBrowserViewModel> logger)
    {
        _library        = library;
        _macroProcessor = macroProcessor;
        _logger         = logger;

        RefreshGames();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Commands
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateWeapon(WeaponLibraryEntry entry)
    {
        var profile = entry.ToWeaponProfile();
        _macroProcessor.SetWeaponProfile(profile);
        ActiveWeaponName = entry.Name;
        ActiveEntry      = entry;
        _logger.LogInformation("Weapon manually activated: {Name}", entry.Name);
    }

    [RelayCommand]
    private void SelectGame(string game)
    {
        SelectedGame = game;
    }

    [RelayCommand]
    private void ClearWeapon()
    {
        _macroProcessor.SetWeaponProfile(null);
        ActiveWeaponName = "Nenhuma";
        ActiveEntry      = null;
        _logger.LogInformation("Active weapon cleared");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    private void RefreshGames()
    {
        var games = _library.GetGames();
        Games.Clear();
        foreach (var g in games)
            Games.Add(g);

        SelectedGame = Games.FirstOrDefault() ?? "";
    }

    private void RefreshCategories()
    {
        Categories.Clear();
        Categories.Add("All");

        var cats = string.IsNullOrEmpty(SelectedGame)
            ? _library.GetAll().Select(e => e.Category).Distinct().Order()
            : (IEnumerable<string>)_library.GetCategories(SelectedGame);

        foreach (var c in cats)
            Categories.Add(c);

        SelectedCategory = "All";
    }

    private void ApplyFilters()
    {
        var game     = string.IsNullOrEmpty(SelectedGame) ? null : SelectedGame;
        var category = SelectedCategory == "All" ? null : SelectedCategory;
        var results  = _library.Search(SearchText, game, category);

        Weapons.Clear();
        foreach (var w in results)
            Weapons.Add(w);
    }
}
