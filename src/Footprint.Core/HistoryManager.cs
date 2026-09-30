namespace Footprint.Core;

/// <summary>Coordinates history changes and the backups that protect them.</summary>
public sealed class HistoryManager(HistoryStore store)
{
    public async Task RestoreAsync(List<CommandRecord> current, List<CommandRecord> replacement, DateOnly today)
    {
        await store.SaveDailyBackupAsync(current, today);
        await store.ReplaceAsync(current, replacement);
    }

    public async Task ClearNonFavoritesAsync(List<CommandRecord> current, DateOnly today)
    {
        await store.SaveDailyBackupAsync(current, today);
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
