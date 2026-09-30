using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Footprint.Core;

public sealed class HistoryStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(CommandRecord record)
    {
        Directory.CreateDirectory(directory);
        await WriteJsonAsync(Path.Combine(directory, $"{record.Id:N}.json"), record);
    }

    public Task DeleteAsync(CommandRecord record)
    {
        var path = Path.Combine(directory, $"{record.Id:N}.json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task EnsureDailyBackupAsync(IEnumerable<CommandRecord> records, DateOnly date)
    {
        var path = BackupPath(date);
        if (!File.Exists(path)) await SaveDailyBackupAsync(records, date);
    }

    public Task SaveDailyBackupAsync(IEnumerable<CommandRecord> records, DateOnly date) =>
        WriteJsonAsync(BackupPath(date), records.ToList());

    public Task<List<DateOnly>> LoadBackupDatesAsync()
    {
        if (!Directory.Exists(BackupDirectory)) return Task.FromResult(new List<DateOnly>());
        var dates = Directory.EnumerateFiles(BackupDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : (DateOnly?)null)
            .Where(date => date is not null)
            .Select(date => date!.Value)
            .OrderByDescending(date => date)
            .ToList();
        return Task.FromResult(dates);
    }

    public async Task<List<CommandRecord>> LoadDailyBackupAsync(DateOnly date)
    {
        var records = JsonSerializer.Deserialize<List<CommandRecord>>(
            await File.ReadAllTextAsync(BackupPath(date)), Options) ?? throw new JsonException("Backup is empty.");
        if (records.Any(record => !IsValid(record))) throw new JsonException("Backup contains an invalid command record.");
        return records.OrderByDescending(record => record.StartedAt).ToList();
    }

    public async Task ReplaceAsync(IEnumerable<CommandRecord> existing, IEnumerable<CommandRecord> replacement)
    {
        var records = replacement.GroupBy(record => record.Id).Select(group => group.First()).ToList();
        if (records.Any(record => !IsValid(record))) throw new ArgumentException("Replacement contains an invalid command record.");
        foreach (var record in records) await SaveAsync(record);
        var replacementIds = records.Select(record => record.Id).ToHashSet();
        foreach (var record in existing.Where(record => !replacementIds.Contains(record.Id))) await DeleteAsync(record);
    }

    public async Task<(List<CommandRecord> Records, int Skipped)> LoadAsync()
    {
        var records = new List<CommandRecord>();
        var skipped = 0;
        if (!Directory.Exists(directory)) return (records, skipped);
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<CommandRecord>(await File.ReadAllTextAsync(path), Options);
                if (record is null || !IsValid(record))
                    throw new JsonException("Invalid command record.");
                records.Add(record);
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }
        return (records.OrderByDescending(record => record.StartedAt).ToList(), skipped);
    }

    private string BackupDirectory => Path.Combine(directory, "Backups");
    private string BackupPath(DateOnly date) => Path.Combine(BackupDirectory,
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    private static bool IsValid(CommandRecord? record) => record is not null && record.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(record.Command) && !string.IsNullOrWhiteSpace(record.WorkingDirectory)
        && record.Output is not null && Enum.IsDefined(record.Shell) && Enum.IsDefined(record.Status);

    private static async Task WriteJsonAsync<T>(string target, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
