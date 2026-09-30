using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
}
