using System;
using System.Linq;
using System.Threading;
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
    // Per-user-session single-instance guard. Held for the whole process lifetime; the OS releases it
    // automatically when the process exits — including Velopack's update-restart, which exits the old
    // process before launching the new build, so there is no hand-off race.
    private static Mutex? _instanceMutex;
    private const string InstanceName = @"Local\BetterBar.SingleInstance.8116f400-a45e-44c1-811a";

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack hooks (install/update/uninstall) run and exit here before WPF starts. They are NOT
        // subject to the single-instance guard below, because Run() exits the process on a hook invocation.
        VelopackApp.Build().Run();

        // Only one copy of BetterBar may run per user session. A second launch (double-click, autostart
        // firing twice, or `--setup` while it's already running) simply exits. We never Wait on the mutex,
        // so an abandoned mutex from a crashed instance can't throw — the name is just freed by the OS.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool createdNew);
        if (!createdNew) return;

        // "--setup" (or "/setup") forces the first-run setup wizard even when a configuration already
        // exists — completing it replaces the bottom bar on the primary monitor. Read here and handed to
        // App so it can decide before the normal boot sequence runs. (Requires BetterBar to not already be
        // running — see the single-instance guard above.)
        App.ForceSetupWizard = args.Any(a =>
            a.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/setup",  StringComparison.OrdinalIgnoreCase));

        var app = new App();
        app.InitializeComponent();
        app.Run();

        GC.KeepAlive(_instanceMutex);
    }
}
