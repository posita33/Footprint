using System.Windows;
using System.IO;
using UEUtraceAnalyzer.Core;

namespace UEUtraceAnalyzer;

public partial class App : Application
{
    private readonly AppearanceSettingsStore _settingsStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UEUtraceAnalyzer", "Settings.json"));
    internal AppearanceSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        try { Settings = _settingsStore.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show("設定を読み込めないため、標準の外観で起動します。\n" + error.Message,
                "UEUtraceAnalyzer — 設定", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Appearance.Apply(Settings);
        base.OnStartup(e);
    }

    internal void SaveSettings(AppearanceSettings settings)
    {
        _settingsStore.Save(settings);
        Settings = settings;
        Appearance.Apply(settings);
    }
}
