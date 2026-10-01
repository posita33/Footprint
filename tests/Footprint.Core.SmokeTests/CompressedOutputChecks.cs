using System.Text.Json;
using System.Text.Json.Nodes;
using Footprint.Core;

internal static class CompressedOutputChecks
{
    public static async Task RunAsync(string directory)
    {
        var folder = Path.Combine(directory, "compressed");
        var text = string.Concat(Enumerable.Repeat("日本語の出力😀\r\n", 200_000)) + "COMPLETE_TAIL";
        var record = new CommandRecord { Command = "large output", WorkingDirectory = directory, Output = text, IsFavorite = true };
        var store = new HistoryStore(folder);
        await store.SaveAsync(record);
        var file = Path.Combine(folder, $"{record.Id:N}.json");
        Require(new FileInfo(file).Length < text.Length / 20, "Repetitive multi-million-character output must compress substantially.");
        Require((await store.LoadAsync()).Records.Single().Output == text, "History must decompress the entire output exactly.");
        var date = DateOnly.FromDateTime(record.StartedAt.LocalDateTime);
        await store.SaveBackupsAsync([record], date);
        Require((await store.LoadDailyBackupAsync(date)).Single().Output == text &&
            (await store.LoadFavoriteBackupAsync(date)).Single().Output == text, "Both backup types must preserve full compressed output.");
        var sessionStore = new WorkspaceSessionStore(Path.Combine(folder, "session", "Session.json"));
        sessionStore.Save(new WorkspaceSession { Workspaces = [new WorkspaceState { Output = text }] });
        Require(sessionStore.Load()!.Workspaces.Single().Output == text, "Session restore must decompress complete output.");

        var pages = new OutputPages();
        pages.Reset(new string('a', OutputPages.PageSize - 1) + "😀" + new string('b', OutputPages.PageSize - 2) + "\r\nTAIL");
        Require(string.Concat(Enumerable.Range(0, pages.PageCount).Select(pages.GetPage)) == pages.FullText,
            "Surrogate pairs and CRLF boundaries must reconstruct without losing or duplicating characters.");
        Require(Enumerable.Range(0, pages.PageCount).All(i => pages.GetPage(i).Length <= OutputPages.PageSize + 1),
            "Every textbox page must stay bounded.");
        pages.Append("😀 more");
        Require(string.Concat(Enumerable.Range(0, pages.PageCount).Select(pages.GetPage)) == pages.FullText,
            "Appending must preserve page boundaries and full text.");
        pages.Reset(new string('x', OutputPages.PageSize - 1) + "😀");
        Require(pages.PageCount == 1 && pages.GetPage(0).EndsWith("😀"), "A boundary-adjusted final character must not create an empty page.");
        pages.Reset("");
        Require(pages.PageCount == 1 && pages.GetPage(0) == "", "Empty output must be a single empty page.");
        pages.Reset(new string('x', OutputPages.PageSize));
        Require(pages.PageCount == 1, "An exact page boundary must not create an empty trailing page.");

        var original = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        var legacy = original.DeepClone().AsObject();
        legacy["Output"] = "旧形式の出力\r\n";
        await File.WriteAllTextAsync(file, legacy.ToJsonString());
        Require((await store.LoadAsync()).Records.Single().Output == "旧形式の出力\r\n", "Legacy string output must remain readable.");
        var legacyBackup = Path.Combine(folder, "Backups", date.ToString("yyyy-MM-dd") + ".json");
        await File.WriteAllTextAsync(legacyBackup, new JsonArray(legacy.DeepClone()).ToJsonString());
        Require((await store.LoadDailyBackupAsync(date)).Single().Output == "旧形式の出力\r\n", "Legacy backups must remain readable.");
        await File.WriteAllTextAsync(Path.Combine(folder, "session", "Session.json"),
            JsonSerializer.Serialize(new { ActiveIndex = 0, Workspaces = new[] { new { Title = "旧タブ", ShellIndex = 0, WorkingDirectory = directory, Command = "echo", Output = "legacy session" } } }));
        Require(sessionStore.Load()!.Workspaces[0].Output == "legacy session", "Legacy sessions must remain readable.");

        foreach (var field in new[] { "data", "length", "sha256", "encoding" })
        {
            var corrupt = original.DeepClone().AsObject();
            corrupt["Output"]![field] = field == "length" ? JsonValue.Create(1) : JsonValue.Create("invalid");
            await File.WriteAllTextAsync(file, corrupt.ToJsonString());
            var loaded = await store.LoadAsync();
            Require(loaded.Skipped == 1 && loaded.Records.Count == 0 && File.Exists(file), "Corrupt compressed history must be preserved and reported.");
        }
        Console.WriteLine("PASS: multi-million-character compression, full history/backups/session restore, legacy formats, corruption handling and Unicode paging");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
