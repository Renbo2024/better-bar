using System;
using System.Linq;
using Velopack;

namespace BetterBarApp;

/// <summary>
/// Explicit application entry point so the Velopack install / update / uninstall hooks run at the
/// very start of <c>Main</c> — before WPF initializes. On a hook invocation, <c>Run()</c> performs
/// its work and exits the process; on a normal launch it returns immediately and the WPF app starts.
/// (Selected via <c>&lt;StartupObject&gt;</c> in the .csproj so it supersedes the auto-generated
/// WPF entry point.)
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // "--setup" (or "/setup") forces the first-run setup wizard even when a configuration already
        // exists — completing it replaces the bottom bar on the primary monitor. Read here and handed to
        // App so it can decide before the normal boot sequence runs.
        App.ForceSetupWizard = args.Any(a =>
            a.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/setup",  StringComparison.OrdinalIgnoreCase));

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
