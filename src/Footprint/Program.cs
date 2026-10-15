using Footprint.Core;

namespace Footprint;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Handle elevated execution before constructing WPF or starting its dispatcher.
        if (args.Length > 0 && args[0] == ElevatedCommandRunner.WorkerArgument)
        {
            if (args.Length != 3 || !int.TryParse(args[2], out var parent)) return 1;
            try { return ElevatedCommandRunner.RunWorkerAsync(args[1], parent).GetAwaiter().GetResult(); }
            catch (Exception) { return 1; }
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
