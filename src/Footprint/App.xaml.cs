using System.Windows;
using System.IO;
using Footprint.Core;

namespace Footprint;

public partial class App : Application
{
    private readonly AppearanceSettingsStore _settingsStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "Settings.json"));
    internal AppearanceSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == ElevatedCommandRunner.WorkerArgument)
        {
            try { Shutdown(e.Args.Length == 3 && int.TryParse(e.Args[2], out var parent)
                    ? Task.Run(() => ElevatedCommandRunner.RunWorkerAsync(e.Args[1], parent)).GetAwaiter().GetResult() : 1); }
            catch (Exception) { Shutdown(1); }
            return;
        }
        try { Settings = _settingsStore.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show("設定を読み込めないため、標準の外観で起動します。\n" + error.Message,
                "Footprint — 設定", MessageBoxButton.OK, MessageBoxImage.Warning);
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
