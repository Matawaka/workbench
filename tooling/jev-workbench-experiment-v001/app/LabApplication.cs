using System.Windows;

namespace Matawaka.Workbench.JevLab;

// Dedicated experimental entry point. Does not instantiate production MainWindow,
// CommandRouter, settings, AgentHost, maintenance services or a model provider.
internal sealed class LabApplication : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? manifest = null, smokeOutput = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length) return 2;
            if (args[i] == "--manifest" && manifest is null) manifest = args[++i];
            else if (args[i] == "--smoke" && smokeOutput is null) smokeOutput = args[++i];
            else return 2;
        }
        if (smokeOutput is not null && manifest is not null) return 2;
        var app = new LabApplication { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new JevLabWindow();
        app.MainWindow = window;
        window.ContentRendered += async (_, _) =>
        {
            if (window.Started) return;
            window.Started = true;
            if (smokeOutput is not null)
            {
                try { await LabSmoke.RunAsync(window, smokeOutput); app.Shutdown(0); }
                catch { app.Shutdown(1); }
            }
            else if (manifest is not null) await window.LoadObservationAsync(manifest);
        };
        return app.Run(window);
    }
}
