using System.Text.Json;

namespace Footprint.Core;

public sealed class AppearanceSettings
{
    public bool DarkTheme { get; set; }
    public double CommandFontSize { get; set; } = 12;

    public bool IsValid() => double.IsFinite(CommandFontSize) && CommandFontSize is >= 12 and <= 24;
}

public sealed class AppearanceSettingsStore(string path)
{
    public AppearanceSettings Load()
    {
        if (!File.Exists(path)) return new AppearanceSettings();
        var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path));
        if (settings is null || !settings.IsValid()) throw new JsonException("外観設定が不正です。");
        return settings;
    }

    public void Save(AppearanceSettings settings)
    {
        if (!settings.IsValid()) throw new ArgumentException("文字サイズは12～24に指定してください。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
