namespace Footprint.Core;

/// <summary>Coordinates history changes and the backups that protect them.</summary>
public sealed class HistoryManager(HistoryStore store)
{
    public async Task RestoreAsync(List<CommandRecord> current, List<CommandRecord> replacement, DateOnly today)
    {
        await store.SaveBackupsAsync(current, today);
        await store.ReplaceAsync(current, replacement);
    }

    public async Task<List<CommandRecord>> RestoreFavoritesAsync(List<CommandRecord> current,
        List<CommandRecord> favorites, DateOnly today)
    {
        if (favorites.Any(record => !record.IsFavorite))
            throw new ArgumentException("Favorite backup contains a non-favorite record.");
        await store.SaveBackupsAsync(current, today);
        var ids = favorites.Select(record => record.Id).ToHashSet();
        var replacement = current.Select(record => System.Text.Json.JsonSerializer.Deserialize<CommandRecord>(
            System.Text.Json.JsonSerializer.Serialize(record))!).ToList();
        foreach (var record in replacement) record.IsFavorite = ids.Contains(record.Id);
        replacement.AddRange(favorites.Where(record => replacement.All(existing => existing.Id != record.Id)));
        await store.ReplaceAsync(current, replacement);
        return replacement.OrderByDescending(record => record.StartedAt).ToList();
    }

    public async Task ClearNonFavoritesAsync(List<CommandRecord> current, DateOnly today)
    {
        await store.SaveBackupsAsync(current, today);
        foreach (var record in current.Where(record => !record.IsFavorite).ToList())
            await store.DeleteAsync(record);
        current.RemoveAll(record => !record.IsFavorite);
    }

    public async Task ToggleFavoriteAsync(CommandRecord record)
    {
        var previous = record.IsFavorite;
        record.IsFavorite = !previous;
        try { await store.SaveAsync(record); }
        catch
        {
            record.IsFavorite = previous;
            throw;
        }
    }
}
