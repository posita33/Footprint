using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private void RequestIssue_Click(object sender, RoutedEventArgs e) =>
        new RequestWindow { Owner = this }.ShowDialog();
}
