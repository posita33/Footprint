using System.Text.Json;
using System.Text.Json.Serialization;

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
        var target = Path.Combine(directory, $"{record.Id:N}.json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, Options));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
                if (record is null || record.Id == Guid.Empty || string.IsNullOrWhiteSpace(record.Command)
                    || string.IsNullOrWhiteSpace(record.WorkingDirectory) || record.Output is null
                    || !Enum.IsDefined(record.Shell) || !Enum.IsDefined(record.Status))
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
}
