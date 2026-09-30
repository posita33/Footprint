using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var settings = ((App)Application.Current).Settings;
        ThemeBox.SelectedIndex = settings.DarkTheme ? 1 : 0;
        FontSizeSlider.Value = settings.CommandFontSize;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ThemeBox.SelectedIndex = 0;
        FontSizeSlider.Value = 12;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ((App)Application.Current).SaveSettings(new AppearanceSettings
            {
                DarkTheme = ThemeBox.SelectedIndex == 1,
                CommandFontSize = FontSizeSlider.Value
            });
            DialogResult = true;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "設定を保存できませんでした。\n" + error.Message,
                "Footprint — 設定", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
