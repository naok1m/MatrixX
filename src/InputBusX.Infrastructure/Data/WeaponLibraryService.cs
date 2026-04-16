using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;

namespace InputBusX.Infrastructure.Data;

public sealed class WeaponLibraryService : IWeaponLibraryService
{
    private readonly Lazy<IReadOnlyList<WeaponLibraryEntry>> _entries;

    public WeaponLibraryService()
    {
        _entries = new Lazy<IReadOnlyList<WeaponLibraryEntry>>(Load);
    }

    public IReadOnlyList<WeaponLibraryEntry> GetAll() => _entries.Value;

    public IReadOnlyList<string> GetGames() =>
        _entries.Value.Select(e => e.Game).Distinct().Order().ToList();

    public IReadOnlyList<string> GetCategories(string game) =>
        _entries.Value
            .Where(e => e.Game == game)
            .Select(e => e.Category)
            .Distinct()
            .Order()
            .ToList();

    public IReadOnlyList<WeaponLibraryEntry> Search(string query, string? game = null, string? category = null)
    {
        var q = query.Trim();
        return _entries.Value
            .Where(e => (game     == null || e.Game     == game)
                     && (category == null || e.Category == category)
                     && (string.IsNullOrEmpty(q)
                         || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || e.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || e.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .ToList();
    }

    // ── Load from embedded resource ───────────────────────────────────────

    private static IReadOnlyList<WeaponLibraryEntry> Load()
    {
        var asm          = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("weapon-library.json"));

        if (resourceName == null)
            return [];

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Deserialize<List<WeaponLibraryEntry>>(stream, options) ?? [];
    }
}
