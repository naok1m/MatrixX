using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;

namespace InputBusX.UI.ViewModels;

public partial class WeaponLibraryViewModel : ObservableObject
{
    private readonly IWeaponLibraryService _library;

    // ── Filters ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedGame = "All";
    [ObservableProperty] private string _selectedCategory = "All";

    // ── Lists ─────────────────────────────────────────────────────────────
    public ObservableCollection<string> Games      { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<WeaponLibraryEntry> Results { get; } = [];

    // ── Selection ─────────────────────────────────────────────────────────
    [ObservableProperty] private WeaponLibraryEntry? _selectedEntry;

    /// <summary>Fires when the user confirms an entry to add to their profile.</summary>
    public event Action<WeaponLibraryEntry>? EntrySelected;

    public WeaponLibraryViewModel(IWeaponLibraryService library)
    {
        _library = library;
        RefreshGames();
        ApplyFilters();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Reactive updates
    // ──────────────────────────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value)       => ApplyFilters();
    partial void OnSelectedGameChanged(string value)     { RefreshCategories(); ApplyFilters(); }
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();

    // ──────────────────────────────────────────────────────────────────────
    //  Commands
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectEntry(WeaponLibraryEntry? entry)
    {
        if (entry == null) return;
        EntrySelected?.Invoke(entry);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText       = "";
        SelectedGame     = "All";
        SelectedCategory = "All";
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    private void RefreshGames()
    {
        Games.Clear();
        Games.Add("All");
        foreach (var g in _library.GetGames())
            Games.Add(g);
    }

    private void RefreshCategories()
    {
        Categories.Clear();
        Categories.Add("All");

        IEnumerable<string> cats = SelectedGame == "All"
            ? _library.GetAll().Select(e => e.Category).Distinct().Order()
            : _library.GetCategories(SelectedGame);

        foreach (var c in cats)
            Categories.Add(c);

        SelectedCategory = "All";
    }

    private void ApplyFilters()
    {
        var game     = SelectedGame     == "All" ? null : SelectedGame;
        var category = SelectedCategory == "All" ? null : SelectedCategory;
        var results  = _library.Search(SearchText, game, category);

        Results.Clear();
        foreach (var r in results)
            Results.Add(r);
    }
}
