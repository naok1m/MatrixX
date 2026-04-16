using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IWeaponLibraryService
{
    IReadOnlyList<WeaponLibraryEntry> GetAll();
    IReadOnlyList<string> GetGames();
    IReadOnlyList<string> GetCategories(string game);
    IReadOnlyList<WeaponLibraryEntry> Search(string query, string? game = null, string? category = null);
}
