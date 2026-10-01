using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private void RequestIssue_Click(object sender, RoutedEventArgs e) => ShowRequests(false);
    private void ImplementIssue_Click(object sender, RoutedEventArgs e) => ShowRequests(true);
    private void ShowRequests(bool implementation) => new RequestWindow(implementation,
        () => UpdateApplication_Click(this, new RoutedEventArgs())) { Owner = this }.ShowDialog();
}
