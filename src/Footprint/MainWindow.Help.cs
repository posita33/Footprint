using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
}
